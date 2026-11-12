using Qwerty.Codi.Application.Dto;
using System.Globalization;
using System.Text.Json;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Lessons;
using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.IntegrationTests.Realtime;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Sessions;

/// <summary>
/// Contenuto della sessione e regia del lavoro (§6.4, §6.5): assegnazione dell'esercizio, sua
/// abilitazione e comandi di esecuzione — avvio, riavvio, azzeramento. La scena è quella reale dei
/// casi d'uso: scuola, classe, corso, lezione, sessione avviata e alunni entrati dal percorso di
/// ingresso. L'esercizio pubblicato è l'unica parte preparata direttamente, perché la pubblicazione
/// ha già i suoi test e qui serve soltanto come materiale da assegnare.
/// </summary>
public sealed class AssignmentUseCaseTests
{
    private const string InitialWorkspaceJson = """{"blocks":[]}""";

    [Theory]
    [InlineData("Enabled")]
    [InlineData("Paused")]
    [InlineData("Waiting")]
    public async Task IngressoTardivo_CopiaAttivitaEPermessiSenzaDirettiveStoriche(string mode)
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var versionId = Guid.NewGuid();
        await SeedExerciseAsync(scenario, scenario.AdminId, lessonId, Guid.NewGuid(), [versionId]);
        await AssignAndEnableAsync(scenario, seed, versionId);
        await scenario.Host.CommandAsAsync<CommandAcceptedDto>("RunProgram",
            new { sessionId = seed.SessionId, expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId) }, scenario.RegiaContext());
        if (mode == "Paused") await scenario.Host.CommandAsAsync<SessionDto>("Pause",
            new { sessionId = seed.SessionId, expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId) }, scenario.RegiaContext());
        if (mode == "Waiting") await scenario.ShowWaitingAsync(seed, await CurrentRevisionAsync(scenario, seed.SessionId));
        var joined = await scenario.JoinAsync(seed, "Sara", "late");
        var state = await scenario.RowAsync($"SELECT * FROM participant_state WHERE participant_id = '{joined.Joined.ParticipantId:D}';");
        Assert.NotNull(state["attempt_id"]); Assert.Null(state["directive_json"]);
        Assert.Equal(mode == "Waiting" ? "Waiting" : "Exercise", state["view"]);
        Assert.Equal(mode == "Enabled" ? 1L : 0L, state["interaction_enabled"]);
        Assert.Equal(mode == "Paused" ? 1L : 0L, state["teacher_paused"]);
        Assert.Equal(2L, await CountAsync(scenario, "SELECT COUNT(*) FROM exercise_attempt;"));
        if (mode != "Enabled")
        {
            await scenario.Host.CommandAsAsync<SessionDto>("Resume", new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
                participantId = mode == "Waiting" ? joined.Joined.ParticipantId : (Guid?)null
            }, scenario.RegiaContext());
            state = await scenario.RowAsync($"SELECT * FROM participant_state WHERE participant_id = '{joined.Joined.ParticipantId:D}';");
            Assert.Equal("Exercise", state["view"]); Assert.Equal(1L, state["interaction_enabled"]);
            Assert.Null(state["directive_json"]);
        }
    }

    [Fact]
    public async Task AssegnazioneDiClasse_CreaUnTentativoPerOgniAmmesso()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        await scenario.JoinAsync(seed, "Sara", "chiave-di-prova-2");

        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var exerciseId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        await SeedExerciseAsync(scenario, scenario.AdminId, lessonId, exerciseId, [versionId]);

        var revision = await CurrentRevisionAsync(scenario, seed.SessionId);
        var accepted = await scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            AssignExerciseCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = revision, exerciseVersionId = versionId },
            scenario.RegiaContext());

        Assert.Equal(seed.SessionId, accepted.SessionId);
        Assert.Equal(revision + 1, accepted.SessionRevision);

        // Un tentativo per destinatario, il primo per ognuno, già riferito alla versione assegnata.
        Assert.Equal(2L, await CountAsync(scenario, $"""
            SELECT COUNT(*) FROM exercise_attempt WHERE session_id = '{seed.SessionId:D}';
            """));
        Assert.Equal(1L, await ScalarAsync<long>(scenario, $"""
            SELECT MIN(attempt_number) FROM exercise_attempt WHERE session_id = '{seed.SessionId:D}';
            """));
        Assert.Equal(2L, await CountAsync(scenario, $"""
            SELECT COUNT(*) FROM exercise_attempt
            WHERE session_id = '{seed.SessionId:D}' AND exercise_version_id = '{versionId:D}';
            """));

        // Nasce pronto e bloccato: vista dell'esercizio, controlli chiusi, nessuna direttiva.
        var state = await scenario.RowAsync($"""
            SELECT view, interaction_enabled, teacher_paused, execution_generation,
                   assignment_id, attempt_id, directive_json
            FROM participant_state
            WHERE session_id = '{seed.SessionId:D}';
            """);
        Assert.Equal("Exercise", (string?)state["view"]);
        Assert.Equal(0L, (long?)state["interaction_enabled"]);
        Assert.Equal(0L, (long?)state["teacher_paused"]);
        Assert.Equal(0L, (long?)state["execution_generation"]);
        Assert.Null(state["directive_json"]);
        Assert.NotNull((string?)state["assignment_id"]);
        Assert.NotNull((string?)state["attempt_id"]);

        // Tracce di regia della stessa transazione: comando di classe, consegne per ogni ammesso,
        // un messaggio in coda.
        var command = await scenario.RowAsync($"""
            SELECT command_name, scope, target_participant_id
            FROM session_command WHERE session_id = '{seed.SessionId:D}';
            """);
        Assert.Equal(AssignExerciseCommand.RequestName, (string?)command["command_name"]);
        Assert.Equal("Class", (string?)command["scope"]);
        Assert.Null(command["target_participant_id"]);
        Assert.Equal(2L, await CountAsync(scenario, $"""
            SELECT COUNT(*) FROM command_delivery
            WHERE command_id = (SELECT id FROM session_command WHERE session_id = '{seed.SessionId:D}');
            """));
        Assert.Equal(1L, await CountAsync(scenario, $"""
            SELECT COUNT(*) FROM outbox_message
            WHERE session_id = '{seed.SessionId:D}' AND payload_json LIKE '%{accepted.CommandId:D}%';
            """));
    }

    [Fact]
    public async Task AssegnazioneDiClasse_NonPortaIlTentativoNellInvolucro()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        await scenario.JoinAsync(seed, "Sara", "chiave-di-prova-2");

        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var exerciseId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        await SeedExerciseAsync(scenario, scenario.AdminId, lessonId, exerciseId, [versionId]);

        var accepted = await scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            AssignExerciseCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
                exerciseVersionId = versionId,
            },
            scenario.RegiaContext());

        // Il tentativo è del singolo alunno: l'involucro uguale per tutti nomina l'assegnazione, non
        // un tentativo che gli altri non hanno (§11.4).
        var assignmentId = await GuidAsync(scenario, $"""
            SELECT id FROM activity_assignment WHERE session_id = '{seed.SessionId:D}';
            """);
        var payload = await ScalarAsync<string>(scenario, $"""
            SELECT payload_json FROM outbox_message WHERE session_id = '{seed.SessionId:D}' LIMIT 1;
            """);

        Assert.Contains(accepted.CommandId.ToString("D"), payload, StringComparison.Ordinal);
        Assert.Contains(assignmentId.ToString("D"), payload, StringComparison.Ordinal);
        var message = JsonSerializer.Deserialize<CommandIssuedMessageDto>(payload, CodiJson.Payload)!;
        Assert.Null(message.DesiredState.AttemptId);
        Assert.Equal(2, message.DesiredStates!.Count);
        Assert.Equal((await AttemptIdsAsync(scenario, seed.SessionId)).Order(),
            message.DesiredStates.Values.Select(state => state.AttemptId!.Value.ToString("D")).Order());
    }

    [Fact]
    public async Task AssegnazioneAlSingolo_PortaIlTentativoNellInvolucro()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        await scenario.JoinAsync(seed, "Sara", "chiave-di-prova-2");

        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var exerciseId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        await SeedExerciseAsync(scenario, scenario.AdminId, lessonId, exerciseId, [versionId]);
        var participantId = await GuidAsync(scenario, $"""
            SELECT id FROM session_participant
            WHERE session_id = '{seed.SessionId:D}' ORDER BY id LIMIT 1;
            """);

        await scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            AssignExerciseCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
                exerciseVersionId = versionId,
                participantId,
            },
            scenario.RegiaContext());

        var attemptIds = await AttemptIdsAsync(scenario, seed.SessionId);
        var attemptId = Assert.Single(attemptIds);
        var payload = await ScalarAsync<string>(scenario, $"""
            SELECT payload_json FROM outbox_message WHERE session_id = '{seed.SessionId:D}' LIMIT 1;
            """);

        Assert.Contains(attemptId, payload, StringComparison.Ordinal);

        // Il destinatario è dichiarato due volte: nel comando registrato e nell'assegnazione.
        var command = await scenario.RowAsync($"""
            SELECT scope, target_participant_id FROM session_command WHERE session_id = '{seed.SessionId:D}';
            """);
        Assert.Equal("Participant", (string?)command["scope"]);
        Assert.Equal(participantId.ToString("D"), (string?)command["target_participant_id"]);
        Assert.Equal("Participant", await ScalarAsync<string>(scenario, $"""
            SELECT scope FROM activity_assignment WHERE session_id = '{seed.SessionId:D}';
            """));
    }

    [Fact]
    public async Task AssegnazioneDaCasella_SceglieLUltimaVersionePubblicata()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");

        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var exerciseId = Guid.NewGuid();
        var firstVersionId = Guid.NewGuid();
        var secondVersionId = Guid.NewGuid();
        var lessonExerciseId = await SeedExerciseAsync(
            scenario, scenario.AdminId, lessonId, exerciseId, [firstVersionId, secondVersionId]);

        await scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            AssignExerciseCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
                lessonExerciseId,
            },
            scenario.RegiaContext());

        // La casella riferisce l'esercizio: la versione assegnata è la più recente pubblicata.
        Assert.Equal(secondVersionId.ToString("D"), await ScalarAsync<string>(scenario, $"""
            SELECT exercise_version_id FROM activity_assignment WHERE session_id = '{seed.SessionId:D}';
            """));
        Assert.Equal(lessonExerciseId.ToString("D"), await ScalarAsync<string>(scenario, $"""
            SELECT lesson_exercise_id FROM activity_assignment WHERE session_id = '{seed.SessionId:D}';
            """));
    }

    [Fact]
    public async Task Assegnazione_SenzaUnSoloMateriale_Rifiuta()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        var revision = await CurrentRevisionAsync(scenario, seed.SessionId);

        var withoutMaterial = await scenario.Host.ExpectFailureAsAsync(
            AssignExerciseCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = revision },
            scenario.RegiaContext());
        var withBoth = await scenario.Host.ExpectFailureAsAsync(
            AssignExerciseCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = revision,
                lessonExerciseId = Guid.NewGuid(),
                exerciseVersionId = Guid.NewGuid(),
            },
            scenario.RegiaContext());

        Assert.Equal(CodiErrorCodes.ValidationFailed, withoutMaterial.Code);
        Assert.Equal(CodiErrorCodes.ValidationFailed, withBoth.Code);
        Assert.Equal(0L, await CountAsync(scenario, "SELECT COUNT(*) FROM activity_assignment;"));
    }

    [Fact]
    public async Task AssegnazioneSenzaRevisione_Rifiuta()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");

        var failure = await scenario.Host.ExpectFailureAsAsync(
            AssignExerciseCommand.RequestName,
            new { sessionId = seed.SessionId, exerciseVersionId = Guid.NewGuid() },
            scenario.RegiaContext());

        Assert.Equal(CodiErrorCodes.EntityVersionRequired, failure.Code);
    }

    [Fact]
    public async Task AssegnazioneConRevisioneVecchia_Rifiuta()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");

        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var exerciseId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        await SeedExerciseAsync(scenario, scenario.AdminId, lessonId, exerciseId, [versionId]);
        var staleRevision = await CurrentRevisionAsync(scenario, seed.SessionId) - 1;

        var failure = await scenario.Host.ExpectFailureAsAsync(
            AssignExerciseCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = staleRevision, exerciseVersionId = versionId },
            scenario.RegiaContext());

        Assert.Equal(CodiErrorCodes.SessionRevisionConflict, failure.Code);
        Assert.Equal(0L, await CountAsync(scenario, "SELECT COUNT(*) FROM activity_assignment;"));
        Assert.Equal(0L, await CountAsync(scenario, "SELECT COUNT(*) FROM session_command;"));
    }

    [Fact]
    public async Task AssegnazioneSuSessioneTerminata_Rifiuta()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        await scenario.Host.CommandAsAsync<SessionDto>(
            EndSessionCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
            },
            scenario.RegiaContext());

        var failure = await scenario.Host.ExpectFailureAsAsync(
            AssignExerciseCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
                exerciseVersionId = Guid.NewGuid(),
            },
            scenario.RegiaContext());

        Assert.Equal(CodiErrorCodes.SessionStateConflict, failure.Code);
    }

    [Fact]
    public async Task AssegnazioneDiUnAltroAmministratore_Rifiuta()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        var otherController = new CodiRequestContext(
            isAuthenticated: true,
            [CodiPolicy.Admin, CodiPolicy.SessionController],
            adminUserId: Guid.NewGuid());

        var failure = await scenario.Host.ExpectFailureAsAsync(
            AssignExerciseCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
                exerciseVersionId = Guid.NewGuid(),
            },
            otherController);

        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    [Fact]
    public async Task AssegnazioneConMaterialeNonRisolvibile_Rifiuta()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");

        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var courseId = await GuidAsync(scenario, $"SELECT course_id FROM lesson WHERE id = '{lessonId:D}';");
        var otherLesson = await scenario.Host.CommandAsAsync<LessonDto>(
            CreateLessonCommand.RequestName,
            new { courseId, title = "Seconda lezione" },
            scenario.RegiaContext());

        var exerciseId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var lessonExerciseId = await SeedExerciseAsync(scenario, scenario.AdminId, lessonId, exerciseId, [versionId]);
        var boxOfAnotherLesson = await SeedExerciseBoxAsync(scenario, otherLesson.Id, Guid.NewGuid(), position: 1);
        var boxWithoutVersion = await SeedExerciseBoxAsync(scenario, lessonId, Guid.NewGuid(), position: 2);

        var revision = await CurrentRevisionAsync(scenario, seed.SessionId);
        var missingVersion = await scenario.Host.ExpectFailureAsAsync(
            AssignExerciseCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = revision, exerciseVersionId = Guid.NewGuid() },
            scenario.RegiaContext());
        var otherLessonBox = await scenario.Host.ExpectFailureAsAsync(
            AssignExerciseCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = revision, lessonExerciseId = boxOfAnotherLesson },
            scenario.RegiaContext());
        var unpublishedBox = await scenario.Host.ExpectFailureAsAsync(
            AssignExerciseCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = revision, lessonExerciseId = boxWithoutVersion },
            scenario.RegiaContext());
        var unknownParticipant = await scenario.Host.ExpectFailureAsAsync(
            AssignExerciseCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = revision,
                exerciseVersionId = versionId,
                participantId = Guid.NewGuid(),
            },
            scenario.RegiaContext());

        Assert.Equal(CodiErrorCodes.ResourceNotFound, missingVersion.Code);
        Assert.Equal(CodiErrorCodes.ResourceNotFound, otherLessonBox.Code);
        Assert.Equal(CodiErrorCodes.ValidationFailed, unpublishedBox.Code);
        Assert.Equal(CodiErrorCodes.ResourceNotFound, unknownParticipant.Code);
        Assert.NotEqual(Guid.Empty, lessonExerciseId);
    }

    [Fact]
    public async Task AbilitazioneDiClasse_AccendeIControlliSenzaToccareIlLavoro()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        await scenario.JoinAsync(seed, "Sara", "chiave-di-prova-2");

        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var versionId = Guid.NewGuid();
        await SeedExerciseAsync(scenario, scenario.AdminId, lessonId, Guid.NewGuid(), [versionId]);
        await scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            AssignExerciseCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
                exerciseVersionId = versionId,
            },
            scenario.RegiaContext());
        var attemptIds = await AttemptIdsAsync(scenario, seed.SessionId);

        var before = await CurrentRevisionAsync(scenario, seed.SessionId);
        var accepted = await scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            EnableActivityCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = before },
            scenario.RegiaContext());

        Assert.Equal(before + 1, accepted.SessionRevision);
        Assert.Equal(2L, await CountAsync(scenario, $"""
            SELECT COUNT(*) FROM participant_state
            WHERE session_id = '{seed.SessionId:D}' AND interaction_enabled = 1;
            """));

        // Abilitare non ricrea il lavoro: i tentativi sono quelli dell'assegnazione, non nuovi.
        Assert.Equal(attemptIds, await AttemptIdsAsync(scenario, seed.SessionId));

        var command = await scenario.RowAsync($"""
            SELECT command_name, scope, revision FROM session_command
            WHERE id = '{accepted.CommandId:D}';
            """);
        Assert.Equal(EnableActivityCommand.RequestName, (string?)command["command_name"]);
        Assert.Equal("Class", (string?)command["scope"]);
        Assert.Equal(before + 1, (long?)command["revision"]);
    }

    [Fact]
    public async Task AbilitazioneAlSingolo_LasciaGliAltriBloccati()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        await scenario.JoinAsync(seed, "Sara", "chiave-di-prova-2");

        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var versionId = Guid.NewGuid();
        await SeedExerciseAsync(scenario, scenario.AdminId, lessonId, Guid.NewGuid(), [versionId]);
        await scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            AssignExerciseCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
                exerciseVersionId = versionId,
            },
            scenario.RegiaContext());
        var participantId = await GuidAsync(scenario, $"""
            SELECT id FROM session_participant
            WHERE session_id = '{seed.SessionId:D}' ORDER BY id LIMIT 1;
            """);

        var accepted = await scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            EnableActivityCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
                participantId,
            },
            scenario.RegiaContext());

        Assert.Equal(1L, await CountAsync(scenario, $"""
            SELECT COUNT(*) FROM participant_state
            WHERE session_id = '{seed.SessionId:D}' AND interaction_enabled = 1;
            """));
        Assert.Equal(1L, await CountAsync(scenario, $"""
            SELECT COUNT(*) FROM command_delivery WHERE command_id = '{accepted.CommandId:D}';
            """));

        var command = await scenario.RowAsync($"""
            SELECT scope, target_participant_id FROM session_command WHERE id = '{accepted.CommandId:D}';
            """);
        Assert.Equal("Participant", (string?)command["scope"]);
        Assert.Equal(participantId.ToString("D"), (string?)command["target_participant_id"]);
    }

    [Fact]
    public async Task AbilitazioneSenzaEsercizioCaricato_RifiutaSenzaApplicazioniParziali()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        var revision = await CurrentRevisionAsync(scenario, seed.SessionId);

        var failure = await scenario.Host.ExpectFailureAsAsync(
            EnableActivityCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = revision },
            scenario.RegiaContext());

        Assert.Equal(CodiErrorCodes.SessionStateConflict, failure.Code);
        Assert.Contains("nessun esercizio caricato", failure.Message, StringComparison.Ordinal);
        Assert.Equal(revision, await CurrentRevisionAsync(scenario, seed.SessionId));
        Assert.Equal(0L, await CountAsync(scenario, "SELECT COUNT(*) FROM session_command;"));
        Assert.Equal(0L, await CountAsync(scenario, "SELECT COUNT(*) FROM participant_state WHERE interaction_enabled = 1;"));
    }

    [Fact]
    public async Task AbilitazioneConVistaDiAttesa_Rifiuta()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");

        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var versionId = Guid.NewGuid();
        await SeedExerciseAsync(scenario, scenario.AdminId, lessonId, Guid.NewGuid(), [versionId]);
        var assigned = await scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            AssignExerciseCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
                exerciseVersionId = versionId,
            },
            scenario.RegiaContext());
        await scenario.ShowWaitingAsync(seed, assigned.SessionRevision);

        var failure = await scenario.Host.ExpectFailureAsAsync(
            EnableActivityCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
            },
            scenario.RegiaContext());

        Assert.Equal(CodiErrorCodes.SessionStateConflict, failure.Code);
        Assert.Contains("vista Waiting", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0L, await CountAsync(scenario, "SELECT COUNT(*) FROM participant_state WHERE interaction_enabled = 1;"));
    }

    [Fact]
    public async Task AbilitazioneSenzaRevisioneOConRevisioneSuperata_Rifiuta()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");

        var withoutRevision = await scenario.Host.ExpectFailureAsAsync(
            EnableActivityCommand.RequestName,
            new { sessionId = seed.SessionId },
            scenario.RegiaContext());
        var stale = await scenario.Host.ExpectFailureAsAsync(
            EnableActivityCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = 1L },
            scenario.RegiaContext());

        Assert.Equal(CodiErrorCodes.EntityVersionRequired, withoutRevision.Code);
        Assert.Equal(CodiErrorCodes.SessionRevisionConflict, stale.Code);
    }

    [Fact]
    public async Task AbilitazioneSuDestinatarioNonAmmesso_DiUnAltroAmministratoreOSuSessioneTerminata_Rifiuta()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        var revision = await CurrentRevisionAsync(scenario, seed.SessionId);

        var unknownParticipant = await scenario.Host.ExpectFailureAsAsync(
            EnableActivityCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = revision, participantId = Guid.NewGuid() },
            scenario.RegiaContext());
        var otherController = new CodiRequestContext(
            isAuthenticated: true,
            [CodiPolicy.Admin, CodiPolicy.SessionController],
            adminUserId: Guid.NewGuid());
        var otherAdmin = await scenario.Host.ExpectFailureAsAsync(
            EnableActivityCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = revision },
            otherController);

        Assert.Equal(CodiErrorCodes.ResourceNotFound, unknownParticipant.Code);
        Assert.Equal(CodiErrorCodes.ResourceNotFound, otherAdmin.Code);

        // Una sessione terminata non abilita attività: è un conflitto di stato, non un'assenza.
        await scenario.Host.CommandAsAsync<SessionDto>(
            EndSessionCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = revision },
            scenario.RegiaContext());
        var ended = await scenario.Host.ExpectFailureAsAsync(
            EnableActivityCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
            },
            scenario.RegiaContext());

        Assert.Equal(CodiErrorCodes.SessionStateConflict, ended.Code);
        Assert.Equal(0L, await CountAsync(scenario, "SELECT COUNT(*) FROM participant_state WHERE interaction_enabled = 1;"));
    }

    [Fact]
    public async Task AvvioAlSingolo_ScriveUnaDirettivaConLaGenerazioneDelDestinatario()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var versionId = Guid.NewGuid();
        await SeedExerciseAsync(scenario, scenario.AdminId, lessonId, Guid.NewGuid(), [versionId]);
        await AssignAndEnableAsync(scenario, seed, versionId);
        var participantId = await GuidAsync(scenario, $"""
            SELECT id FROM session_participant WHERE session_id = '{seed.SessionId:D}' ORDER BY id LIMIT 1;
            """);

        var before = await CurrentRevisionAsync(scenario, seed.SessionId);
        var accepted = await scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            RunProgramCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = before, participantId },
            scenario.RegiaContext());

        Assert.Equal(before + 1, accepted.SessionRevision);

        // La direttiva dichiara la generazione del destinatario: è ciò che distingue un riavvio
        // imposto dal server da una corsa normale (§5.3).
        var directive = ReadDirective((string?)(await StateAsync(scenario, participantId))["directive_json"]);
        Assert.Equal("Run", directive.Kind);
        Assert.Equal(0L, directive.Generation);

        // L'involucro porta la direttiva: è il modo in cui il client la riceve (§11.4).
        var payload = await ScalarAsync<string>(scenario, $"""
            SELECT payload_json FROM outbox_message
            WHERE session_id = '{seed.SessionId:D}' AND payload_json LIKE '%{accepted.CommandId:D}%' LIMIT 1;
            """);
        Assert.Contains(directive.Id.ToString("D"), payload, StringComparison.Ordinal);
        Assert.Contains("\"attemptId\"", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AvvioDiClasse_UsaUnaSolaDirettivaSenzaGenerazione()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        await scenario.JoinAsync(seed, "Sara", "chiave-di-prova-2");
        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var versionId = Guid.NewGuid();
        await SeedExerciseAsync(scenario, scenario.AdminId, lessonId, Guid.NewGuid(), [versionId]);
        await AssignAndEnableAsync(scenario, seed, versionId);

        var accepted = await scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            RunProgramCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
            },
            scenario.RegiaContext());

        // Una direttiva per tutti: identica, e senza generazione, perché dopo un riavvio individuale
        // le generazioni dei singoli possono divergere.
        Assert.Equal(1L, await ScalarAsync<long>(scenario, $"""
            SELECT COUNT(DISTINCT directive_json) FROM participant_state WHERE session_id = '{seed.SessionId:D}';
            """));
        var directive = ReadDirective(await ScalarAsync<string>(scenario, $"""
            SELECT directive_json FROM participant_state WHERE session_id = '{seed.SessionId:D}' LIMIT 1;
            """));
        Assert.Equal("Run", directive.Kind);
        Assert.Null(directive.Generation);

        var payload = await ScalarAsync<string>(scenario, $"""
            SELECT payload_json FROM outbox_message
            WHERE session_id = '{seed.SessionId:D}' AND payload_json LIKE '%{accepted.CommandId:D}%' LIMIT 1;
            """);
        Assert.Contains(directive.Id.ToString("D"), payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AvvioSenzaAttivitaAbilitata_Rifiuta()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var versionId = Guid.NewGuid();
        await SeedExerciseAsync(scenario, scenario.AdminId, lessonId, Guid.NewGuid(), [versionId]);
        await scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            AssignExerciseCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
                exerciseVersionId = versionId,
            },
            scenario.RegiaContext());

        var revision = await CurrentRevisionAsync(scenario, seed.SessionId);
        var failure = await scenario.Host.ExpectFailureAsAsync(
            RunProgramCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = revision },
            scenario.RegiaContext());

        Assert.Equal(CodiErrorCodes.SessionStateConflict, failure.Code);
        Assert.Contains("attività non abilitata", failure.Message, StringComparison.Ordinal);
        Assert.Equal(revision, await CurrentRevisionAsync(scenario, seed.SessionId));
        Assert.Equal(0L, await CountAsync(scenario, $"""
            SELECT COUNT(*) FROM session_command WHERE command_name = '{RunProgramCommand.RequestName}';
            """));
    }

    [Fact]
    public async Task AvvioConPausaDocenteOPausaDiRecupero_Rifiuta()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var versionId = Guid.NewGuid();
        await SeedExerciseAsync(scenario, scenario.AdminId, lessonId, Guid.NewGuid(), [versionId]);
        await AssignAndEnableAsync(scenario, seed, versionId);

        await SetStateFlagAsync(scenario, seed.SessionId, "teacher_paused");
        var paused = await scenario.Host.ExpectFailureAsAsync(
            RunProgramCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
            },
            scenario.RegiaContext());

        await SetStateFlagAsync(scenario, seed.SessionId, "teacher_paused", value: 0);
        await SetStateFlagAsync(scenario, seed.SessionId, "recovery_blocked");
        var recovering = await scenario.Host.ExpectFailureAsAsync(
            RunProgramCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
            },
            scenario.RegiaContext());

        Assert.Equal(CodiErrorCodes.SessionStateConflict, paused.Code);
        Assert.Contains("in pausa docente", paused.Message, StringComparison.Ordinal);
        Assert.Contains("pausa di recupero", recovering.Message, StringComparison.Ordinal);
        Assert.Equal(0L, await CountAsync(scenario, "SELECT COUNT(*) FROM participant_state WHERE directive_json IS NOT NULL;"));
    }

    [Fact]
    public async Task AvvioSenzaRevisioneConRevisioneSuperataOSuSessioneTerminata_Rifiuta()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");

        var withoutRevision = await scenario.Host.ExpectFailureAsAsync(
            RunProgramCommand.RequestName,
            new { sessionId = seed.SessionId },
            scenario.RegiaContext());
        var stale = await scenario.Host.ExpectFailureAsAsync(
            RunProgramCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = 1L },
            scenario.RegiaContext());

        await scenario.Host.CommandAsAsync<SessionDto>(
            EndSessionCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
            },
            scenario.RegiaContext());
        var ended = await scenario.Host.ExpectFailureAsAsync(
            RunProgramCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
            },
            scenario.RegiaContext());

        Assert.Equal(CodiErrorCodes.EntityVersionRequired, withoutRevision.Code);
        Assert.Equal(CodiErrorCodes.SessionRevisionConflict, stale.Code);
        Assert.Equal(CodiErrorCodes.SessionStateConflict, ended.Code);
    }

    [Fact]
    public async Task Riavvio_IncrementaLaGenerazioneEConservaIlProgetto()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var versionId = Guid.NewGuid();
        await SeedExerciseAsync(scenario, scenario.AdminId, lessonId, Guid.NewGuid(), [versionId]);
        await AssignAndEnableAsync(scenario, seed, versionId);
        var participantId = await GuidAsync(scenario, $"""
            SELECT id FROM session_participant WHERE session_id = '{seed.SessionId:D}' ORDER BY id LIMIT 1;
            """);

        // Il progetto dell'alunno è cambiato rispetto al modello iniziale della versione.
        const string WorkedWorkspace = """{"blocks":[7]}""";
        await using (var connection = await scenario.Host.OpenConnectionAsync())
        {
            await ExecuteAsync(
                connection,
                transaction: null,
                "UPDATE exercise_attempt SET workspace_json = $workspace WHERE session_id = $session;",
                ("$workspace", WorkedWorkspace),
                ("$session", seed.SessionId.ToString("D")));
        }

        var before = await CurrentRevisionAsync(scenario, seed.SessionId);
        await scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            RestartExecutionCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = before, participantId },
            scenario.RegiaContext());

        Assert.Equal(1L, await ScalarAsync<long>(scenario, $"""
            SELECT execution_generation FROM participant_state WHERE participant_id = '{participantId:D}';
            """));
        Assert.Equal(WorkedWorkspace, await ScalarAsync<string>(scenario, $"""
            SELECT workspace_json FROM exercise_attempt WHERE session_id = '{seed.SessionId:D}';
            """));

        var directive = ReadDirective((string?)(await StateAsync(scenario, participantId))["directive_json"]);
        Assert.Equal("Restart", directive.Kind);
        Assert.Equal(1L, directive.Generation);
    }

    [Fact]
    public async Task RiavvioDiClasse_IncrementaLaGenerazioneDiOgnuno()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        await scenario.JoinAsync(seed, "Sara", "chiave-di-prova-2");
        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var versionId = Guid.NewGuid();
        await SeedExerciseAsync(scenario, scenario.AdminId, lessonId, Guid.NewGuid(), [versionId]);
        await AssignAndEnableAsync(scenario, seed, versionId);

        await scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            RestartExecutionCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
            },
            scenario.RegiaContext());

        Assert.Equal(2L, await ScalarAsync<long>(scenario, $"""
            SELECT COUNT(*) FROM participant_state
            WHERE session_id = '{seed.SessionId:D}' AND execution_generation = 1;
            """));
        Assert.Equal(1L, await ScalarAsync<long>(scenario, $"""
            SELECT COUNT(DISTINCT directive_json) FROM participant_state WHERE session_id = '{seed.SessionId:D}';
            """));
    }

    [Fact]
    public async Task Azzeramento_ArchiviaIlTentativoENeCreaUnoDalModelloIniziale()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var versionId = Guid.NewGuid();
        await SeedExerciseAsync(scenario, scenario.AdminId, lessonId, Guid.NewGuid(), [versionId]);
        await AssignAndEnableAsync(scenario, seed, versionId);
        var participantId = await GuidAsync(scenario, $"""
            SELECT id FROM session_participant WHERE session_id = '{seed.SessionId:D}' ORDER BY id LIMIT 1;
            """);
        var attemptId = await GuidAsync(scenario, $"""
            SELECT attempt_id FROM participant_state WHERE participant_id = '{participantId:D}';
            """);
        var rowVersion = await ScalarAsync<long>(scenario, $"""
            SELECT row_version FROM exercise_attempt WHERE id = '{attemptId:D}';
            """);

        var before = await CurrentRevisionAsync(scenario, seed.SessionId);
        var accepted = await scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            ResetWorkCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                participantId,
                attemptId,
                attemptRowVersion = rowVersion,
                expectedSessionRevision = before,
            },
            scenario.RegiaContext());

        Assert.Equal(before + 1, accepted.SessionRevision);
        Assert.Equal("Archived", await ScalarAsync<string>(scenario, $"""
            SELECT status FROM exercise_attempt WHERE id = '{attemptId:D}';
            """));

        // Il tentativo nuovo nasce dal modello iniziale della versione, pronto e bloccato.
        var fresh = await scenario.RowAsync($"""
            SELECT id, attempt_number, status, workspace_json FROM exercise_attempt
            WHERE session_id = '{seed.SessionId:D}' AND status = 'Ready';
            """);
        Assert.Equal(2L, (long?)fresh["attempt_number"]);
        Assert.Equal(InitialWorkspaceJson, (string?)fresh["workspace_json"]);

        var state = await StateAsync(scenario, participantId);
        Assert.Equal((string?)fresh["id"], (string?)state["attempt_id"]);
        Assert.Equal(0L, (long?)state["interaction_enabled"]);
        Assert.Equal(0L, (long?)state["execution_generation"]);
        Assert.Null(state["directive_json"]);

        var payload = await ScalarAsync<string>(scenario, $"""
            SELECT payload_json FROM outbox_message
            WHERE session_id = '{seed.SessionId:D}' AND payload_json LIKE '%{accepted.CommandId:D}%' LIMIT 1;
            """);
        Assert.Contains((string)fresh["id"]!, payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AzzeramentoConTentativoDiversoOConVersioneSuperata_Rifiuta()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var versionId = Guid.NewGuid();
        await SeedExerciseAsync(scenario, scenario.AdminId, lessonId, Guid.NewGuid(), [versionId]);
        await AssignAndEnableAsync(scenario, seed, versionId);
        var participantId = await GuidAsync(scenario, $"""
            SELECT id FROM session_participant WHERE session_id = '{seed.SessionId:D}' ORDER BY id LIMIT 1;
            """);
        var attemptId = await GuidAsync(scenario, $"""
            SELECT attempt_id FROM participant_state WHERE participant_id = '{participantId:D}';
            """);
        var revision = await CurrentRevisionAsync(scenario, seed.SessionId);

        var otherAttempt = await scenario.Host.ExpectFailureAsAsync(
            ResetWorkCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                participantId,
                attemptId = Guid.NewGuid(),
                attemptRowVersion = 1L,
                expectedSessionRevision = revision,
            },
            scenario.RegiaContext());
        var supersededVersion = await scenario.Host.ExpectFailureAsAsync(
            ResetWorkCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                participantId,
                attemptId,
                attemptRowVersion = 99L,
                expectedSessionRevision = revision,
            },
            scenario.RegiaContext());

        Assert.Equal(CodiErrorCodes.SessionStateConflict, otherAttempt.Code);
        Assert.Contains("il lavoro corrente non è quello indicato", otherAttempt.Message, StringComparison.Ordinal);
        Assert.Equal(CodiErrorCodes.RowVersionMismatch, supersededVersion.Code);
        Assert.Equal(1L, await CountAsync(scenario, $"""
            SELECT COUNT(*) FROM exercise_attempt WHERE session_id = '{seed.SessionId:D}';
            """));
        Assert.Null((string?)(await StateAsync(scenario, participantId))["directive_json"]);
    }

    [Fact]
    public async Task AzzeramentoSenzaRevisioneODiUnAltroAmministratore_Rifiuta()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        var participantId = await GuidAsync(scenario, $"SELECT id FROM session_participant WHERE session_id = '{seed.SessionId:D}';");
        var attemptId = Guid.NewGuid();
        var otherController = new CodiRequestContext(
            isAuthenticated: true,
            [CodiPolicy.Admin, CodiPolicy.SessionController],
            adminUserId: Guid.NewGuid());

        var withoutRevision = await scenario.Host.ExpectFailureAsAsync(
            ResetWorkCommand.RequestName,
            new { sessionId = seed.SessionId, participantId, attemptId, attemptRowVersion = 1L },
            scenario.RegiaContext());
        var otherAdmin = await scenario.Host.ExpectFailureAsAsync(
            ResetWorkCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                participantId,
                attemptId,
                attemptRowVersion = 1L,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
            },
            otherController);

        Assert.Equal(CodiErrorCodes.EntityVersionRequired, withoutRevision.Code);
        Assert.Equal(CodiErrorCodes.ResourceNotFound, otherAdmin.Code);
    }

    [Fact]
    public async Task AzzeramentoSenzaDestinatarioConStato_Rifiuta()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var versionId = Guid.NewGuid();
        await SeedExerciseAsync(scenario, scenario.AdminId, lessonId, Guid.NewGuid(), [versionId]);
        await AssignAndEnableAsync(scenario, seed, versionId);
        var participantId = await GuidAsync(scenario, $"""
            SELECT id FROM session_participant WHERE session_id = '{seed.SessionId:D}';
            """);

        // Senza riga di stato il destinatario non esiste per la regia: è una risorsa assente, non un
        // azzeramento senza effetto.
        await using (var connection = await scenario.Host.OpenConnectionAsync())
        {
            await ExecuteAsync(
                connection,
                transaction: null,
                "DELETE FROM participant_state WHERE participant_id = $participant;",
                ("$participant", participantId.ToString("D")));
        }

        var failure = await scenario.Host.ExpectFailureAsAsync(
            ResetWorkCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                participantId,
                attemptId = Guid.NewGuid(),
                attemptRowVersion = 1L,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
            },
            scenario.RegiaContext());

        Assert.Equal(CodiErrorCodes.SessionStateConflict, failure.Code);
        Assert.Contains("non è un alunno ammesso", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AvvioSenzaPartecipanti_RegistraIlComandoSenzaConsegne()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();

        var accepted = await scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            RunProgramCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
            },
            scenario.RegiaContext());

        // Nessuno da avviare: il comando resta registrato e il messaggio in coda esiste, come per gli
        // altri comandi di stato desiderato (§12.1).
        Assert.Equal(0, accepted.DeliverySummary.Pending);
        Assert.Equal(0L, await CountAsync(scenario, $"""
            SELECT COUNT(*) FROM command_delivery WHERE command_id = '{accepted.CommandId:D}';
            """));
        Assert.Equal(1L, await CountAsync(scenario, $"""
            SELECT COUNT(*) FROM outbox_message
            WHERE session_id = '{seed.SessionId:D}' AND payload_json LIKE '%{accepted.CommandId:D}%';
            """));
    }

    [Fact]
    public async Task LetturaDelProprioLavoro_PortaDefinizioneProgettoEImpronta()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        var joined = await scenario.JoinAsync(seed, "Luca");
        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var versionId = Guid.NewGuid();
        await SeedExerciseAsync(scenario, scenario.AdminId, lessonId, Guid.NewGuid(), [versionId]);
        await AssignAndEnableAsync(scenario, seed, versionId);
        var context = CodiUseCaseHost.ParticipantContext(
            joined.Joined.CredentialSessionId, joined.Joined.ParticipantId, seed.SessionId);

        var exercise = await scenario.Host.QueryOneAsAsync<AssignedExerciseDto>(
            GetAssignedExerciseQuery.RequestName, new { }, context);
        var attempt = await scenario.Host.QueryOneAsAsync<AttemptDto>(
            GetAttemptQuery.RequestName, new { }, context);

        // La versione è quella fissata dall'assegnazione, non l'ultima pubblicata nel frattempo.
        Assert.Equal(versionId, exercise.ExerciseVersionId);
        Assert.Equal("Disegno con i blocchi", exercise.ExerciseTitle);
        Assert.Equal("{}", exercise.DefinitionJson);
        Assert.Equal("Class", exercise.Scope);
        Assert.Equal(1L, exercise.AttemptNumber);
        Assert.Equal("1.0.0", exercise.EngineVersion);

        // Il progetto è quello del tentativo corrente, con l'impronta su cui il client riconosce un
        // salvataggio ripetuto.
        Assert.Equal(exercise.AttemptId, attempt.AttemptId);
        Assert.Equal(exercise.AssignmentId, attempt.AssignmentId);
        Assert.Equal(InitialWorkspaceJson, attempt.WorkspaceJson);
        Assert.False(string.IsNullOrWhiteSpace(attempt.WorkspaceHash));
        Assert.Equal("Ready", attempt.Status);
        Assert.Equal(0L, attempt.ProjectRevision);
        Assert.Null(attempt.CheckpointJson);
    }

    [Fact]
    public async Task LetturaDelProprioLavoro_SenzaAssegnazioneOSenzaCredenziale_Rifiuta()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        var joined = await scenario.JoinAsync(seed, "Luca");
        var context = CodiUseCaseHost.ParticipantContext(
            joined.Joined.CredentialSessionId, joined.Joined.ParticipantId, seed.SessionId);

        var withoutAssignment = await scenario.Host.ExpectQueryFailureAsAsync(
            GetAssignedExerciseQuery.RequestName, new { }, context);
        var attemptWithoutAssignment = await scenario.Host.ExpectQueryFailureAsAsync(
            GetAttemptQuery.RequestName, new { }, context);
        var anonymous = await scenario.Host.ExpectQueryFailureAsAsync(
            GetAssignedExerciseQuery.RequestName, new { }, CodiRequestContext.Anonymous);

        Assert.Equal(CodiErrorCodes.ResourceNotFound, withoutAssignment.Code);
        Assert.Equal(CodiErrorCodes.ResourceNotFound, attemptWithoutAssignment.Code);
        Assert.Equal(CodiErrorCodes.AuthenticationRequired, anonymous.Code);
    }

    [Fact]
    public async Task Griglia_PortaPresenzaStatoDesideratoETentativo()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        await scenario.JoinAsync(seed, "Sara", "chiave-di-prova-2");
        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var versionId = Guid.NewGuid();
        await SeedExerciseAsync(scenario, scenario.AdminId, lessonId, Guid.NewGuid(), [versionId]);
        await AssignAndEnableAsync(scenario, seed, versionId);

        var grid = Assert.IsAssignableFrom<IReadOnlyList<SessionParticipantOverviewDto>>(
            await scenario.Host.ExecuteAsAsync(
                CodiRequestKind.Query,
                ListSessionParticipantsQuery.RequestName,
                new { sessionId = seed.SessionId },
                scenario.RegiaContext()));

        Assert.Equal(new[] { "Luca", "Sara" }, grid.Select(row => row.DisplayName));
        Assert.All(grid, row => Assert.Equal("Admitted", row.AdmissionStatus));
        Assert.All(grid, row => Assert.True(row.RowVersion > 0));
        Assert.All(grid, row => Assert.Equal(ParticipantPresence.Disconnected, row.Presence));
        Assert.All(grid, row => Assert.Equal("Exercise", row.View));
        Assert.All(grid, row => Assert.True(row.InteractionEnabled));
        Assert.All(grid, row => Assert.False(row.TeacherPaused));
        Assert.All(grid, row => Assert.Equal("Ready", row.AttemptStatus));
        Assert.All(grid, row => Assert.Equal(1L, row.AttemptNumber));
        Assert.All(grid, row => Assert.Equal(1L, row.AttemptRowVersion));
        Assert.All(grid, row => Assert.False(row.HasDirective));
    }

    [Fact]
    public async Task Dettaglio_PortaMaterialeEStoricoDeiTentativi()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var versionId = Guid.NewGuid();
        await SeedExerciseAsync(scenario, scenario.AdminId, lessonId, Guid.NewGuid(), [versionId]);
        await AssignAndEnableAsync(scenario, seed, versionId);
        var participantId = await GuidAsync(scenario, $"""
            SELECT id FROM session_participant WHERE session_id = '{seed.SessionId:D}';
            """);
        var attemptId = await GuidAsync(scenario, $"""
            SELECT attempt_id FROM participant_state WHERE participant_id = '{participantId:D}';
            """);
        var rowVersion = await ScalarAsync<long>(scenario, $"""
            SELECT row_version FROM exercise_attempt WHERE id = '{attemptId:D}';
            """);
        await scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            ResetWorkCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                participantId,
                attemptId,
                attemptRowVersion = rowVersion,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
            },
            scenario.RegiaContext());

        var detail = await scenario.Host.QueryOneAsAsync<SessionParticipantDetailDto>(
            GetParticipantDetailQuery.RequestName,
            new { sessionId = seed.SessionId, participantId },
            scenario.RegiaContext());

        Assert.Equal("Luca", detail.Participant.DisplayName);
        Assert.Equal("Disegno con i blocchi", detail.Assignment!.ExerciseTitle);
        // La versione del motore fissata dall'assegnazione accompagna il dettaglio: è quella che dice
        // al docente come leggere il lavoro dell'alunno — blocchi oppure verbale di risposta.
        Assert.Equal("1.0.0", detail.Assignment.EngineVersion);
        Assert.Equal("Class", detail.Assignment.Scope);
        Assert.Equal(versionId, detail.Assignment.ExerciseVersionId);

        // Dopo l'azzeramento il lavoro corrente è il secondo tentativo, e lo storico lo dice.
        Assert.Equal(2, detail.AttemptCount);
        Assert.Equal(2L, detail.Attempt!.AttemptNumber);
        Assert.Equal("Ready", detail.Attempt.Status);
        Assert.Equal(InitialWorkspaceJson, detail.Attempt.WorkspaceJson);
        Assert.False(detail.Attempt.HasCheckpoint);
    }

    [Fact]
    public async Task GrigliaEDettaglio_DiUnAltroAmministratoreODiUnaRisorsaAssente_Rifiutano()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        var otherController = new CodiRequestContext(
            isAuthenticated: true,
            [CodiPolicy.Admin, CodiPolicy.SessionController],
            adminUserId: Guid.NewGuid());

        var otherAdmin = await scenario.Host.ExpectQueryFailureAsAsync(
            ListSessionParticipantsQuery.RequestName,
            new { sessionId = seed.SessionId },
            otherController);
        var unknownSession = await scenario.Host.ExpectQueryFailureAsAsync(
            GetParticipantDetailQuery.RequestName,
            new { sessionId = Guid.NewGuid(), participantId = Guid.NewGuid() },
            scenario.RegiaContext());
        var unknownParticipant = await scenario.Host.ExpectQueryFailureAsAsync(
            GetParticipantDetailQuery.RequestName,
            new { sessionId = seed.SessionId, participantId = Guid.NewGuid() },
            scenario.RegiaContext());

        Assert.Equal(CodiErrorCodes.ResourceNotFound, otherAdmin.Code);
        Assert.Equal(CodiErrorCodes.ResourceNotFound, unknownSession.Code);
        Assert.Equal(CodiErrorCodes.ResourceNotFound, unknownParticipant.Code);
    }

    [Fact]
    public async Task AvvioDiClasse_LaDirettivaArrivaAlDestinatarioNelloSnapshot()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        var joined = await scenario.JoinAsync(seed, "Luca");
        await scenario.JoinAsync(seed, "Sara", "chiave-di-prova-2");
        var lessonId = await GuidAsync(scenario, $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var versionId = Guid.NewGuid();
        await SeedExerciseAsync(scenario, scenario.AdminId, lessonId, Guid.NewGuid(), [versionId]);
        await AssignAndEnableAsync(scenario, seed, versionId);
        await scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            RunProgramCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
            },
            scenario.RegiaContext());

        // L'involucro del comando di classe porta una sola `desiredState`: la direttiva del singolo
        // arriva dal suo snapshot, che è la lettura per destinatario.
        var context = CodiUseCaseHost.ParticipantContext(
            joined.Joined.CredentialSessionId, joined.Joined.ParticipantId, seed.SessionId);
        var snapshot = await scenario.Host.QueryOneAsAsync<ParticipantSnapshotDto>(
            GetParticipantSnapshotQuery.RequestName, new { }, context);

        var directive = snapshot.DesiredState.ExecutionDirective;
        Assert.NotNull(directive);
        Assert.Equal(ExecutionDirectiveDto.RunKind, directive!.Kind);
        Assert.Null(directive.ExecutionGeneration);
        Assert.NotNull(snapshot.DesiredState.AttemptId);
        Assert.Equal("Exercise", snapshot.DesiredState.View);
        Assert.True(snapshot.DesiredState.InteractionEnabled);
    }

    /// <summary>Assegna l'esercizio e abilita l'attività: la premessa di ogni comando di esecuzione.</summary>
    private static async Task AssignAndEnableAsync(OutboxScenario scenario, Seed seed, Guid versionId)
    {
        await scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            AssignExerciseCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
                exerciseVersionId = versionId,
            },
            scenario.RegiaContext());

        await scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            EnableActivityCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await CurrentRevisionAsync(scenario, seed.SessionId),
            },
            scenario.RegiaContext());
    }

    /// <summary>Stato desiderato di un alunno, letto per colonne che i comandi di esecuzione toccano.</summary>
    private static Task<IReadOnlyDictionary<string, object?>> StateAsync(OutboxScenario scenario, Guid participantId) =>
        scenario.RowAsync($"""
            SELECT attempt_id, interaction_enabled, teacher_paused, recovery_blocked,
                   execution_generation, directive_json
            FROM participant_state WHERE participant_id = '{participantId:D}';
            """);

    /// <summary>Alza un flag di pausa direttamente: lo stato lo scrivono i comandi che non esistono ancora.</summary>
    private static async Task SetStateFlagAsync(
        OutboxScenario scenario,
        Guid sessionId,
        string column,
        long value = 1)
    {
        await using var connection = await scenario.Host.OpenConnectionAsync();

        await ExecuteAsync(
            connection,
            transaction: null,
            $"UPDATE participant_state SET {column} = $value WHERE session_id = $session;",
            ("$value", value.ToString(CultureInfo.InvariantCulture)),
            ("$session", sessionId.ToString("D")));
    }

    /// <summary>Legge la direttiva persistita come il client la riceve: identificativo, tipo, generazione.</summary>
    private static (Guid Id, string Kind, long? Generation) ReadDirective(string? directiveJson)
    {
        Assert.NotNull(directiveJson);

        using var document = JsonDocument.Parse(directiveJson!);
        var root = document.RootElement;
        var generation = root.GetProperty("executionGeneration");

        return (
            root.GetProperty("directiveId").GetGuid(),
            root.GetProperty("kind").GetString()!,
            generation.ValueKind switch
            {
                // I contatori a 64 bit viaggiano come stringhe decimali (§10.2).
                JsonValueKind.Null => null,
                JsonValueKind.String => long.Parse(generation.GetString()!, CultureInfo.InvariantCulture),
                _ => generation.GetInt64(),
            });
    }

    /// <summary>
    /// Esercizio pubblicato e casella di sequenza nella lezione: l'identità dell'esercizio e la
    /// versione, inserite direttamente perché la pubblicazione ha i propri test.
    /// </summary>
    private static async Task<Guid> SeedExerciseAsync(
        OutboxScenario scenario,
        Guid adminId,
        Guid lessonId,
        Guid exerciseId,
        IReadOnlyList<Guid> versionIds)
    {
        await using var connection = await scenario.Host.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO exercise (id, title, description, is_archived, created_at_utc_ms, updated_at_utc_ms, row_version)
            VALUES ($id, 'Disegno con i blocchi', '', 0, 1000, 1000, 1);
            """,
            ("$id", exerciseId.ToString("D")));

        for (var index = 0; index < versionIds.Count; index++)
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO exercise_version (
                    id, exercise_id, version_number, definition_json, initial_workspace_json,
                    engine_version, schema_version, content_hash, published_by_admin_id, created_at_utc_ms)
                VALUES ($id, $exerciseId, $number, '{}', $workspace, '1.0.0', '1.0.0', 'impronta-di-prova', $adminId, 1000);
                """,
                ("$id", versionIds[index].ToString("D")),
                ("$exerciseId", exerciseId.ToString("D")),
                ("$number", (index + 1).ToString(CultureInfo.InvariantCulture)),
                ("$workspace", InitialWorkspaceJson),
                ("$adminId", adminId.ToString("D")));
        }

        var lessonExerciseId = Guid.NewGuid();
        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO lesson_exercise (id, lesson_id, exercise_id, position, created_at_utc_ms, updated_at_utc_ms, row_version)
            VALUES ($id, $lessonId, $exerciseId, 1, 1000, 1000, 1);
            """,
            ("$id", lessonExerciseId.ToString("D")),
            ("$lessonId", lessonId.ToString("D")),
            ("$exerciseId", exerciseId.ToString("D")));

        await transaction.CommitAsync();

        return lessonExerciseId;
    }

    /// <summary>Casella di sequenza senza versioni pubblicate: serve ai rifiuti dell'assegnazione.</summary>
    private static async Task<Guid> SeedExerciseBoxAsync(
        OutboxScenario scenario,
        Guid lessonId,
        Guid exerciseId,
        long position)
    {
        await using var connection = await scenario.Host.OpenConnectionAsync();

        await ExecuteAsync(
            connection,
            transaction: null,
            """
            INSERT INTO exercise (id, title, description, is_archived, created_at_utc_ms, updated_at_utc_ms, row_version)
            VALUES ($id, 'Esercizio senza versioni', '', 0, 1000, 1000, 1);
            """,
            ("$id", exerciseId.ToString("D")));

        var lessonExerciseId = Guid.NewGuid();
        await ExecuteAsync(
            connection,
            transaction: null,
            """
            INSERT INTO lesson_exercise (id, lesson_id, exercise_id, position, created_at_utc_ms, updated_at_utc_ms, row_version)
            VALUES ($id, $lessonId, $exerciseId, $position, 1000, 1000, 1);
            """,
            ("$id", lessonExerciseId.ToString("D")),
            ("$lessonId", lessonId.ToString("D")),
            ("$exerciseId", exerciseId.ToString("D")),
            ("$position", position.ToString(CultureInfo.InvariantCulture)));

        return lessonExerciseId;
    }

    private static async Task ExecuteAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction? transaction,
        string sql,
        params (string Name, string Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;

        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static Task<long> CurrentRevisionAsync(OutboxScenario scenario, Guid sessionId) =>
        ScalarAsync<long>(scenario, $"SELECT session_revision FROM lesson_session WHERE id = '{sessionId:D}';");

    private static Task<long> CountAsync(OutboxScenario scenario, string sql) => ScalarAsync<long>(scenario, sql);

    private static Task<T> ScalarAsync<T>(OutboxScenario scenario, string sql) => scenario.ScalarAsync<T>(sql);

    /// <summary>Identificativo letto come testo canonico e convertito: lo scalare non converte da sé.</summary>
    private static async Task<Guid> GuidAsync(OutboxScenario scenario, string sql) =>
        Guid.Parse(await scenario.ScalarAsync<string>(sql), CultureInfo.InvariantCulture);

    private static async Task<IReadOnlyList<string>> AttemptIdsAsync(OutboxScenario scenario, Guid sessionId)
    {
        await using var connection = await scenario.Host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT id FROM exercise_attempt WHERE session_id = '{sessionId:D}' ORDER BY id;";

        var ids = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }
}
