using Qwerty.Codi.Application.Dto;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Exercises;
using Qwerty.Codi.Application.Sessions;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Realtime;

/// <summary>
/// Sblocco non distruttivo della pausa di recupero (§12.3): il caso QA del 2026-11-12.
///
/// L'alunno riceve RunProgram, poi ricarica la pagina. Il server lo mette in pausa di recupero e
/// l'interazione si chiude. La ripresa per alunno (Resume con participantId) è l'azione che
/// sblocca senza cancellare il lavoro: conserva tentativo e workspace, azzera il blocco e, quando
/// il contesto precedente esisteva, riapre i controlli. La "Azzera il lavoro" resta la sola via
/// distruttiva. Ciò che qui si verifica è che le due vie non si confondano.
/// </summary>
public sealed class RecoveryUnblockTests : IAsyncLifetime
{
    private OutboxScenario scenario = null!;
    private Seed seed = null!;
    private JoinSessionOutcome joined = null!;
    private ParticipantSnapshotDto snapshot = null!;
    private AttemptDto attempt = null!;
    private Guid versionId;
    private long lastAcceptedRevision;
    private CodiRequestContext Context => new(true,
        [CodiPolicy.AdmittedParticipant, CodiPolicy.ActiveParticipantConnection, CodiPolicy.FinalizingParticipant],
        joined.Joined.CredentialSessionId, participantId: joined.Joined.ParticipantId);
    private const string Workspace = """{"blocks":{"languageVersion":0,"blocks":[{"type":"aula_start","id":"start"},{"type":"aula_move","id":"move-1"}]}}""";

    public async Task InitializeAsync()
    {
        scenario = await OutboxScenario.CreateAsync();
        seed = await scenario.StartedSessionAsync();
        joined = await scenario.JoinAsync(seed, "Luca");
        var exercise = await scenario.Host.CommandAsAsync<ExerciseDto>("CreateExercise", new { exerciseType = "GridPath", title = "Stella" }, scenario.RegiaContext());
        var draft = await scenario.Host.CommandAsAsync<ExerciseDraftDto>("SaveExerciseDraft",
            new { exerciseId = exercise.Id, definitionJson = Definition, workspaceJson = "{}" }, scenario.RegiaContext());
        var version = await scenario.Host.CommandAsAsync<ExerciseVersionDto>("PublishExerciseVersion",
            new { exerciseId = exercise.Id, baseRowVersion = draft.RowVersion }, scenario.RegiaContext());
        versionId = version.Id;
        await Control("AssignExercise", version: versionId);
        await SynchronizeAsync();
        attempt = await scenario.Host.QueryOneAsAsync<AttemptDto>("GetAttempt", new { }, Context);
    }

    public async Task DisposeAsync() => await scenario.DisposeAsync();

    private async Task<long> RevisionAsync() => await scenario.ScalarAsync<long>("SELECT session_revision FROM lesson_session;");

    /// <summary>Comando di regia, di classe o per alunno: la pagina lo invia con la revisione letta.</summary>
    private async Task<object?> Control(string name, Guid? participantId = null, Guid? version = null) =>
        await scenario.Host.ExecuteAsAsync(CodiRequestKind.Command, name,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await RevisionAsync(),
                exerciseVersionId = version,
                participantId,
            }, scenario.RegiaContext());

    /// <summary>Handshake del client, nuova pagina: epoca nuova, snapshot applicato.</summary>
    private async Task SynchronizeAsync()
    {
        snapshot = await scenario.Host.CommandAsAsync<ParticipantSnapshotDto>("InitializeParticipantConnection",
            new { protocolVersion = 1, clientInstanceId = Guid.NewGuid(), appVersion = "test", engineVersion = "regia-demo-1" }, Context);
        var registry = scenario.Host.Services.GetRequiredService<IParticipantConnectionRegistry>();
        registry.Acquire(new(joined.Joined.ParticipantId, seed.SessionId, Guid.NewGuid(), "client", snapshot.ConnectionEpoch, snapshot.EffectiveRevision), CodiUseCaseHost.Now);
        registry.MarkSnapshotApplied(new(joined.Joined.ParticipantId, "client", snapshot.ConnectionEpoch, snapshot.EffectiveRevision), CodiUseCaseHost.Now);
    }

    private async Task<CommandAcceptedDto> AcceptedAsync(string name, Guid? participantId = null)
    {
        var accepted = Assert.IsAssignableFrom<CommandAcceptedDto>(await Control(name, participantId));
        lastAcceptedRevision = accepted.SessionRevision;
        return accepted;
    }

    /// <summary>Spedisce le consegne in coda: la consegna esiste solo dopo il dispatcher.</summary>
    private async Task DispatchAsync()
    {
        var dispatcher = new OutboxDispatcher(
            scenario.Host.Services.GetRequiredService<IUnitOfWorkContext>(),
            new RecordingCommandDeliveryNotifier(),
            scenario.Host.Services.GetRequiredService<TimeProvider>());
        await dispatcher.DispatchPendingAsync(CancellationToken.None);
    }

    /// <summary>Il client conferma che cosa ha applicato (§12.2).</summary>
    private async Task<CommandAcknowledgementDto> AcknowledgeAsync(Guid commandId, long effectiveRevision, string outcome) =>
        await scenario.Host.CommandAsAsync<CommandAcknowledgementDto>("AcknowledgeCommand",
            new AcknowledgeCommandCommand(commandId, ParticipantProtocol.CurrentVersion, effectiveRevision,
                snapshot.ConnectionEpoch, outcome, RuntimeStatus: "Paused"), Context);

    /// <summary>Reload della pagina alunno: epoca nuova e il client dichiara di non essere certo (§12.3).</summary>
    private async Task<ParticipantSnapshotDto> ReloadWithRecoveryReportAsync()
    {
        await SynchronizeAsync();
        return await scenario.Host.CommandAsAsync<ParticipantSnapshotDto>("ReportRecoveryBlocked",
            new ReportRecoveryBlockedCommand(snapshot.ConnectionEpoch, attempt.AttemptId, lastAcceptedRevision), Context);
    }

    private async Task<IReadOnlyDictionary<string, object?>> StateRowAsync() =>
        await scenario.RowAsync("""
            SELECT recovery_blocked, interaction_enabled, teacher_paused, attempt_id, directive_json, resume_context_json
            FROM participant_state;
            """);

    private Task<string> WorkspaceRowAsync() =>
        scenario.ScalarAsync<string>($"SELECT workspace_json FROM exercise_attempt WHERE id = '{attempt.AttemptId:D}';");

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ReloadDopoEsecuzione_BloccoVisibile_RipresaIndividualeConservaIlLavoroERiapreIControlli()
    {
        // Riproduzione QA: RunProgram applicato, l'alunno salva, poi ricarica. Il blocco di recupero
        // deve essere visibile alla regia e lo sblocco per alunno non deve toccare il tentativo.
        await AcceptedAsync("EnableActivity", joined.Joined.ParticipantId);
        var run = await AcceptedAsync("RunProgram", joined.Joined.ParticipantId);
        await DispatchAsync();
        await AcknowledgeAsync(run.CommandId, run.SessionRevision, CommandAcknowledgementOutcomes.Applied);

        var saved = await scenario.Host.CommandAsAsync<AttemptSaveDto>("SaveAttemptWorkspace",
            new SaveAttemptWorkspaceCommand(attempt.AttemptId, Guid.NewGuid(), snapshot.ConnectionEpoch,
                attempt.RowVersion, 1, Workspace, ExerciseContentHash.ComputeWorkspace(Workspace)), Context);
        Assert.Equal(2, saved.RowVersion);

        var recovered = await ReloadWithRecoveryReportAsync();
        Assert.True(recovered.DesiredState.RecoveryBlocked);
        Assert.Equal(1L, (await StateRowAsync())["recovery_blocked"]);

        // La card della regia legge il blocco dall'overview (§14.1): è lì che l'operatore lo vede.
        var overview = Assert.IsAssignableFrom<IReadOnlyList<SessionParticipantOverviewDto>>(
            await scenario.Host.ExecuteAsAsync(CodiRequestKind.Query, "ListSessionParticipants",
                new { sessionId = seed.SessionId }, scenario.RegiaContext()));
        Assert.True(Assert.Single(overview).RecoveryBlocked);

        // Sblocco non distruttivo: la stessa azione che la pagina offre come "sblocca il recupero".
        await Control("Resume", joined.Joined.ParticipantId);

        var state = await StateRowAsync();
        Assert.Equal(0L, state["recovery_blocked"]);
        Assert.Equal(1L, state["interaction_enabled"]);
        Assert.Equal(0L, state["teacher_paused"]);
        Assert.Equal(attempt.AttemptId.ToString("D"), state["attempt_id"]);
        Assert.Null(state["directive_json"]);
        Assert.Null(state["resume_context_json"]);

        // Il lavoro conservato: workspace e revisione del progetto sono quelli salvati prima del reload.
        Assert.Equal(Workspace, await WorkspaceRowAsync());
        Assert.Equal(1L, await scenario.ScalarAsync<long>($"SELECT project_revision FROM exercise_attempt WHERE id = '{attempt.AttemptId:D}';"));

        // L'alunno può continuare a lavorare: un nuovo salvataggio sulla stessa epoca è ammesso.
        var fresh = await scenario.Host.QueryOneAsAsync<AttemptDto>("GetAttempt", new { }, Context);
        var next = await scenario.Host.CommandAsAsync<AttemptSaveDto>("SaveAttemptWorkspace",
            new SaveAttemptWorkspaceCommand(attempt.AttemptId, Guid.NewGuid(), snapshot.ConnectionEpoch,
                fresh.RowVersion, 2, Workspace, ExerciseContentHash.ComputeWorkspace(Workspace)), Context);
        Assert.Equal(3, next.RowVersion);
        Assert.Equal(2L, next.ProjectRevision);

        // Anche la regia può rieseguire: il blocco non è più un conflitto.
        await AcceptedAsync("RunProgram", joined.Joined.ParticipantId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BloccoConContestoPrecedenteAssente_RipresaConservaIlLavoroPoiServonoIControlli()
    {
        // Variante QA: il blocco nasce dalla conferma "applicato con recupero" e il contesto
        // precedente non c'è. La ripresa resta non distruttiva; i controlli tornano con la sola
        // abilitazione, senza azzerare nulla.
        await AcceptedAsync("EnableActivity", joined.Joined.ParticipantId);
        var run = await AcceptedAsync("RunProgram", joined.Joined.ParticipantId);
        await DispatchAsync();
        var ack = await AcknowledgeAsync(run.CommandId, run.SessionRevision, CommandAcknowledgementOutcomes.AppliedWithRecoveryRequired);
        Assert.True(ack.RequiresRecovery);

        var saved = await scenario.Host.CommandAsAsync<AttemptSaveDto>("SaveAttemptWorkspace",
            new SaveAttemptWorkspaceCommand(attempt.AttemptId, Guid.NewGuid(), snapshot.ConnectionEpoch,
                attempt.RowVersion, 1, Workspace, ExerciseContentHash.ComputeWorkspace(Workspace)), Context);
        Assert.Equal(2, saved.RowVersion);

        await ExecuteAsync("""UPDATE participant_state SET resume_context_json = NULL;""");
        await Control("Resume", joined.Joined.ParticipantId);

        var state = await StateRowAsync();
        Assert.Equal(0L, state["recovery_blocked"]);
        Assert.Equal(0L, state["interaction_enabled"]);
        Assert.Equal(attempt.AttemptId.ToString("D"), state["attempt_id"]);
        Assert.Equal(Workspace, await WorkspaceRowAsync());

        await AcceptedAsync("EnableActivity", joined.Joined.ParticipantId);
        Assert.Equal(1L, (await StateRowAsync())["interaction_enabled"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EsecuzioneDuranteIlBloccoDiRecupero_RifiutaSenzaToccareIlLavoro()
    {
        // Un tentativo incerto resta protetto: finché il blocco c'è, RunProgram non si applica e il
        // workspace salvato non cambia.
        await AcceptedAsync("EnableActivity", joined.Joined.ParticipantId);
        var run = await AcceptedAsync("RunProgram", joined.Joined.ParticipantId);
        await DispatchAsync();
        await AcknowledgeAsync(run.CommandId, run.SessionRevision, CommandAcknowledgementOutcomes.Applied);
        await scenario.Host.CommandAsAsync<AttemptSaveDto>("SaveAttemptWorkspace",
            new SaveAttemptWorkspaceCommand(attempt.AttemptId, Guid.NewGuid(), snapshot.ConnectionEpoch,
                attempt.RowVersion, 1, Workspace, ExerciseContentHash.ComputeWorkspace(Workspace)), Context);

        await ReloadWithRecoveryReportAsync();

        var rejection = await scenario.Host.ExpectFailureAsAsync("RunProgram",
            new { sessionId = seed.SessionId, expectedSessionRevision = await RevisionAsync(), participantId = joined.Joined.ParticipantId },
            scenario.RegiaContext());
        Assert.Equal(CodiErrorCodes.SessionStateConflict, rejection.Code);
        Assert.Equal(Workspace, await WorkspaceRowAsync());
        Assert.Equal(1L, await scenario.ScalarAsync<long>($"SELECT project_revision FROM exercise_attempt WHERE id = '{attempt.AttemptId:D}';"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RipresaSuAlunnoNonBloccato_RifiutaPercheNessunoStatoRipristinabile()
    {
        // Lo sblocco non è un pulsante magico: senza pausa docente né di recupero il server lo
        // rifiuta, così una ripressione non può riscrivere lo stato di un alunno attivo.
        var rejection = await scenario.Host.ExpectFailureAsAsync("Resume",
            new { sessionId = seed.SessionId, expectedSessionRevision = await RevisionAsync(), participantId = joined.Joined.ParticipantId },
            scenario.RegiaContext());
        Assert.Equal(CodiErrorCodes.SessionStateConflict, rejection.Code);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await scenario.Host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    internal const string Definition = """
    {"schemaVersion":1,"exerciseType":"GridPath","engineVersion":"gridpath-1","title":"Stella","instructions":"Vai alla stella.",
    "scene":{"width":5,"height":4,"start":{"x":0,"y":1,"direction":"East"},"obstacles":[],"goal":{"x":3,"y":1}},
    "toolbox":{"allowedBlocks":["aula_start","aula_move","aula_turn","aula_repeat"]},
    "limits":{"maxBlocks":30,"maxNestingDepth":8,"maxExecutionSteps":1000,"maxActiveDurationMs":120000},
    "completion":{"type":"ReachCell","x":3,"y":1},"feedback":{"success":"Bravo!","collision":"Ostacolo."}}
    """;
}
