using Qwerty.Codi.Application.Dto;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Exercises;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.IntegrationTests.Realtime;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Sessions;

public sealed class AttemptSaveTests : IAsyncLifetime
{
    private OutboxScenario scenario = null!;
    private Seed seed = null!;
    private JoinSessionOutcome joined = null!;
    private ParticipantSnapshotDto snapshot = null!;
    private AttemptDto attempt = null!;
    private Guid versionId;
    private CodiRequestContext Context => new(true,
        [CodiPolicy.AdmittedParticipant, CodiPolicy.ActiveParticipantConnection, CodiPolicy.FinalizingParticipant],
        joined.Joined.CredentialSessionId, participantId: joined.Joined.ParticipantId);
    private const string Workspace = """{"blocks":{"languageVersion":0,"blocks":[{"type":"aula_start","id":"start"}]}}""";
    private static string Hash(string text) => ExerciseContentHash.ComputeWorkspace(text);

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
        await Assign();
        await Synchronize();
        attempt = await scenario.Host.QueryOneAsAsync<AttemptDto>("GetAttempt", new { }, Context);
    }
    public async Task DisposeAsync() => await scenario.DisposeAsync();
    private async Task<long> Revision() => await scenario.ScalarAsync<long>("SELECT session_revision FROM lesson_session;");
    private Task<object?> Assign() => Control("AssignExercise", versionId);
    private async Task<object?> Control(string name, Guid? version = null) =>
        await scenario.Host.ExecuteAsAsync(CodiRequestKind.Command, name,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await Revision(),
                exerciseVersionId = version,
                participantId = name == "ResetWork" ? (Guid?)joined.Joined.ParticipantId : null,
                attemptId = name == "ResetWork" ? (Guid?)attempt.AttemptId : null,
                attemptRowVersion = attempt?.RowVersion
            }, scenario.RegiaContext());
    private async Task Synchronize()
    {
        snapshot = await scenario.Host.CommandAsAsync<ParticipantSnapshotDto>("InitializeParticipantConnection",
            new { protocolVersion = 1, clientInstanceId = Guid.NewGuid(), appVersion = "test", engineVersion = "regia-demo-1" }, Context);
        var registry = scenario.Host.Services.GetRequiredService<IParticipantConnectionRegistry>();
        registry.Acquire(new(joined.Joined.ParticipantId, seed.SessionId, Guid.NewGuid(), "client", snapshot.ConnectionEpoch, snapshot.EffectiveRevision), CodiUseCaseHost.Now);
        registry.MarkSnapshotApplied(new(joined.Joined.ParticipantId, "client", snapshot.ConnectionEpoch, snapshot.EffectiveRevision), CodiUseCaseHost.Now);
    }
    private SaveAttemptWorkspaceCommand Save() => new(attempt.AttemptId, Guid.NewGuid(), snapshot.ConnectionEpoch,
        attempt.RowVersion, 1, Workspace, Hash(Workspace));
    private AttemptCheckpoint Checkpoint() => new(1, "regia-demo-1", attempt.AttemptId, attempt.ExerciseVersionId,
        snapshot.DesiredState.ExecutionGeneration ?? 0, Guid.NewGuid(), attempt.WorkspaceHash, attempt.ProjectRevision, "Paused",
        JsonSerializer.SerializeToElement(new { progress = 8 }));
    private Task<AttemptSaveDto> Save(SaveAttemptWorkspaceCommand request) => scenario.Host.CommandAsAsync<AttemptSaveDto>("SaveAttemptWorkspace", request, Context);
    private async Task<AttemptFinalizationGrantDto> Grant() => Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<AttemptFinalizationGrantDto>>(
        await scenario.Host.ExecuteAsAsync(CodiRequestKind.Query, "GetAttemptFinalizations", new { }, Context)));
    private FinalizeAttemptCommand Final(AttemptFinalizationGrantDto grant, AttemptCheckpoint? checkpoint = null) =>
        new(attempt.AttemptId, grant.GrantId, Guid.NewGuid(), grant.ConnectionEpoch, attempt.RowVersion, 1, Workspace, Hash(Workspace), checkpoint);

    [Fact]
    public async Task Progetto_RicevutaIdempotente_EStoricoDocente()
    {
        var request = Save();
        var receipt = await Save(request);
        Assert.Equal(2, receipt.RowVersion); Assert.Equal(1, receipt.ProjectRevision);
        Assert.Equal(CodiUseCaseHost.Now, receipt.LastSavedAtUtc);
        Assert.Equal(receipt, await Save(request));
        var read = await scenario.Host.QueryOneAsAsync<AttemptDto>("GetAttempt", new { }, Context);
        Assert.Equal(Workspace, read.WorkspaceJson); Assert.Equal(Hash(Workspace), read.WorkspaceHash);
        var history = await scenario.Host.ExecuteAsAsync(CodiRequestKind.Query, "ListParticipantAttempts", new { sessionId = seed.SessionId, participantId = joined.Joined.ParticipantId }, scenario.RegiaContext());
        // Il contesto docente deve portare l'identità verificata.
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<AttemptDto>>(history));
    }
    [Theory]
    [InlineData("version", "RowVersionMismatch")]
    [InlineData("missingVersion", "EntityVersionRequired")]
    [InlineData("hash", "ValidationFailed")]
    [InlineData("revision", "SessionStateConflict")]
    [InlineData("negativeRevision", "SessionStateConflict")]
    [InlineData("epoch", "SessionStateConflict")]
    [InlineData("invalidEpoch", "InvalidPayload")]
    [InlineData("saveId", "InvalidPayload")]
    [InlineData("attemptId", "InvalidPayload")]
    [InlineData("missingAttempt", "ResourceNotFound")]
    [InlineData("workspace", "InvalidPayload")]
    [InlineData("oversize", "PayloadTooLarge")]
    [InlineData("unsynchronized", "SessionStateConflict")]
    [InlineData("revoked", "AuthenticationRequired")]
    [InlineData("ownership", "ResourceNotFound")]
    public async Task Progetto_RifiutoNonModificaIlTentativo(string defect, string expected)
    {
        var request = Save();
        request = defect switch
        {
            "version" => request with { BaseRowVersion = 99 },
            "missingVersion" => request with { BaseRowVersion = null },
            "hash" => request with { WorkspaceHash = "wrong" },
            "revision" => request with { ClientProjectRevision = 0 },
            "negativeRevision" => request with { ClientProjectRevision = -1 },
            "epoch" => request with { ConnectionEpoch = snapshot.ConnectionEpoch + 1 },
            "invalidEpoch" => request with { ConnectionEpoch = 0 },
            "saveId" => request with { SaveId = Guid.Empty },
            "attemptId" => request with { AttemptId = Guid.Empty },
            "missingAttempt" => request with { AttemptId = Guid.NewGuid() },
            "workspace" => request with { WorkspaceJson = "{}broken" },
            "oversize" => request with { WorkspaceJson = new string('a', 262145) },
            _ => request
        };
        if (defect == "unsynchronized") scenario.Host.Services.GetRequiredService<IParticipantConnectionRegistry>().NoteDesiredRevision(joined.Joined.ParticipantId, 99);
        if (defect == "revoked") await Execute("UPDATE credential_session SET revoked_at_utc_ms = 1;");
        var context = defect == "ownership" ? new CodiRequestContext(true, [CodiPolicy.ActiveParticipantConnection],
            joined.Joined.CredentialSessionId, participantId: Guid.NewGuid()) : Context;
        var error = await scenario.Host.ExpectFailureAsAsync("SaveAttemptWorkspace", request, context);
        Assert.Equal(expected, error.Code);
        Assert.Equal(0, await scenario.ScalarAsync<long>("SELECT project_revision FROM exercise_attempt;"));
    }
    [Fact]
    public async Task SaveId_ConAltroCorpo_Rifiuta()
    {
        var request = Save(); await Save(request);
        var error = await scenario.Host.ExpectFailureAsAsync("SaveAttemptWorkspace", request with { ClientProjectRevision = 2 }, Context);
        Assert.Equal(CodiErrorCodes.IdempotencyKeyConflict, error.Code);
    }
    [Fact]
    public async Task Checkpoint_Persistito_EInvalidatoDallaModifica()
    {
        var receipt = await scenario.Host.CommandAsAsync<AttemptSaveDto>("SaveAttemptCheckpoint",
            new SaveAttemptCheckpointCommand(attempt.AttemptId, Guid.NewGuid(), snapshot.ConnectionEpoch, 1, Checkpoint()), Context);
        Assert.True(receipt.CheckpointSaved);
        Assert.Contains("regia-demo-1", await scenario.ScalarAsync<string>("SELECT checkpoint_json FROM exercise_attempt;"));
        await Save(Save() with { BaseRowVersion = receipt.RowVersion });
        Assert.Equal(0, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM exercise_attempt WHERE checkpoint_json IS NOT NULL;"));
    }
    [Theory]
    [InlineData("schema")]
    [InlineData("engine")]
    [InlineData("attempt")]
    [InlineData("version")]
    [InlineData("generation")]
    [InlineData("hash")]
    [InlineData("revision")]
    [InlineData("run")]
    [InlineData("status")]
    [InlineData("shape")]
    [InlineData("progress")]
    [InlineData("progressType")]
    [InlineData("oversize")]
    public async Task Checkpoint_Incompatibile_RifiutaSenzaScrittura(string defect)
    {
        var checkpoint = Checkpoint();
        checkpoint = defect switch
        {
            "schema" => checkpoint with { SchemaVersion = 2 },
            "engine" => checkpoint with { EngineVersion = "other" },
            "attempt" => checkpoint with { AttemptId = Guid.NewGuid() },
            "version" => checkpoint with { ExerciseVersionId = Guid.NewGuid() },
            "generation" => checkpoint with { ExecutionGeneration = 99 },
            "hash" => checkpoint with { WorkspaceHash = "wrong" },
            "revision" => checkpoint with { ProjectRevision = 99 },
            "run" => checkpoint with { RunId = Guid.Empty },
            "status" => checkpoint with { RuntimeStatus = "Flying" },
            "shape" => checkpoint with { RuntimeState = JsonSerializer.SerializeToElement(1) },
            "progress" => checkpoint with { RuntimeState = JsonSerializer.SerializeToElement(new { progress = 41 }) },
            "progressType" => checkpoint with { RuntimeState = JsonSerializer.SerializeToElement(new { progress = "eight" }) },
            "oversize" => checkpoint with { RuntimeState = JsonSerializer.SerializeToElement(new { progress = 8, padding = new string('a', 65537) }) },
            _ => checkpoint
        };
        var error = await scenario.Host.ExpectFailureAsAsync("SaveAttemptCheckpoint",
            new SaveAttemptCheckpointCommand(attempt.AttemptId, Guid.NewGuid(), snapshot.ConnectionEpoch, 1, checkpoint), Context);
        Assert.Contains(error.Code, new[] { CodiErrorCodes.ValidationFailed, CodiErrorCodes.InvalidPayload, CodiErrorCodes.PayloadTooLarge });
        Assert.Equal(1, await scenario.ScalarAsync<long>("SELECT row_version FROM exercise_attempt;"));
    }
    [Theory]
    [InlineData("EndSession")]
    [InlineData("AssignExercise")]
    [InlineData("ResetWork")]
    public async Task Finalizzazione_AggiornaSoloIlVecchioTentativo_UnaVolta(string command)
    {
        await Control(command, command == "AssignExercise" ? versionId : null);
        var grant = await Grant();
        var request = Final(grant, Checkpoint() with { WorkspaceHash = Hash(Workspace), ProjectRevision = 1 });
        var receipt = await scenario.Host.CommandAsAsync<AttemptSaveDto>("FinalizeAttempt", request, Context);
        Assert.True(receipt.CheckpointSaved);
        Assert.Equal(receipt, await scenario.Host.CommandAsAsync<AttemptSaveDto>("FinalizeAttempt", request, Context));
        var rejected = await scenario.Host.ExpectFailureAsAsync("FinalizeAttempt", request with { SaveId = Guid.NewGuid() }, Context);
        Assert.Equal(CodiErrorCodes.SessionStateConflict, rejected.Code);
        Assert.Equal(Workspace, await scenario.ScalarAsync<string>($"SELECT workspace_json FROM exercise_attempt WHERE id = '{attempt.AttemptId:D}';"));
        if (command != "EndSession")
        {
            Assert.NotEqual(attempt.AttemptId.ToString("D"), await scenario.ScalarAsync<string>("SELECT attempt_id FROM participant_state;"));
            Assert.Equal(0, await scenario.ScalarAsync<long>($"SELECT project_revision FROM exercise_attempt WHERE id <> '{attempt.AttemptId:D}';"));
        }
    }
    [Fact]
    public async Task Finalizzazione_Incompatibile_NonConsumaGrantENonSalvaProgetto()
    {
        await Control("EndSession"); var grant = await Grant();
        var error = await scenario.Host.ExpectFailureAsAsync("FinalizeAttempt", Final(grant, Checkpoint()), Context);
        Assert.Equal(CodiErrorCodes.ValidationFailed, error.Code);
        Assert.Equal(0, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM attempt_finalization_grant WHERE used_at_utc_ms IS NOT NULL;"));
        Assert.Equal(0, await scenario.ScalarAsync<long>("SELECT project_revision FROM exercise_attempt;"));
    }
    [Theory]
    [InlineData("expired")]
    [InlineData("missing")]
    [InlineData("credential")]
    [InlineData("epoch")]
    public async Task Grant_EstraneoOScaduto_Rifiuta(string defect)
    {
        var otherId = defect == "credential" ? (await scenario.JoinAsync(seed, "Anna", "anna")).Joined.CredentialSessionId : Guid.Empty;
        await Control("EndSession"); var grant = await Grant(); var request = Final(grant);
        if (defect == "expired") await Execute("UPDATE attempt_finalization_grant SET expires_at_utc_ms = 1;");
        if (defect == "missing") request = request with { GrantId = Guid.NewGuid() };
        if (defect == "credential") await Execute($"UPDATE attempt_finalization_grant SET credential_session_id = '{otherId:D}' WHERE attempt_id = '{attempt.AttemptId:D}';");
        if (defect == "epoch") await Execute("UPDATE attempt_finalization_grant SET connection_epoch = 99;");
        var error = await scenario.Host.ExpectFailureAsAsync("FinalizeAttempt", request, Context);
        Assert.Equal(CodiErrorCodes.PermissionDenied, error.Code);
    }
    [Fact]
    public async Task FinestreTerminali_Scadute_RevocanoCredenziali()
    {
        await Control("EndSession");
        var time = new FixedTime(CodiUseCaseHost.Now.AddSeconds(31));
        var cleanup = new FinalizationCleanup(scenario.Host.Services.GetRequiredService<IUnitOfWorkContext>(),
            scenario.Host.Services.GetRequiredService<ISessionCoordinator>(), time);
        await cleanup.ExpireAsync(CancellationToken.None);
        Assert.Equal(1, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM credential_session WHERE participant_id IS NOT NULL AND revoked_at_utc_ms IS NOT NULL;"));
    }
    [Theory]
    [InlineData("Active")]
    [InlineData("Paused")]
    [InlineData("Ended")]
    [InlineData("Lobby")]
    public async Task Ripartenza_NessunReplay_NessunaPartecipazioneDuplicata(string status)
    {
        await Save(Save());
        if (status == "Paused") await Control("Pause");
        if (status == "Ended") await Control("EndSession");
        if (status == "Lobby") await Execute("UPDATE lesson_session SET status = 'Lobby', started_at_utc_ms = NULL, join_code = NULL;");
        var revision = await Revision();
        var service = scenario.Host.Services.GetRequiredService<SessionStartupRecovery>();
        var count = await service.RecoverAsync();
        Assert.Equal(status == "Ended" ? 0 : 1, count);
        Assert.Equal(1, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
        Assert.Equal(1, await scenario.ScalarAsync<long>("SELECT project_revision FROM exercise_attempt;"));
        if (status == "Ended") { Assert.Equal(revision, await Revision()); return; }
        Assert.Equal(revision + 1, await Revision());
        Assert.Equal(status == "Lobby" ? "Lobby" : "Paused", await scenario.ScalarAsync<string>("SELECT status FROM lesson_session;"));
        Assert.Equal(1, await scenario.ScalarAsync<long>("SELECT recovery_blocked FROM participant_state;"));
        Assert.Equal(0, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM participant_state WHERE directive_json IS NOT NULL;"));
        Assert.Equal(0, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM outbox_message WHERE processed_at_utc_ms IS NULL;"));
        Assert.Null(scenario.Host.Services.GetRequiredService<IParticipantConnectionRegistry>().Get(joined.Joined.ParticipantId));
        await Synchronize();
        Assert.True(snapshot.DesiredState.RecoveryBlocked);
        Assert.Equal(1, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
    }
    private async Task Execute(string sql)
    {
        await using var connection = await scenario.Host.OpenConnectionAsync(); await using var command = connection.CreateCommand();
        command.CommandText = sql; await command.ExecuteNonQueryAsync();
    }
    private sealed class FixedTime(DateTimeOffset instant) : TimeProvider { public override DateTimeOffset GetUtcNow() => instant; }
    internal const string Definition = """
    {"schemaVersion":1,"exerciseType":"GridPath","engineVersion":"gridpath-1","title":"Stella","instructions":"Vai alla stella.",
    "scene":{"width":5,"height":4,"start":{"x":0,"y":1,"direction":"East"},"obstacles":[],"goal":{"x":3,"y":1}},
    "toolbox":{"allowedBlocks":["aula_start","aula_move","aula_turn","aula_repeat"]},
    "limits":{"maxBlocks":30,"maxNestingDepth":8,"maxExecutionSteps":1000,"maxActiveDurationMs":120000},
    "completion":{"type":"ReachCell","x":3,"y":1},"feedback":{"success":"Bravo!","collision":"Ostacolo."}}
    """;
}


