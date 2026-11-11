using System.Data.Common;
using Microsoft.Data.Sqlite;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Sessions;
using Qwerty.Codi.Infrastructure.Migrations;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Persistence;

/// <summary>
/// Persistenza di partecipanti e postazioni (§7.2, §7.4) su un database temporaneo migrato (§20.3):
/// round trip dei campi, nome ripulito, esclusione, controllo di <c>row_version</c>, unicità della
/// chiave del browser, chiave esterna verso la sessione e la postazione, e lettura delle
/// partecipazioni di una sola sessione.
/// </summary>
public sealed class SessionParticipantRepositoryTests : IAsyncLifetime
{
    /// <summary>Codice esteso SQLITE_CONSTRAINT_FOREIGNKEY: il riferimento non esiste.</summary>
    private const int ForeignKeyViolationCode = 787;

    /// <summary>Codice esteso SQLITE_CONSTRAINT_UNIQUE: la seconda riga collide sull'indice.</summary>
    private const int UniqueViolationCode = 2067;

    private const string SchoolId = "a0000000-0000-0000-0000-000000000001";
    private const string ClassroomId = "a0000000-0000-0000-0000-000000000002";
    private const string OtherClassroomId = "a0000000-0000-0000-0000-000000000003";
    private const string CourseId = "a0000000-0000-0000-0000-000000000004";
    private const string LessonId = "a0000000-0000-0000-0000-000000000005";
    private const string AdminId = "b0000000-0000-0000-0000-000000000001";
    private const string SessionId = "c0000000-0000-0000-0000-000000000001";
    private const string OtherSessionId = "c0000000-0000-0000-0000-000000000002";
    private const string StationId = "d0000000-0000-0000-0000-000000000001";
    private const string MissingStationId = "d0000000-0000-0000-0000-000000000099";

    private const string BrowserKeyHash = "impronta-pc-1";

    private const long NowMs = 1_775_000_000_000;

    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(NowMs);

    private TempSqliteDatabase _database = null!;

    public async Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();
        await new MigrationRunner(_database.Factory, new TestTimeProvider()).ApplyAsync(
            MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly),
            CancellationToken.None);
        Assert.Equal("wal", await _database.Factory.EnableWriteAheadLoggingAsync(CancellationToken.None));

        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO school (id, name, normalized_name, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($school, 'Scuola Manzoni', 'SCUOLA MANZONI', $now, $now);
            INSERT INTO classroom (id, school_id, name, normalized_name, school_year, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($classroom, $school, 'Terza A', 'TERZA A', '2026/2027', $now, $now),
                       ($otherClassroom, $school, 'Terza B', 'TERZA B', '2026/2027', $now, $now);
            INSERT INTO course (id, title, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($course, 'Informatica', $now, $now);
            INSERT INTO lesson (id, course_id, title, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($lesson, $course, 'Lezione', $now, $now);
            INSERT INTO admin_user (
                id, username, normalized_username, password_hash, security_stamp,
                is_active, failed_login_count, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($admin, 'Maestra', 'MAESTRA', 'hash-di-prova', 'stamp-di-prova', 1, 0, $now, $now);
            INSERT INTO lesson_session (
                id, lesson_id, classroom_id, controller_admin_id, status, admissions_open,
                session_revision, class_default_json, started_at_utc_ms, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($session, $lesson, $classroom, $admin, 'Active', 1, 1, '{"view":"Waiting"}', $now, $now, $now),
                       ($otherSession, $lesson, $otherClassroom, $admin, 'Active', 1, 1, '{"view":"Waiting"}', $now, $now, $now);
            INSERT INTO station (
                id, classroom_id, label, browser_key_hash, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($station, $classroom, 'PC 1', $hash, $now, $now);
            """;
        AddParameter(command, "$school", SchoolId);
        AddParameter(command, "$classroom", ClassroomId);
        AddParameter(command, "$otherClassroom", OtherClassroomId);
        AddParameter(command, "$course", CourseId);
        AddParameter(command, "$lesson", LessonId);
        AddParameter(command, "$admin", AdminId);
        AddParameter(command, "$session", SessionId);
        AddParameter(command, "$otherSession", OtherSessionId);
        AddParameter(command, "$station", StationId);
        AddParameter(command, "$hash", BrowserKeyHash);
        AddParameter(command, "$now", NowMs);
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync()
    {
        _database.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InsertAsync_ConPartecipazioneNuova_SiRileggeUguale()
    {
        // Arrange
        await using var work = await _database.Units.CreateAsync(useTransaction: true);
        var participants = work.Repo<ISessionParticipantRepository>();
        var participant = SessionParticipant.Create(
            Guid.Parse(SessionId), "  Luca  ", Guid.Parse(StationId), Now);

        // Act
        var id = await participants.InsertAsync(SessionParticipantEntity.FromDomain(participant));

        // Assert
        var stored = await participants.GetAsync(id);
        Assert.Equal(participant.Id.ToString("D"), id);
        Assert.Equal(SessionId, stored!.SessionId);
        Assert.Equal(StationId, stored.StationId);
        Assert.Equal("Luca", stored.DisplayName);
        Assert.Equal("Admitted", stored.AdmissionStatusText);
        Assert.Equal(0, stored.ConnectionEpoch);
        Assert.Equal(NowMs, stored.JoinedAtUtcMs);
        Assert.Null(stored.ExcludedAtUtcMs);
        Assert.Equal(NowMs, stored.CreatedAtUtcMs);
        Assert.Equal(1, stored.RowVersion);

        // L'entità torna al dominio con gli stessi valori e senza perdere l'ammissione.
        var restored = stored.ToDomain();
        Assert.Equal(participant.Id, restored.Id);
        Assert.True(restored.IsAdmitted);
        Assert.Equal(participant.JoinedAtUtc, restored.JoinedAtUtc);
        await work.CommitAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InsertAsync_SenzaPostazione_SiRileggeSenzaPostazione()
    {
        // Arrange: la postazione è facoltativa (§7.2), quindi la riga si legge anche senza.
        await using var work = await _database.Units.CreateAsync(useTransaction: true);
        var participants = work.Repo<ISessionParticipantRepository>();
        var participant = SessionParticipant.Create(Guid.Parse(SessionId), "Anna", null, Now);

        // Act
        await participants.InsertAsync(SessionParticipantEntity.FromDomain(participant));

        // Assert
        var stored = await participants.GetAsync(participant.Id.ToString("D"));
        Assert.Null(stored!.StationId);
        Assert.Null(stored.ToDomain().StationId);
        await work.CommitAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InsertAsync_ConPartecipazioneEsclusa_ConservaStatoEIstante()
    {
        // Arrange: l'esclusione del dominio diventa lo stato persistito e il suo istante (§7.4).
        await using var work = await _database.Units.CreateAsync(useTransaction: true);
        var participants = work.Repo<ISessionParticipantRepository>();
        var participant = SessionParticipant.Create(Guid.Parse(SessionId), "Luca", null, Now);
        var excludedAt = Now.AddMinutes(3);
        participant.Exclude(excludedAt);

        // Act
        await participants.InsertAsync(SessionParticipantEntity.FromDomain(participant));

        // Assert
        var stored = await participants.GetAsync(participant.Id.ToString("D"));
        Assert.Equal("Excluded", stored!.AdmissionStatusText);
        Assert.Equal(excludedAt.ToUnixTimeMilliseconds(), stored.ExcludedAtUtcMs);
        Assert.False(stored.ToDomain().IsAdmitted);
        await work.CommitAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateAsync_ConVersioneObsoleta_RestituisceFalsoSenzaSovrascrivere()
    {
        // Arrange
        await using var work = await _database.Units.CreateAsync(useTransaction: true);
        var participants = work.Repo<ISessionParticipantRepository>();
        var participant = SessionParticipant.Create(Guid.Parse(SessionId), "Luca", null, Now);
        await participants.InsertAsync(SessionParticipantEntity.FromDomain(participant));
        var current = await participants.GetAsync(participant.Id.ToString("D"));
        var stale = await participants.GetAsync(participant.Id.ToString("D"));

        // Act: la correzione del nome del docente passa dalla copia letta per ultima.
        current!.DisplayName = "Luca B.";
        current.UpdatedAtUtcMs = NowMs + 1000;
        Assert.True(await participants.UpdateAsync(current));
        stale!.DisplayName = "Nome vecchio";

        // Assert
        Assert.Equal(2, current.RowVersion);
        Assert.False(await participants.UpdateAsync(stale));
        var stored = await participants.GetAsync(participant.Id.ToString("D"));
        Assert.Equal("Luca B.", stored!.DisplayName);
        Assert.Equal(NowMs + 1000, stored.UpdatedAtUtcMs);
        await work.CommitAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InsertAsync_ConPostazioneInesistente_ViolaLaChiaveEsternaSenzaDiventareUnConflitto()
    {
        // Arrange
        await using var work = await _database.Units.CreateAsync(useTransaction: true);
        var participants = work.Repo<ISessionParticipantRepository>();
        var participant = SessionParticipant.Create(
            Guid.Parse(SessionId), "Luca", Guid.Parse(MissingStationId), Now);

        // Act
        var error = await Assert.ThrowsAsync<SqliteException>(
            () => participants.InsertAsync(SessionParticipantEntity.FromDomain(participant)));

        // Assert: il vincolo è violato, non è mancata una tabella e non è un conflitto di versione.
        Assert.Equal(ForeignKeyViolationCode, error.SqliteExtendedErrorCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task QueryAsync_PerSessione_RestituisceSoloISuoiPartecipanti()
    {
        // Arrange: due partecipanti nella prima sessione e uno nella seconda, con lo stesso nome.
        await using var work = await _database.Units.CreateAsync(useTransaction: true);
        var participants = work.Repo<ISessionParticipantRepository>();
        await participants.InsertAsync(SessionParticipantEntity.FromDomain(
            SessionParticipant.Create(Guid.Parse(SessionId), "Luca", null, Now)));
        await participants.InsertAsync(SessionParticipantEntity.FromDomain(
            SessionParticipant.Create(Guid.Parse(SessionId), "Luca", null, Now)));
        await participants.InsertAsync(SessionParticipantEntity.FromDomain(
            SessionParticipant.Create(Guid.Parse(OtherSessionId), "Anna", null, Now)));

        // Act
        var ofSession = await participants.QueryAsync(row => row.SessionId == SessionId);

        // Assert: omonimi ammessi nella stessa sessione, nessuna riga di un'altra sessione.
        Assert.Equal(2, ofSession.Count);
        Assert.All(ofSession, row => Assert.Equal("Luca", row.DisplayName));
        await work.CommitAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Station_Update_AvanzaLaRowVersionEPreservaLaClasse()
    {
        // Arrange
        await using var work = await _database.Units.CreateAsync(useTransaction: true);
        var stations = work.Repo<IStationRepository>();
        var station = await stations.GetAsync(StationId);

        // Act: il contatto si aggiorna a ogni ingresso da quel browser.
        station!.LastSeenAtUtcMs = NowMs + 60_000;
        station.UpdatedAtUtcMs = NowMs + 60_000;
        Assert.True(await stations.UpdateAsync(station));

        // Assert
        var stored = await stations.GetAsync(StationId);
        Assert.Equal(2, stored!.RowVersion);
        Assert.Equal(NowMs + 60_000, stored.LastSeenAtUtcMs);
        Assert.Equal(ClassroomId, stored.ClassroomId);
        Assert.Equal("PC 1", stored.Label);
        await work.CommitAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InsertAsync_ConImprontaDuplicata_ViolaUnicitaDellaPostazione()
    {
        // Arrange: la stessa chiave del browser non entra due volte nel registro (§7.2).
        await using var work = await _database.Units.CreateAsync(useTransaction: true);
        var stations = work.Repo<IStationRepository>();
        var duplicate = new StationEntity
        {
            Id = MissingStationId,
            BrowserKeyHash = BrowserKeyHash,
            CreatedAtUtcMs = NowMs,
            UpdatedAtUtcMs = NowMs,
            RowVersion = 1,
        };

        // Act
        var error = await Assert.ThrowsAsync<SqliteException>(() => stations.InsertAsync(duplicate));

        // Assert
        Assert.Equal(UniqueViolationCode, error.SqliteExtendedErrorCode);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
