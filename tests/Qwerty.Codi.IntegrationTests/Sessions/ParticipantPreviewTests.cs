using Qwerty.Codi.Application.Dto;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Api.Hubs;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Exercises;
using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.IntegrationTests.Realtime;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Sessions;

/// <summary>
/// Anteprima dei blocchi (§15.5): non è un salvataggio, vive in una cache in memoria e il docente la
/// legge quando vuole il contenuto. Ciò che si verifica è che un'anteprima valida diventi leggibile,
/// che una superata non sostituisca la corrente e che il confronto con il progetto persistito
/// dichiari quando l'anteprima è solo parziale.
/// </summary>
public sealed class ParticipantPreviewTests : IAsyncLifetime
{
    private OutboxScenario scenario = null!;
    private Seed seed = null!;
    private JoinSessionOutcome joined = null!;
    private ParticipantSnapshotDto snapshot = null!;
    private AttemptDto attempt = null!;

    private const string Workspace =
        """{"blocks":{"languageVersion":0,"blocks":[{"type":"aula_start","id":"start","next":{"block":{"type":"aula_move","id":"move-1"}}}]}}""";

    private static readonly string Definition = JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        exerciseType = "GridPath",
        engineVersion = "gridpath-1",
        title = "Stella",
        instructions = "Arriva alla stella.",
        scene = new
        {
            width = 5,
            height = 4,
            start = new { x = 0, y = 1, direction = "East" },
            obstacles = Array.Empty<object>(),
            goal = new { x = 3, y = 1 },
        },
        toolbox = new { allowedBlocks = new[] { "aula_start", "aula_move", "aula_turn", "aula_repeat" } },
        limits = new { maxBlocks = 30, maxNestingDepth = 8, maxExecutionSteps = 1000, maxActiveDurationMs = 120000 },
        completion = new { type = "ReachCell", x = 3, y = 1 },
        feedback = new { success = "Bravo!", collision = "Ops!" },
    });

    private CodiRequestContext ParticipantContext => new(true,
        [CodiPolicy.AdmittedParticipant, CodiPolicy.ActiveParticipantConnection],
        joined.Joined.CredentialSessionId, participantId: joined.Joined.ParticipantId);

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
        await scenario.Host.ExecuteAsAsync(CodiRequestKind.Command, "AssignExercise",
            new { sessionId = seed.SessionId, expectedSessionRevision = await Revision(), exerciseVersionId = version.Id },
            scenario.RegiaContext());
        await SynchronizeAsync();
        attempt = await scenario.Host.QueryOneAsAsync<AttemptDto>("GetAttempt", new { }, ParticipantContext);
    }

    public async Task DisposeAsync() => await scenario.DisposeAsync();

    private Task<long> Revision() => scenario.ScalarAsync<long>("SELECT session_revision FROM lesson_session;");

    /// <summary>Una variante valida del progetto: due passi invece di uno.</summary>
    private static readonly string ChangedWorkspace =
        """{"blocks":{"languageVersion":0,"blocks":[{"type":"aula_start","id":"start","next":{"block":{"type":"aula_move","id":"move-1","next":{"block":{"type":"aula_move","id":"move-2"}}}}}]}}""";

    private async Task SynchronizeAsync()
    {
        snapshot = await scenario.Host.CommandAsAsync<ParticipantSnapshotDto>("InitializeParticipantConnection",
            new { protocolVersion = 1, clientInstanceId = Guid.NewGuid(), appVersion = "test", engineVersion = "gridpath-1" },
            ParticipantContext);
        var registry = scenario.Host.Services.GetRequiredService<IParticipantConnectionRegistry>();
        registry.Acquire(new(joined.Joined.ParticipantId, seed.SessionId, Guid.NewGuid(), "client", snapshot.ConnectionEpoch, snapshot.EffectiveRevision), CodiUseCaseHost.Now);
        registry.MarkSnapshotApplied(new(joined.Joined.ParticipantId, "client", snapshot.ConnectionEpoch, snapshot.EffectiveRevision), CodiUseCaseHost.Now);
    }

    /// <summary>Anteprima coerente col tentativo corrente: impronta e revisione del progetto salvato.</summary>
    private PublishWorkspacePreviewCommand Preview(string? workspace = null, string? hash = null,
        long? generation = null, long? epoch = null) =>
        new(attempt.AttemptId, epoch ?? snapshot.ConnectionEpoch,
            generation ?? snapshot.DesiredState.ExecutionGeneration ?? 0, attempt.ProjectRevision,
            hash ?? attempt.WorkspaceHash, workspace ?? attempt.WorkspaceJson);

    private Task<WorkspacePreviewDto> Publish(PublishWorkspacePreviewCommand request) =>
        scenario.Host.CommandAsAsync<WorkspacePreviewDto>("PublishWorkspacePreview", request, ParticipantContext);

    // La query dichiara null quando non c'è anteprima: si legge il risultato grezzo, non con
    // QueryOneAsAsync, che pretende un tipo valorizzato.
    private async Task<ParticipantPreviewViewDto?> Read() =>
        await scenario.Host.ExecuteAsAsync(CodiRequestKind.Query, "GetParticipantPreview",
            new { sessionId = seed.SessionId, participantId = joined.Joined.ParticipantId }, scenario.RegiaContext())
            as ParticipantPreviewViewDto;

    [Fact]
    public async Task Anteprima_CoerenteColProgetto_IlDocenteLaLeggeComeCompleta()
    {
        var receipt = await Publish(Preview());

        Assert.Equal(joined.Joined.ParticipantId, receipt.ParticipantId);
        Assert.Equal(seed.SessionId, receipt.SessionId);
        Assert.Equal(attempt.WorkspaceHash, receipt.WorkspaceHash);

        var read = await Read();
        Assert.NotNull(read);
        Assert.Equal(attempt.WorkspaceJson, read.WorkspaceJson);
        Assert.False(read.Partial);
        Assert.Equal(CodiUseCaseHost.Now, read.ReceivedAtUtc);
        Assert.Equal(attempt.AttemptId, read.AttemptId);
    }

    [Fact]
    public async Task Anteprima_DiUnProgettoNonAncoraSalvato_IlDocenteLaVedeParziale()
    {
        // Il progetto dell'alunno è cambiato ma non è stato salvato: l'anteprima descrive qualcosa
        // che il server non ha ancora ricevuto, quindi va mostrata come parziale (§15.5).
        await Publish(Preview(ChangedWorkspace, ExerciseContentHash.ComputeWorkspace(ChangedWorkspace)));

        var read = await Read();

        Assert.NotNull(read);
        Assert.True(read.Partial);
    }

    [Fact]
    public async Task Anteprima_DiUnaGenerazioneSuperata_NonSostituisceLaCorrente()
    {
        await Publish(Preview());

        var failure = await Assert.ThrowsAsync<CodiRequestException>(() => Publish(Preview(generation: 999)));

        Assert.Equal(CodiErrorCodes.SessionStateConflict, failure.Code);
        Assert.Equal(attempt.WorkspaceHash, (await Read())!.WorkspaceHash);
    }

    [Fact]
    public async Task Anteprima_ConConnessioneNonSincronizzata_Rifiutata()
    {
        var failure = await Assert.ThrowsAsync<CodiRequestException>(() => Publish(Preview(epoch: snapshot.ConnectionEpoch + 1)));

        Assert.Equal(CodiErrorCodes.SessionStateConflict, failure.Code);
        Assert.Null(await Read());
    }

    [Fact]
    public async Task Anteprima_SenzaUnTentativoAssegnato_Rifiutata()
    {
        var failure = await Assert.ThrowsAsync<CodiRequestException>(() =>
            Publish(Preview() with { AttemptId = Guid.NewGuid() }));

        Assert.Equal(CodiErrorCodes.SessionStateConflict, failure.Code);
    }

    [Fact]
    public async Task Cache_ConRevisioneSuperata_NonRetrocede()
    {
        var cache = scenario.Host.Services.GetRequiredService<IParticipantPreviewCache>();
        var nowUtc = CodiUseCaseHost.Now;
        var first = new ParticipantPreviewDto(seed.SessionId, joined.Joined.ParticipantId, attempt.AttemptId,
            1, 0, 5, "hash", "{}", nowUtc);

        Assert.True(cache.Update(first));
        Assert.False(cache.Update(first with { ProjectRevision = 4 }));
        Assert.Equal(5, cache.Get(joined.Joined.ParticipantId)!.ProjectRevision);

        cache.Remove(joined.Joined.ParticipantId);
        Assert.Null(cache.Get(joined.Joined.ParticipantId));
        Assert.Null(cache.Get(Guid.NewGuid()));
    }

    [Fact]
    public async Task Cache_ConAnteprimaScaduta_NonLaMostraPiu()
    {
        var cache = scenario.Host.Services.GetRequiredService<IParticipantPreviewCache>();
        // Un'anteprima vecchia mostrata come fresca sarebbe una bugia: oltre la scadenza sparisce.
        cache.Update(Cached(joined.Joined.ParticipantId, CodiUseCaseHost.Now - TimeSpan.FromSeconds(31)));

        Assert.Null(cache.Get(joined.Joined.ParticipantId));
    }

    [Fact]
    public async Task Cache_OltreLaCapienza_ScartaLaPiuVecchia()
    {
        var cache = scenario.Host.Services.GetRequiredService<IParticipantPreviewCache>();
        var oldest = Guid.NewGuid();
        var newest = Guid.NewGuid();
        for (var index = 0; index < 128; index++)
            cache.Update(Cached(index == 0 ? oldest : Guid.NewGuid(),
                CodiUseCaseHost.Now.AddMilliseconds(-(128 - index))));
        cache.Update(Cached(newest, CodiUseCaseHost.Now));

        Assert.Null(cache.Get(oldest));
        Assert.Equal(1, cache.Get(newest)!.ProjectRevision);
    }

    private ParticipantPreviewDto Cached(Guid participantId, DateTimeOffset receivedAtUtc) =>
        new(seed.SessionId, participantId, attempt.AttemptId, snapshot.ConnectionEpoch, 0, 1, "hash", "{}",
            receivedAtUtc);

    [Fact]
    public async Task MetodoHub_PubblicaLAnteprimaEAvvisaIDocentiSenzaSalvare()
    {
        var commands = await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM session_command;");
        var teacherClients = new FakeHubClients();
        var hub = new ParticipantHub(new StubContextProvider(ParticipantContext), scenario.Host.Executor,
            scenario.Host.Services.GetRequiredService<IParticipantConnectionRegistry>(),
            scenario.Host.Services.GetRequiredService<IServerInstance>(),
            scenario.Host.Services.GetRequiredService<TimeProvider>(),
            scenario.Host.Services.GetRequiredService<ParticipantObservationService>(),
            coordinator: null,
            previews: new WorkspacePreviewNotifier(new NotifierHubContext(teacherClients)))
        {
            Context = new FakeHubCallerContext(new DefaultHttpContext(), "client"),
            Clients = new FakeHubClients(),
        };

        await hub.WorkspacePreviewUpdated(new WorkspacePreviewArguments(1, attempt.AttemptId,
            snapshot.ConnectionEpoch, snapshot.DesiredState.ExecutionGeneration ?? 0,
            attempt.ProjectRevision, attempt.WorkspaceHash, attempt.WorkspaceJson));

        // Il trasporto traduce: l'anteprima è in cache, il docente è avvisato, nulla è persistito.
        Assert.Equal(attempt.WorkspaceJson, (await Read())!.WorkspaceJson);
        var sent = Assert.Single(teacherClients.Sent);
        Assert.Equal($"group:{CodiHubGroups.Teachers(seed.SessionId)}",
            sent.Target);
        Assert.Equal(TeacherRealtimeEvents.WorkspacePreviewAvailable, sent.Method);
        Assert.Equal(commands, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM session_command;"));
    }

    [Fact]
    public async Task MetodoHub_OltreIlLimiteDelMessaggio_RifiutaLAnteprima()
    {
        var hub = new ParticipantHub(new StubContextProvider(ParticipantContext), scenario.Host.Executor,
            scenario.Host.Services.GetRequiredService<IParticipantConnectionRegistry>(),
            scenario.Host.Services.GetRequiredService<IServerInstance>(),
            scenario.Host.Services.GetRequiredService<TimeProvider>(),
            scenario.Host.Services.GetRequiredService<ParticipantObservationService>())
        {
            Context = new FakeHubCallerContext(new DefaultHttpContext(), "client"),
            Clients = new FakeHubClients(),
        };

        var failure = await Assert.ThrowsAsync<CodiRequestException>(() => hub.WorkspacePreviewUpdated(
            new WorkspacePreviewArguments(1, attempt.AttemptId, snapshot.ConnectionEpoch,
                snapshot.DesiredState.ExecutionGeneration ?? 0, attempt.ProjectRevision, attempt.WorkspaceHash,
                $"{{\"pad\":\"{new string('x', 25 * 1024)}\"}}")));

        Assert.Equal(CodiErrorCodes.InvalidPayload, failure.Code);
        Assert.Null(await Read());
    }

    private sealed class NotifierHubContext(FakeHubClients clients) : IHubContext<TeacherHub>
    {
        public IHubClients Clients => clients;
        public IGroupManager Groups { get; } = new FakeGroupManager();
    }
}
