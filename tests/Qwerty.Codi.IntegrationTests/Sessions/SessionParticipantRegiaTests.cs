using Qwerty.Codi.Application.Dto;
using System.Data.Common;
using System.Globalization;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Lessons;
using Qwerty.Codi.Application.Organization;
using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.Domain.Sessions;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Sessions;

/// <summary>
/// Regia dei partecipanti (§10.4, §9.5) dal percorso reale: correzione di nome e postazione ed
/// esclusione. I comandi modificano una riga esistente, quindi dichiarano la versione attesa
/// (<c>baseRowVersion</c>): i test coprono il percorso positivo, l'idempotenza dell'esclusione e i
/// rifiuti previsti — versione mancante o superata, nome inutilizzabile, postazione inesistente o di
/// un'altra classe, sessione di un altro amministratore, partecipante inesistente, assenza di
/// identità.
///
/// Lo stato di regia è quello della credenziale verificata dal trasporto: qui il contesto è
/// dichiarato dal caso di prova, mentre nel servizio arriva dal cookie amministrativo.
/// </summary>
public sealed class SessionParticipantRegiaTests : IAsyncLifetime
{
    private CodiUseCaseHost _host = null!;

    private Guid _adminId;

    public async Task InitializeAsync()
    {
        _host = await CodiUseCaseHost.CreateAsync();
        _adminId = await InsertAdminUserAsync();
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateParticipant_CorreggeNomeEPostazione()
    {
        // Arrange: Luca entra senza postazione, Anna da un PC riconosciuto; il maestro sposta Luca
        // su quella postazione e ne corregge il nome (§9.5).
        var fixture = await StartedSessionAsync();
        var luca = await JoinAsync(fixture, "Luca");
        var anna = await JoinAsync(
            fixture, "Anna", browserKey: "chiave-casuale-anna", idempotencyKey: "chiave-ingresso-anna");
        var annaStation = await StationIdAsync(anna.Joined.ParticipantId);

        // Act
        var updated = await _host.CommandAsAsync<SessionParticipantDto>(
            UpdateParticipantCommand.RequestName,
            new
            {
                sessionId = fixture.SessionId,
                participantId = luca.Joined.ParticipantId,
                displayName = "  Luca Rossi  ",
                stationId = annaStation,
                baseRowVersion = 1L,
            },
            RegiaContext());

        // Assert: il nome è ripulito, la postazione è quella indicata e la versione avanza.
        Assert.Equal("Luca Rossi", updated.DisplayName);
        Assert.Equal(annaStation, updated.StationId);
        Assert.Equal("Admitted", updated.AdmissionStatus);
        Assert.Equal(2, updated.RowVersion);

        var row = await ColumnsAsync(
            "SELECT display_name, station_id, row_version FROM session_participant WHERE id = $id;",
            luca.Joined.ParticipantId);
        Assert.Equal("Luca Rossi", row[0]);
        Assert.Equal(annaStation.ToString("D"), row[1]);
        Assert.Equal(2L, row[2]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateParticipant_SenzaVersioneAttesa_RifiutaConVersioneRichiesta()
    {
        // Arrange: la versione è facoltativa nel record, quindi il corpo è deserializzabile e la
        // sua assenza ha l'esito 428 di §10.2.
        var fixture = await StartedSessionAsync();
        var luca = await JoinAsync(fixture, "Luca");

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            UpdateParticipantCommand.RequestName,
            new { sessionId = fixture.SessionId, participantId = luca.Joined.ParticipantId, displayName = "Luca" },
            RegiaContext());

        // Assert
        Assert.Equal(CodiErrorCodes.EntityVersionRequired, failure.Code);
        Assert.Equal(
            "Luca",
            await ScalarAsync<string>(
                $"SELECT display_name FROM session_participant WHERE id = '{luca.Joined.ParticipantId:D}';"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateParticipant_ConVersioneSuperata_RifiutaSenzaSovrascrivere()
    {
        // Arrange
        var fixture = await StartedSessionAsync();
        var luca = await JoinAsync(fixture, "Luca");

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            UpdateParticipantCommand.RequestName,
            new
            {
                sessionId = fixture.SessionId,
                participantId = luca.Joined.ParticipantId,
                displayName = "Luca Rossi",
                baseRowVersion = 99L,
            },
            RegiaContext());

        // Assert: la riga non è stata toccata.
        Assert.Equal(CodiErrorCodes.RowVersionMismatch, failure.Code);
        Assert.Equal(
            "Luca",
            await ScalarAsync<string>(
                $"SELECT display_name FROM session_participant WHERE id = '{luca.Joined.ParticipantId:D}';"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateParticipant_ConNomeNonUtilizzabile_RifiutaConNomeNonValido()
    {
        // Arrange: il limite è di trenta grafemi (§7.4).
        var fixture = await StartedSessionAsync();
        var luca = await JoinAsync(fixture, "Luca");

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            UpdateParticipantCommand.RequestName,
            new
            {
                sessionId = fixture.SessionId,
                participantId = luca.Joined.ParticipantId,
                displayName = new string('a', ParticipantName.MaxGraphemes + 1),
                baseRowVersion = 1L,
            },
            RegiaContext());

        // Assert
        Assert.Equal(CodiErrorCodes.InvalidName, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateParticipant_ConPostazioneInesistente_RifiutaConRisorsaAssente()
    {
        // Arrange
        var fixture = await StartedSessionAsync();
        var luca = await JoinAsync(fixture, "Luca");

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            UpdateParticipantCommand.RequestName,
            new
            {
                sessionId = fixture.SessionId,
                participantId = luca.Joined.ParticipantId,
                displayName = "Luca",
                stationId = Guid.NewGuid(),
                baseRowVersion = 1L,
            },
            RegiaContext());

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateParticipant_ConPostazioneDiUnAltraClasse_RifiutaConRisorsaAssente()
    {
        // Arrange: un PC di un'altra classe non è una postazione di questa lezione.
        var fixture = await StartedSessionAsync();
        var luca = await JoinAsync(fixture, "Luca");
        var otherClassroom = await CreateClassroomAsync(fixture.SchoolId, "Terza B");
        var foreignStation = await InsertStationAsync(otherClassroom);

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            UpdateParticipantCommand.RequestName,
            new
            {
                sessionId = fixture.SessionId,
                participantId = luca.Joined.ParticipantId,
                displayName = "Luca",
                stationId = foreignStation,
                baseRowVersion = 1L,
            },
            RegiaContext());

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateParticipant_SuSessioneDiUnAltroAmministratore_RifiutaConRisorsaAssente()
    {
        // Arrange: la sessione di un altro amministratore è indistinguibile da una inesistente.
        var fixture = await StartedSessionAsync();
        var luca = await JoinAsync(fixture, "Luca");
        var otherAdmin = new CodiRequestContext(
            isAuthenticated: true,
            [CodiPolicy.Admin, CodiPolicy.SessionController],
            adminUserId: Guid.NewGuid());

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            UpdateParticipantCommand.RequestName,
            new
            {
                sessionId = fixture.SessionId,
                participantId = luca.Joined.ParticipantId,
                displayName = "Luca",
                baseRowVersion = 1L,
            },
            otherAdmin);

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateParticipant_ConPartecipanteInesistente_RifiutaConRisorsaAssente()
    {
        // Arrange
        var fixture = await StartedSessionAsync();

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            UpdateParticipantCommand.RequestName,
            new
            {
                sessionId = fixture.SessionId,
                participantId = Guid.NewGuid(),
                displayName = "Luca",
                baseRowVersion = 1L,
            },
            RegiaContext());

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateParticipant_SenzaIdentita_RifiutaConAccessoRichiesto()
    {
        // Arrange
        var fixture = await StartedSessionAsync();
        var luca = await JoinAsync(fixture, "Luca");

        // Act: il contesto anonimo non concede la policy di regia; il rifiuto precede l'handler.
        var failure = await _host.ExpectFailureAsAsync(
            UpdateParticipantCommand.RequestName,
            new
            {
                sessionId = fixture.SessionId,
                participantId = luca.Joined.ParticipantId,
                displayName = "Luca",
                baseRowVersion = 1L,
            },
            CodiUseCaseHost.JoinContext("scope-di-prova", "chiave-di-prova"));

        // Assert
        Assert.Equal(CodiErrorCodes.AuthenticationRequired, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExcludeParticipant_RevocaLaPartecipazioneSenzaCancellarla()
    {
        // Arrange
        var fixture = await StartedSessionAsync();
        var luca = await JoinAsync(fixture, "Luca");

        // Act
        var excluded = await _host.CommandAsAsync<SessionParticipantDto>(
            ExcludeParticipantCommand.RequestName,
            new
            {
                sessionId = fixture.SessionId,
                participantId = luca.Joined.ParticipantId,
                baseRowVersion = 1L,
            },
            RegiaContext());

        // Assert: la partecipazione resta (la riga non si cancella), l'ammissione diventa Excluded e
        // la versione avanza; l'istante di esclusione è quello dell'operazione.
        Assert.Equal("Excluded", excluded.AdmissionStatus);
        Assert.Equal(CodiUseCaseHost.Now, excluded.ExcludedAtUtc);
        Assert.Equal(2, excluded.RowVersion);
        Assert.Equal(1L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));

        var row = await ColumnsAsync(
            "SELECT admission_status, excluded_at_utc_ms, row_version FROM session_participant WHERE id = $id;",
            luca.Joined.ParticipantId);
        Assert.Equal("Excluded", row[0]);
        Assert.Equal(CodiUseCaseHost.Now.ToUnixTimeMilliseconds(), Convert.ToInt64(row[1], CultureInfo.InvariantCulture));
        Assert.Equal(2L, row[2]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExcludeParticipant_RipetutoNonIncrementaLaVersione()
    {
        // Arrange: escludere chi è già escluso non cambia nulla, come l'archiviazione ripetuta.
        var fixture = await StartedSessionAsync();
        var luca = await JoinAsync(fixture, "Luca");
        var first = await ExcludeAsync(fixture, luca.Joined.ParticipantId, baseRowVersion: 1L);

        // Act
        var second = await ExcludeAsync(
            fixture, luca.Joined.ParticipantId, baseRowVersion: first.RowVersion);

        // Assert
        Assert.Equal("Excluded", second.AdmissionStatus);
        Assert.Equal(first.RowVersion, second.RowVersion);
        Assert.Equal(first.ExcludedAtUtc, second.ExcludedAtUtc);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExcludeParticipant_SenzaVersioneAttesa_RifiutaConVersioneRichiesta()
    {
        // Arrange
        var fixture = await StartedSessionAsync();
        var luca = await JoinAsync(fixture, "Luca");

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            ExcludeParticipantCommand.RequestName,
            new { sessionId = fixture.SessionId, participantId = luca.Joined.ParticipantId },
            RegiaContext());

        // Assert
        Assert.Equal(CodiErrorCodes.EntityVersionRequired, failure.Code);
        Assert.Equal(
            "Admitted",
            await ScalarAsync<string>(
                $"SELECT admission_status FROM session_participant WHERE id = '{luca.Joined.ParticipantId:D}';"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExcludeParticipant_ConVersioneSuperata_RifiutaSenzaSovrascrivere()
    {
        // Arrange
        var fixture = await StartedSessionAsync();
        var luca = await JoinAsync(fixture, "Luca");

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            ExcludeParticipantCommand.RequestName,
            new { sessionId = fixture.SessionId, participantId = luca.Joined.ParticipantId, baseRowVersion = 99L },
            RegiaContext());

        // Assert
        Assert.Equal(CodiErrorCodes.RowVersionMismatch, failure.Code);
        Assert.Equal(
            "Admitted",
            await ScalarAsync<string>(
                $"SELECT admission_status FROM session_participant WHERE id = '{luca.Joined.ParticipantId:D}';"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExcludeParticipant_SuSessioneDiUnAltroAmministratore_RifiutaConRisorsaAssente()
    {
        // Arrange
        var fixture = await StartedSessionAsync();
        var luca = await JoinAsync(fixture, "Luca");
        var otherAdmin = new CodiRequestContext(
            isAuthenticated: true,
            [CodiPolicy.Admin, CodiPolicy.SessionController],
            adminUserId: Guid.NewGuid());

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            ExcludeParticipantCommand.RequestName,
            new { sessionId = fixture.SessionId, participantId = luca.Joined.ParticipantId, baseRowVersion = 1L },
            otherAdmin);

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExcludeParticipant_SenzaIdentita_RifiutaConAccessoRichiesto()
    {
        // Arrange
        var fixture = await StartedSessionAsync();
        var luca = await JoinAsync(fixture, "Luca");

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            ExcludeParticipantCommand.RequestName,
            new { sessionId = fixture.SessionId, participantId = luca.Joined.ParticipantId, baseRowVersion = 1L },
            CodiUseCaseHost.JoinContext("scope-di-prova", "chiave-di-prova"));

        // Assert
        Assert.Equal(CodiErrorCodes.AuthenticationRequired, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateParticipant_SuSessioneInesistente_RifiutaConRisorsaAssente()
    {
        // Arrange
        await StartedSessionAsync();

        // Act: una sessione inesistente è indistinguibile da una di un altro amministratore.
        var failure = await _host.ExpectFailureAsAsync(
            UpdateParticipantCommand.RequestName,
            new
            {
                sessionId = Guid.NewGuid(),
                participantId = Guid.NewGuid(),
                displayName = "Luca",
                baseRowVersion = 1L,
            },
            RegiaContext());

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExcludeParticipant_SuSessioneInesistente_RifiutaConRisorsaAssente()
    {
        // Arrange
        await StartedSessionAsync();

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            ExcludeParticipantCommand.RequestName,
            new { sessionId = Guid.NewGuid(), participantId = Guid.NewGuid(), baseRowVersion = 1L },
            RegiaContext());

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    private Task<SessionParticipantDto> ExcludeAsync(Fixture fixture, Guid participantId, long baseRowVersion) =>
        _host.CommandAsAsync<SessionParticipantDto>(
            ExcludeParticipantCommand.RequestName,
            new { sessionId = fixture.SessionId, participantId, baseRowVersion },
            RegiaContext());

    /// <summary>Catena reale con la sessione in play: è la condizione perché si possa entrare.</summary>
    private async Task<Fixture> StartedSessionAsync()
    {
        var school = await _host.CommandAsAsync<SchoolDto>(
            CreateSchoolCommand.RequestName, new { name = "Scuola Manzoni" }, RegiaContext());
        var classroomId = await CreateClassroomAsync(school.Id, "Terza A");
        var course = await _host.CommandAsAsync<CourseDto>(
            CreateCourseCommand.RequestName, new { title = "Informatica di base" }, RegiaContext());
        var lesson = await _host.CommandAsAsync<LessonDto>(
            CreateLessonCommand.RequestName, new { courseId = course.Id, title = "Prima lezione" }, RegiaContext());
        var session = await _host.CommandAsAsync<SessionDto>(
            CreateSessionCommand.RequestName,
            new { lessonId = lesson.Id, classroomId },
            RegiaContext());
        await _host.CommandAsAsync<SessionDto>(
            StartSessionCommand.RequestName,
            new { sessionId = session.Id, expectedSessionRevision = 1L },
            RegiaContext());

        return new Fixture(school.Id, classroomId, session.Id);
    }

    private async Task<Guid> CreateClassroomAsync(Guid schoolId, string className)
    {
        var classroom = await _host.CommandAsAsync<ClassroomDto>(
            CreateClassroomCommand.RequestName,
            new { schoolId, name = className, schoolYear = "2026/2027" },
            RegiaContext());

        return classroom.Id;
    }

    private Task<JoinSessionOutcome> JoinAsync(
        Fixture fixture,
        string displayName,
        string? browserKey = null,
        string idempotencyKey = "chiave-di-prova") =>
        _host.CommandAsAsync<JoinSessionOutcome>(
            JoinSessionCommand.RequestName,
            new
            {
                schoolId = fixture.SchoolId,
                classroomId = fixture.ClassroomId,
                sessionId = fixture.SessionId,
                displayName,
                browserKey,
            },
            CodiUseCaseHost.JoinContext("scope-di-prova", idempotencyKey));

    /// <summary>Contesto di regia: la credenziale amministrativa concede anche SessionController (D3).</summary>
    private CodiRequestContext RegiaContext() =>
        new(
            isAuthenticated: true,
            [CodiPolicy.Admin, CodiPolicy.SessionController],
            adminUserId: _adminId);

    private async Task<Guid> StationIdAsync(Guid participantId) =>
        Guid.Parse(await ScalarAsync<string>(
            $"SELECT station_id FROM session_participant WHERE id = '{participantId:D}';"));

    /// <summary>
    /// Inserisce una postazione in una classe arbitraria: serve a un PC che non appartiene alla
    /// classe della sessione, condizione che il percorso reale non produce.
    /// </summary>
    private async Task<Guid> InsertStationAsync(Guid classroomId)
    {
        var id = Guid.NewGuid();
        await ExecuteSqlAsync(
            $"""
            INSERT INTO station (
                id, classroom_id, browser_key_hash, last_seen_at_utc_ms, created_at_utc_ms, updated_at_utc_ms)
            VALUES ('{id:D}', '{classroomId:D}', 'impronta-{id:N}', 1000, 1000, 1000);
            """);

        return id;
    }

    /// <summary>
    /// Inserisce una riga in <c>admin_user</c>: la composizione dei casi d'uso non registra
    /// l'impronta delle password, ma la chiave esterna della sessione richiede l'amministratore.
    /// </summary>
    private async Task<Guid> InsertAdminUserAsync()
    {
        var id = Guid.NewGuid();

        await using var connection = await _host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO admin_user (
                id, username, normalized_username, password_hash, security_stamp,
                is_active, failed_login_count, created_at_utc_ms, updated_at_utc_ms)
            VALUES ($id, 'Maestra', 'MAESTRA-' || $id, 'hash-di-prova', 'stamp-di-prova', 1, 0, 1000, 1000);
            """;
        AddParameter(command, "$id", id.ToString("D"));

        await command.ExecuteNonQueryAsync();

        return id;
    }

    /// <summary>Valori della prima riga, nell'ordine delle colonne selezionate.</summary>
    private async Task<object[]> ColumnsAsync(string sql, Guid id)
    {
        await using var connection = await _host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "$id", id.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "La riga attesa non esiste.");

        var values = new object[reader.FieldCount];
        reader.GetValues(values);

        return values;
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await _host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();

        return (T)Convert.ChangeType(value!, typeof(T), CultureInfo.InvariantCulture);
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = await _host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    /// <summary>Scuola, classe e sessione scelte dal client: la catena che l'ingresso deve verificare.</summary>
    private sealed record Fixture(Guid SchoolId, Guid ClassroomId, Guid SessionId);
}
