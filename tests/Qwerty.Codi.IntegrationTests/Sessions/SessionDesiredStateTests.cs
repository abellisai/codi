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
/// Stato desiderato comandato (§6.2, §6.4, §6.5): il primo comando che cambia ciò che gli alunni
/// devono mostrare. <c>ShowWaiting</c> riporta il default di classe alla vista di attesa e aggiorna
/// nella stessa transazione lo stato materializzato di ogni partecipante, così nessuno resta con la
/// vista precedente.
///
/// La distribuzione ai client ha i suoi test in <c>OutboxDispatcherTests</c>, quindi la verifica guarda il
/// server: la riga della sessione, le righe di <c>participant_state</c> e ciò che il partecipante
/// legge dal proprio contesto con <c>GetParticipantContext</c>.
/// </summary>
public sealed class SessionDesiredStateTests : IAsyncLifetime
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
    public async Task ShowWaiting_RiportaIlDefaultEGliStatiDeiPartecipantiAllAttesa()
    {
        // Arrange: due alunni entrati mentre la classe lavorava su un esercizio.
        var seed = await StartedSessionAsync();
        var luca = await JoinAsync(seed, "Luca");
        var anna = await JoinAsync(seed, "Anna", idempotencyKey: "chiave-ingresso-anna");
        await SetWorkingStateAsync(luca.Joined.ParticipantId);
        await SetWorkingStateAsync(anna.Joined.ParticipantId);

        // Act
        var accepted = await _host.CommandAsAsync<CommandAcceptedDto>(
            ShowWaitingCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = 2L },
            RegiaContext());

        // Assert: il comando è registrato con la revisione risultante e una consegna per alunno.
        Assert.NotEqual(Guid.Empty, accepted.CommandId);
        Assert.Equal(seed.SessionId, accepted.SessionId);
        Assert.Equal(3, accepted.SessionRevision);
        Assert.Equal(new DeliverySummaryDto(Pending: 2, Applied: 0, Failed: 0), accepted.DeliverySummary);
        Assert.Equal(GetCommandStatusQuery.RequestName, accepted.StatusQuery.Name);
        Assert.Equal(seed.SessionId, accepted.StatusQuery.Payload.SessionId);
        Assert.Equal(accepted.CommandId, accepted.StatusQuery.Payload.CommandId);

        // Il comando è ciò che è stato accettato: nome pubblico, ambito di classe e autore (§12.1).
        var command = await ColumnsAsync(
            "SELECT command_name, scope, revision, issued_by_admin_id, target_participant_id FROM session_command WHERE id = $id;",
            accepted.CommandId);
        Assert.Equal(ShowWaitingCommand.RequestName, command[0]);
        Assert.Equal(CommandScopes.Class, command[1]);
        Assert.Equal(3L, command[2]);
        Assert.Equal(_adminId.ToString("D"), command[3]);
        Assert.IsType<DBNull>(command[4]);

        // Le consegne nascono in attesa e il messaggio outbox è in coda, non ancora processato.
        Assert.Equal(
            2L,
            await ScalarAsync<long>($"SELECT COUNT(*) FROM command_delivery WHERE command_id = '{accepted.CommandId:D}' AND status = 'Pending';"));
        Assert.Equal(
            1L,
            await ScalarAsync<long>($"SELECT COUNT(*) FROM outbox_message WHERE session_id = '{seed.SessionId:D}' AND message_type = 'CommandIssued' AND processed_at_utc_ms IS NULL;"));
        Assert.Contains(
            accepted.CommandId.ToString("D"),
            await ScalarAsync<string>($"SELECT payload_json FROM outbox_message WHERE session_id = '{seed.SessionId:D}';"));

        // Il default di classe torna in attesa: i nuovi ingressi non trovano l'esercizio.
        Assert.Equal(
            LessonSession.WaitingClassJson,
            await ScalarAsync<string>($"SELECT class_default_json FROM lesson_session WHERE id = '{seed.SessionId:D}';"));

        // Ogni partecipante è sospeso e riporta la revisione a cui lo stato è stato deciso.
        foreach (var participantId in new[] { luca.Joined.ParticipantId, anna.Joined.ParticipantId })
        {
            var state = await ColumnsAsync(
                "SELECT view, interaction_enabled, effective_revision, row_version FROM participant_state WHERE participant_id = $id;",
                participantId);

            Assert.Equal(nameof(ParticipantView.Waiting), state[0]);
            Assert.Equal(0L, state[1]);
            Assert.Equal(3L, state[2]);
            Assert.Equal(2L, state[3]);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ShowWaiting_DopoIlComando_IlContestoDelPartecipanteMostraLAttesa()
    {
        // Arrange: è l'effetto osservabile della porzione — l'alunno legge la vista dal proprio
        // contesto, che rilegge lo stato a ogni richiesta.
        var seed = await StartedSessionAsync();
        var luca = await JoinAsync(seed, "Luca");
        await SetWorkingStateAsync(luca.Joined.ParticipantId);

        // Act
        await _host.CommandAsAsync<CommandAcceptedDto>(
            ShowWaitingCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = 2L },
            RegiaContext());

        var context = await _host.QueryOneAsAsync<ParticipantContextDto>(
            GetParticipantContextQuery.RequestName,
            new { },
            CodiUseCaseHost.ParticipantContext(luca.Joined.CredentialSessionId, luca.Joined.ParticipantId));

        // Assert
        Assert.Equal(nameof(ParticipantView.Waiting), context.View);
        Assert.Equal(nameof(SessionStatus.Active), context.SessionStatus);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ContestoDelPartecipante_PortaIlPercorsoScuolaClasseLezione()
    {
        // Arrange: l'alunno arriva da un codice di otto cifre e deve riconoscere dove si trova (0.40).
        var seed = await StartedSessionAsync(className: "Quarta B", schoolName: "Scuola Verdi");
        var luca = await JoinAsync(seed, "Luca");

        // Act
        var context = await _host.QueryOneAsAsync<ParticipantContextDto>(
            GetParticipantContextQuery.RequestName,
            new { },
            CodiUseCaseHost.ParticipantContext(luca.Joined.CredentialSessionId, luca.Joined.ParticipantId));

        // Assert: i tre nomi del percorso, distinti da quelli di un'altra sessione.
        Assert.Equal("Scuola Verdi", context.SchoolName);
        Assert.Equal("Quarta B", context.ClassroomName);
        Assert.Equal("Prima lezione", context.LessonTitle);
        Assert.Equal("Luca", context.DisplayName);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ContestoDelPartecipante_SenzaLezioneInTabella_MostraIlPercorsoParziale()
    {
        // Arrange: il percorso è contesto, non permesso. Una lezione rimossa non deve impedire
        // all'alunno di leggere chi è e che cosa può fare.
        var seed = await StartedSessionAsync();
        var luca = await JoinAsync(seed, "Luca");
        // La lezione si toglie di forza: lo scopo è proprio lo stato incoerente che l'applicazione
        // non può produrre da sé, quindi la chiave esterna si sospende per questa riga di prova.
        await ExecuteSqlAsync(
            $"PRAGMA foreign_keys = OFF; DELETE FROM lesson WHERE id = (SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}');");

        // Act
        var context = await _host.QueryOneAsAsync<ParticipantContextDto>(
            GetParticipantContextQuery.RequestName,
            new { },
            CodiUseCaseHost.ParticipantContext(luca.Joined.CredentialSessionId, luca.Joined.ParticipantId));

        // Assert
        Assert.Equal(string.Empty, context.LessonTitle);
        Assert.Equal("Scuola Manzoni", context.SchoolName);
        Assert.Equal(nameof(ParticipantView.Waiting), context.View);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ShowWaiting_SuSessioneTerminata_RifiutaConConflittoDiStato()
    {
        // Arrange: la conclusione è terminale, quindi non torna in attesa (§6.1).
        var seed = await StartedSessionAsync();
        var ended = await _host.CommandAsAsync<SessionDto>(
            EndSessionCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = 2L },
            RegiaContext());

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            ShowWaitingCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = ended.SessionRevision },
            RegiaContext());

        // Assert
        Assert.Equal(CodiErrorCodes.SessionStateConflict, failure.Code);
        Assert.Equal(
            ended.SessionRevision,
            (await _host.QueryOneAsAsync<SessionSummaryDto>(
                GetSessionQuery.RequestName,
                new { sessionId = seed.SessionId },
                RegiaContext())).SessionRevision);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ShowWaiting_SenzaPartecipanti_CambiaSoloIlDefaultDiClasse()
    {
        // Arrange: nessuno è ancora entrato, quindi non c'è stato materializzato da aggiornare.
        var seed = await StartedSessionAsync();

        // Act
        var accepted = await _host.CommandAsAsync<CommandAcceptedDto>(
            ShowWaitingCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = 2L },
            RegiaContext());

        // Assert: senza destinatari non ci sono consegne, ma il comando e il messaggio esistono
        // comunque: il client non c'è, non l'effetto.
        Assert.Equal(3, accepted.SessionRevision);
        Assert.Equal(new DeliverySummaryDto(Pending: 0, Applied: 0, Failed: 0), accepted.DeliverySummary);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT COUNT(*) FROM participant_state;"));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT COUNT(*) FROM command_delivery;"));
        Assert.Equal(1L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_command;"));
        Assert.Equal(1L, await ScalarAsync<long>("SELECT COUNT(*) FROM outbox_message;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StatoDelComando_DopoShowWaiting_RestituisceLeConsegneInAttesa()
    {
        // Arrange
        var seed = await StartedSessionAsync();
        var luca = await JoinAsync(seed, "Luca");
        var accepted = await _host.CommandAsAsync<CommandAcceptedDto>(
            ShowWaitingCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = 2L },
            RegiaContext());

        // Act
        var status = await _host.QueryOneAsAsync<CommandStatusDto>(
            GetCommandStatusQuery.RequestName,
            new { sessionId = seed.SessionId, commandId = accepted.CommandId },
            RegiaContext());

        // Assert
        Assert.Equal(accepted.CommandId, status.CommandId);
        Assert.Equal(ShowWaitingCommand.RequestName, status.CommandName);
        Assert.Equal(3, status.Revision);
        Assert.Equal(new DeliverySummaryDto(Pending: 1, Applied: 0, Failed: 0), status.Summary);

        var delivery = Assert.Single(status.Deliveries);
        Assert.Equal(luca.Joined.ParticipantId, delivery.ParticipantId);
        Assert.Equal(CommandDeliveryStates.Pending, delivery.Status);
        Assert.Equal(3, delivery.EffectiveRevision);
        Assert.False(delivery.RequiresRecovery);
        Assert.Equal(0, delivery.SendAttempts);
        Assert.Null(delivery.SentAtUtc);
        Assert.Null(delivery.AcknowledgedAtUtc);
        Assert.Null(delivery.AppliedAtUtc);
        Assert.Null(delivery.ErrorCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StatoDelComando_DiUnAltroAmministratore_RifiutaConRisorsaAssente()
    {
        // Arrange
        var seed = await StartedSessionAsync();
        await JoinAsync(seed, "Luca");
        var accepted = await _host.CommandAsAsync<CommandAcceptedDto>(
            ShowWaitingCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = 2L },
            RegiaContext());

        // Act: la sessione di un altro amministratore è indistinguibile da una inesistente.
        var failure = await _host.ExpectQueryFailureAsAsync(
            GetCommandStatusQuery.RequestName,
            new { sessionId = seed.SessionId, commandId = accepted.CommandId },
            new CodiRequestContext(
                isAuthenticated: true,
                [CodiPolicy.Admin, CodiPolicy.SessionController],
                adminUserId: Guid.NewGuid()));

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StatoDelComando_ConComandoInesistente_RifiutaConRisorsaAssente()
    {
        // Arrange
        var seed = await StartedSessionAsync();

        // Act
        var failure = await _host.ExpectQueryFailureAsAsync(
            GetCommandStatusQuery.RequestName,
            new { sessionId = seed.SessionId, commandId = Guid.NewGuid() },
            RegiaContext());

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StatoDelComando_ConComandoDiUnAltraSessione_RifiutaConRisorsaAssente()
    {
        // Arrange: due sessioni dello stesso amministratore: lo stato di un comando si legge solo
        // dalla sessione a cui il comando appartiene.
        var seed = await StartedSessionAsync();
        var other = await StartedSessionAsync(className: "Quarta B", schoolName: "Scuola Verdi");
        await JoinAsync(seed, "Luca");
        var accepted = await _host.CommandAsAsync<CommandAcceptedDto>(
            ShowWaitingCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = 2L },
            RegiaContext());

        // Act
        var failure = await _host.ExpectQueryFailureAsAsync(
            GetCommandStatusQuery.RequestName,
            new { sessionId = other.SessionId, commandId = accepted.CommandId },
            RegiaContext());

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    /// <summary>Contesto di regia: la credenziale amministrativa concede anche SessionController (D3).</summary>
    private CodiRequestContext RegiaContext() =>
        new(isAuthenticated: true, [CodiPolicy.Admin, CodiPolicy.SessionController], adminUserId: _adminId);

    /// <summary>Catena reale con la sessione in play: la revisione dopo l'avvio è due.</summary>
    private async Task<Seed> StartedSessionAsync(string className = "Terza A", string schoolName = "Scuola Manzoni")
    {
        var school = await _host.CommandAsAsync<SchoolDto>(
            CreateSchoolCommand.RequestName, new { name = schoolName }, RegiaContext());
        var classroom = await _host.CommandAsAsync<ClassroomDto>(
            CreateClassroomCommand.RequestName,
            new { schoolId = school.Id, name = className, schoolYear = "2026/2027" },
            RegiaContext());
        var course = await _host.CommandAsAsync<CourseDto>(
            CreateCourseCommand.RequestName, new { title = "Informatica di base" }, RegiaContext());
        var lesson = await _host.CommandAsAsync<LessonDto>(
            CreateLessonCommand.RequestName, new { courseId = course.Id, title = "Prima lezione" }, RegiaContext());
        var session = await _host.CommandAsAsync<SessionDto>(
            CreateSessionCommand.RequestName,
            new { lessonId = lesson.Id, classroomId = classroom.Id },
            RegiaContext());
        await _host.CommandAsAsync<SessionDto>(
            StartSessionCommand.RequestName,
            new { sessionId = session.Id, expectedSessionRevision = 1L },
            RegiaContext());

        return new Seed(school.Id, classroom.Id, session.Id);
    }

    private Task<JoinSessionOutcome> JoinAsync(Seed seed, string displayName, string idempotencyKey = "chiave-di-prova") =>
        _host.CommandAsAsync<JoinSessionOutcome>(
            JoinSessionCommand.RequestName,
            new
            {
                schoolId = seed.SchoolId,
                classroomId = seed.ClassroomId,
                sessionId = seed.SessionId,
                displayName,
            },
            CodiUseCaseHost.JoinContext("scope-di-prova", idempotencyKey));

    /// <summary>
    /// Porta lo stato di un partecipante all'esercizio: serve a distinguere l'effetto del comando da
    /// uno stato che era già di attesa, che non proverebbe nulla.
    /// </summary>
    private Task SetWorkingStateAsync(Guid participantId) =>
        ExecuteSqlAsync(
            $"UPDATE participant_state SET view = 'Exercise', interaction_enabled = 1 WHERE participant_id = '{participantId:D}';");

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

    /// <summary>Catena scelta dal client, creata dal percorso reale.</summary>
    private sealed record Seed(Guid SchoolId, Guid ClassroomId, Guid SessionId);
}
