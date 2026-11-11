using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR;
using Qwerty.Codi.Application.Dto;
using System.Text.Json;
using Qwerty.Codi.Api.Hubs;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.Domain.Sessions;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Realtime;

[Trait("Category", "Integration")]
public sealed class T4CompletionTests
{
    [Fact]
    public async Task Regia_RispostaPersa_RestituisceLoStessoComandoSenzaNuoveScritture()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        var context = Keyed(scenario, "request-1");
        var body = new { sessionId = seed.SessionId, expectedSessionRevision = 2L };
        var first = await scenario.Host.CommandAsAsync<CommandAcceptedDto>("ShowWaiting", body, context);
        var second = await scenario.Host.CommandAsAsync<CommandAcceptedDto>("ShowWaiting", body, context);
        Assert.Equal(first, second);
        Assert.Equal(1L, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM session_command;"));
        Assert.Equal(1L, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM outbox_message;"));
        Assert.Equal(3L, await scenario.ScalarAsync<long>("SELECT session_revision FROM lesson_session;"));
    }

    [Fact]
    public async Task Regia_StessaChiaveConContenutoDiverso_RifiutaSenzaMutazioni()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.Host.CommandAsAsync<CommandAcceptedDto>("ShowWaiting",
            new { sessionId = seed.SessionId, expectedSessionRevision = 2L }, Keyed(scenario, "request-1"));
        var failure = await scenario.Host.ExpectFailureAsAsync("ShowWaiting",
            new { sessionId = seed.SessionId, expectedSessionRevision = 3L }, Keyed(scenario, "request-1"));
        Assert.Equal(CodiErrorCodes.IdempotencyKeyConflict, failure.Code);
        Assert.Equal(1L, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM session_command;"));
    }

    [Fact]
    public async Task Regia_RichiesteConcorrenti_RestituisceUnSoloComando()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        var body = new { sessionId = seed.SessionId, expectedSessionRevision = 2L };
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            scenario.Host.CommandAsAsync<CommandAcceptedDto>("ShowWaiting", body, Keyed(scenario, "concurrent"))));
        Assert.Single(results.Select(row => row.CommandId).Distinct());
        Assert.Equal(1L, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM session_command;"));
    }

    [Fact]
    public async Task Regia_PrecondizioneRifiutata_NonRegistraIdempotenzaOOutbox()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        var failure = await scenario.Host.ExpectFailureAsAsync("EnableActivity",
            new { sessionId = seed.SessionId, expectedSessionRevision = 2L }, Keyed(scenario, "invalid"));
        Assert.Equal(CodiErrorCodes.SessionStateConflict, failure.Code);
        Assert.Equal(0L, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM idempotency_record WHERE scope_key LIKE 'regia:%';"));
        Assert.Equal(0L, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM outbox_message;"));
    }

    [Fact]
    public async Task PausaIndividuale_RipresaIndividuale_ConservaLoStatoDellaSessione()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        var joined = await scenario.JoinAsync(seed, "Luca");
        var participantId = joined.Joined.ParticipantId;
        await scenario.Host.CommandAsAsync<SessionDto>("Pause",
            new { sessionId = seed.SessionId, expectedSessionRevision = 2L, participantId }, Keyed(scenario, "pause"));
        var snapshot = await Overview(scenario, seed);
        Assert.Equal("Active", snapshot.Session.Status);
        Assert.True(Assert.Single(snapshot.Participants).TeacherPaused);
        await scenario.Host.CommandAsAsync<SessionDto>("Resume",
            new { sessionId = seed.SessionId, expectedSessionRevision = 3L, participantId }, Keyed(scenario, "resume"));
        Assert.False(Assert.Single((await Overview(scenario, seed)).Participants).TeacherPaused);
        Assert.Equal(2L, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM command_delivery;"));
    }

    [Fact]
    public async Task Chiusura_PartecipantePresente_InviaGoodbyeERipeteLEsitoRegistrato()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        var body = new { sessionId = seed.SessionId, expectedSessionRevision = 2L };
        var response = await scenario.Host.CommandAsAsync<SessionDto>("EndSession", body, Keyed(scenario, "end"));
        var replay = await scenario.Host.CommandAsAsync<SessionDto>("EndSession", body, Keyed(scenario, "end"));
        Assert.Equal(response, replay);
        var overview = await Overview(scenario, seed);
        Assert.Equal("Goodbye", Assert.Single(overview.Participants).View);
        Assert.Equal("EndSession", Assert.Single(overview.Commands).CommandName);
        Assert.Equal(1L, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM outbox_message;"));
    }

    [Fact]
    public async Task Telemetria_SequenzaSuperataOTentativoDiverso_NonSostituisceLoStatoOsservato()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        var joined = await scenario.JoinAsync(seed, "Luca");
        var id = joined.Joined.ParticipantId;
        var snapshot = await scenario.Host.CommandAsAsync<ParticipantSnapshotDto>("InitializeParticipantConnection",
            new { protocolVersion = 1, clientInstanceId = Guid.NewGuid(), appVersion = "test", engineVersion = "regia-demo-1" },
            CodiUseCaseHost.ParticipantContext(joined.Joined.CredentialSessionId, id));
        var connections = scenario.Host.Services.GetRequiredService<IParticipantConnectionRegistry>();
        connections.Acquire(new(id, seed.SessionId, Guid.NewGuid(), "c1", snapshot.ConnectionEpoch, snapshot.EffectiveRevision), CodiUseCaseHost.Now);
        connections.MarkSnapshotApplied(new(id, "c1", snapshot.ConnectionEpoch, snapshot.EffectiveRevision), CodiUseCaseHost.Now);
        var context = new CodiRequestContext(true, [CodiPolicy.AdmittedParticipant, CodiPolicy.ActiveParticipantConnection], participantId: id);
        var service = scenario.Host.Services.GetRequiredService<ParticipantObservationService>();
        var update = new ActivityStateArguments(1, snapshot.ServerInstanceId, snapshot.ConnectionEpoch,
            snapshot.EffectiveRevision, null, 0, 2, "Idle", null, null, null, null);
        Assert.NotNull(await service.UpdateAsync(context, "c1", update, CancellationToken.None));
        Assert.Null(await service.UpdateAsync(context, "c1", update with { Sequence = 1, RuntimeStatus = "Running" }, CancellationToken.None));
        await Assert.ThrowsAsync<CodiRequestException>(() => service.UpdateAsync(context, "c1", update with { AttemptId = Guid.NewGuid(), Sequence = 3 }, CancellationToken.None));
        var hub = new ParticipantHub(new StubContextProvider(context), scenario.Host.Executor, connections,
            scenario.Host.Services.GetRequiredService<IServerInstance>(), scenario.Host.Services.GetRequiredService<TimeProvider>(), service)
        { Context = new FakeHubCallerContext(new Microsoft.AspNetCore.Http.DefaultHttpContext(), "c1"), Clients = new FakeHubClients() };
        await hub.ActivityStateUpdated(update with { EffectiveRevision = snapshot.EffectiveRevision - 1, Sequence = 3 });
        var malformed = await Assert.ThrowsAsync<CodiRequestException>(() => hub.ActivityStateUpdated(update with { RuntimeStatus = "Unknown" }));
        Assert.Equal(CodiErrorCodes.InvalidPayload, malformed.Code);
        Assert.Equal("Idle", Assert.Single((await Overview(scenario, seed)).Participants).ObservedState!.RuntimeStatus);
        Assert.Equal(0L, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM session_command;"));
    }

    [Fact]
    public async Task RecuperoDopoRefresh_RipresaEsplicita_RimuoveLaDirettivaPrecedente()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        var joined = await scenario.JoinAsync(seed, "Luca");
        var id = joined.Joined.ParticipantId;
        var context = CodiUseCaseHost.ParticipantContext(joined.Joined.CredentialSessionId, id);
        var snapshot = await scenario.Host.CommandAsAsync<ParticipantSnapshotDto>("InitializeParticipantConnection",
            new { protocolVersion = 1, clientInstanceId = Guid.NewGuid(), appVersion = "test", engineVersion = "regia-demo-1" }, context);
        var connections = scenario.Host.Services.GetRequiredService<IParticipantConnectionRegistry>();
        connections.Acquire(new(id, seed.SessionId, Guid.NewGuid(), "c1", snapshot.ConnectionEpoch, snapshot.EffectiveRevision), CodiUseCaseHost.Now);
        connections.MarkSnapshotApplied(new(id, "c1", snapshot.ConnectionEpoch, snapshot.EffectiveRevision), CodiUseCaseHost.Now);
        var active = new CodiRequestContext(true, [CodiPolicy.ActiveParticipantConnection], participantId: id);
        var recovered = await scenario.Host.CommandAsAsync<ParticipantSnapshotDto>("ReportRecoveryBlocked",
            new { connectionEpoch = snapshot.ConnectionEpoch, attemptId = (Guid?)null, effectiveRevision = snapshot.EffectiveRevision }, active);
        Assert.True(recovered.DesiredState.RecoveryBlocked);
        Assert.False(recovered.DesiredState.InteractionEnabled);
        await scenario.Host.CommandAsAsync<SessionDto>("Resume",
            new { sessionId = seed.SessionId, expectedSessionRevision = 2L, participantId = id }, Keyed(scenario, "recover"));
        var current = Assert.Single((await Overview(scenario, seed)).Participants);
        Assert.False(current.RecoveryBlocked); Assert.False(current.HasDirective);
        var stale = await scenario.Host.ExpectFailureAsAsync("ReportRecoveryBlocked",
            new { connectionEpoch = snapshot.ConnectionEpoch + 1, attemptId = (Guid?)null, effectiveRevision = snapshot.EffectiveRevision }, active);
        Assert.Equal(CodiErrorCodes.SessionStateConflict, stale.Code);
    }

    [Fact]
    public async Task ComandiIndividuali_ConsegneIndipendenti_NonSiAnnullano()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        var luca = await scenario.JoinAsync(seed, "Luca");
        var sara = await scenario.JoinAsync(seed, "Sara", "sara");
        await scenario.Host.CommandAsAsync<SessionDto>("Pause",
            new { sessionId = seed.SessionId, expectedSessionRevision = 2L, participantId = luca.Joined.ParticipantId }, Keyed(scenario, "luca"));
        await scenario.Host.CommandAsAsync<SessionDto>("Pause",
            new { sessionId = seed.SessionId, expectedSessionRevision = 3L, participantId = sara.Joined.ParticipantId }, Keyed(scenario, "sara"));
        var notifier = new RecordingCommandDeliveryNotifier();
        var dispatcher = new OutboxDispatcher(scenario.Host.Services.GetRequiredService<IUnitOfWorkContext>(), notifier,
            scenario.Host.Services.GetRequiredService<TimeProvider>());
        var result = await dispatcher.DispatchPendingAsync(CancellationToken.None);
        Assert.Equal(2, result.DeliveriesSent); Assert.Equal(0, result.DeliveriesSuperseded);
        Assert.Equal(2, notifier.Sent.Select(row => row.ParticipantId).Distinct().Count());
    }

    [Fact]
    public async Task Consegna_SenzaConferma_RitentaDueVolteERestaNonConfermata()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        var luca = await scenario.JoinAsync(seed, "Luca");
        var accepted = await scenario.ShowWaitingAsync(seed, 2L);
        var time = new AdjustableTimeProvider(CodiUseCaseHost.Now);
        var connections = scenario.Host.Services.GetRequiredService<IParticipantConnectionRegistry>();
        var id = luca.Joined.ParticipantId;
        connections.Acquire(new(id, seed.SessionId, Guid.NewGuid(), "c1", 1, 3), CodiUseCaseHost.Now);
        connections.MarkSnapshotApplied(new(id, "c1", 1, 3), CodiUseCaseHost.Now);
        var notifier = new RecordingCommandDeliveryNotifier();
        var dispatcher = new OutboxDispatcher(scenario.Host.Services.GetRequiredService<IUnitOfWorkContext>(), notifier, time, connections);
        await dispatcher.DispatchPendingAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(1)); await dispatcher.DispatchPendingAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(1)); await dispatcher.DispatchPendingAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(3)); await dispatcher.DispatchPendingAsync(CancellationToken.None);
        Assert.Equal(3, notifier.Sent.Count);
        Assert.All(notifier.Sent, row => Assert.Equal(accepted.CommandId, row.Message.CommandId));
        Assert.Equal("Unconfirmed", await scenario.ScalarAsync<string>("SELECT status FROM command_delivery;"));
        Assert.Equal(3L, await scenario.ScalarAsync<long>("SELECT send_attempts FROM command_delivery;"));
        Assert.Equal(CodiUseCaseHost.Now.ToUnixTimeMilliseconds(), await scenario.ScalarAsync<long>("SELECT sent_at_utc_ms FROM command_delivery;"));
    }

    [Theory]
    [InlineData("connection")]
    [InlineData("protocol")]
    [InlineData("server")]
    [InlineData("runtime")]
    [InlineData("sequence")]
    [InlineData("revision")]
    [InlineData("generation")]
    public async Task Telemetria_ContestoSuperatoODatiIncompatibili_Rifiuta(string defect)
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync(); var joined = await scenario.JoinAsync(seed, "Luca");
        var id = joined.Joined.ParticipantId;
        var snapshot = await scenario.Host.CommandAsAsync<ParticipantSnapshotDto>("InitializeParticipantConnection",
            new { protocolVersion = 1, clientInstanceId = Guid.NewGuid(), appVersion = "test", engineVersion = "regia-demo-1" },
            CodiUseCaseHost.ParticipantContext(joined.Joined.CredentialSessionId, id));
        var connections = scenario.Host.Services.GetRequiredService<IParticipantConnectionRegistry>();
        connections.Acquire(new(id, seed.SessionId, Guid.NewGuid(), "c1", snapshot.ConnectionEpoch, snapshot.EffectiveRevision), CodiUseCaseHost.Now);
        connections.MarkSnapshotApplied(new(id, "c1", snapshot.ConnectionEpoch, snapshot.EffectiveRevision), CodiUseCaseHost.Now);
        var update = new ActivityStateArguments(1, snapshot.ServerInstanceId, snapshot.ConnectionEpoch, snapshot.EffectiveRevision, null, 0, 1, "Idle", null, null, null, null);
        update = defect switch
        {
            "protocol" => update with { ProtocolVersion = 2 },
            "server" => update with { ServerInstanceId = Guid.NewGuid() },
            "runtime" => update with { RuntimeStatus = "Unknown" },
            "sequence" => update with { Sequence = -1 },
            "revision" => update with { EffectiveRevision = 0 },
            "generation" => update with { ExecutionGeneration = 1 },
            _ => update
        };
        var context = new CodiRequestContext(true, [CodiPolicy.ActiveParticipantConnection], participantId: id);
        var service = scenario.Host.Services.GetRequiredService<ParticipantObservationService>();
        await Assert.ThrowsAsync<CodiRequestException>(() => service.UpdateAsync(context, defect == "connection" ? "old" : "c1", update, CancellationToken.None));
        await Assert.ThrowsAsync<CodiRequestException>(() => service.UpdateAsync(CodiRequestContext.Anonymous, "c1", update, CancellationToken.None));
        Assert.Null(scenario.Host.Services.GetRequiredService<IParticipantObservationCache>().Get(id));
    }

    [Fact]
    public async Task CicloRegia_Ripetizioni_ConservaGliEsitiOriginali()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        var joined = await scenario.JoinAsync(seed, "Luca");
        long revision = 2;
        foreach (var name in new[] { "CloseAdmissions", "OpenAdmissions", "Pause", "Resume", "EndSession" })
        {
            var body = new { sessionId = seed.SessionId, expectedSessionRevision = revision };
            var response = await scenario.Host.CommandAsAsync<SessionDto>(name, body, Keyed(scenario, name));
            var replay = await scenario.Host.CommandAsAsync<SessionDto>(name, body, Keyed(scenario, name));
            Assert.Equal(response, replay); revision = response.SessionRevision;
        }
        Assert.Equal(5L, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM idempotency_record WHERE scope_key LIKE 'regia:%';"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Regia_ChiaveVuota_RifiutaPrimaDellaMutazione(string key)
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        var failure = await scenario.Host.ExpectFailureAsAsync("ShowWaiting",
            new { sessionId = seed.SessionId, expectedSessionRevision = 2L }, Keyed(scenario, key));
        Assert.Equal(CodiErrorCodes.InvalidPayload, failure.Code);
        Assert.Equal(2L, await scenario.ScalarAsync<long>("SELECT session_revision FROM lesson_session;"));
    }

    [Fact]
    public async Task RipresaIndividuale_SenzaPausa_RifiutaSenzaScritture()
    {
        await using var scenario = await OutboxScenario.CreateAsync(); var seed = await scenario.StartedSessionAsync();
        var joined = await scenario.JoinAsync(seed, "Luca");
        var failure = await scenario.Host.ExpectFailureAsAsync("Resume",
            new { sessionId = seed.SessionId, expectedSessionRevision = 2L, participantId = joined.Joined.ParticipantId }, Keyed(scenario, "resume"));
        Assert.Equal(CodiErrorCodes.SessionStateConflict, failure.Code);
        Assert.Equal(2L, await scenario.ScalarAsync<long>("SELECT session_revision FROM lesson_session;"));
        Assert.Equal(0L, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM outbox_message;"));
    }

    [Fact]
    public async Task Involucro_Classe_RisolveSoloIlTentativoDelDestinatario()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var clients = new FakeHubClients();
        var connections = scenario.Host.Services.GetRequiredService<IParticipantConnectionRegistry>();
        var server = scenario.Host.Services.GetRequiredService<IServerInstance>();
        var notifier = Notifier(scenario, clients);
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var session = Guid.NewGuid();
        connections.Acquire(new(first, session, Guid.NewGuid(), "first", 5, 2), CodiUseCaseHost.Now);
        var firstState = new DesiredStateDto("Exercise", false, Guid.NewGuid(), Guid.NewGuid());
        var secondState = new DesiredStateDto("Exercise", false, Guid.NewGuid(), Guid.NewGuid());
        var message = new CommandIssuedMessageDto(1, Guid.NewGuid(), session, 3, CodiUseCaseHost.Now, "AssignExercise", new("Exercise", false))
        { DesiredStates = new Dictionary<Guid, DesiredStateDto> { [first] = firstState, [second] = secondState } };
        await notifier.SendCommandIssuedAsync(first, message, CancellationToken.None);
        await notifier.SendCommandIssuedAsync(second, message, CancellationToken.None);
        var envelope = (CommandIssuedEnvelopeDto)clients.Sent[0].Arguments[0]!;
        Assert.Equal(firstState, envelope.DesiredState); Assert.Equal(5, envelope.ConnectionEpoch);
        Assert.Equal(server.InstanceId, envelope.ServerInstanceId);
        Assert.Equal($"group:{CodiHubGroups.Participant(first)}", clients.Sent[0].Target);
        var other = (CommandIssuedEnvelopeDto)clients.Sent[1].Arguments[0]!;
        Assert.Equal(secondState, other.DesiredState); Assert.Equal(0, other.ConnectionEpoch);
        Assert.NotEqual(envelope.DesiredState.AttemptId, other.DesiredState.AttemptId);
        Assert.Equal(3, connections.Get(first)!.RequiredRevision);
    }

    /// <summary>
    /// Chiusura della sessione per l'alunno (§11.3, §15.3): l'ultimo involucro, i permessi di
    /// salvataggio conclusivo e la loro scadenza. Prima di questo test l'evento era inviato con un
    /// oggetto anonimo e nessuno verificava che cosa arrivasse al client.
    /// </summary>
    [Fact]
    public async Task ChiusuraSessione_InviaUnMessaggioTipizzatoConLaScadenzaDelSalvataggio()
    {
        // Arrange: Luca non ha un tentativo, quindi non ha nulla da finalizzare: qui si verifica il
        // **contratto** della chiusura — nome dell'evento, forma del messaggio, scadenza — non il
        // contenuto dei permessi, che ha i propri test.
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        var luca = await scenario.JoinAsync(seed, "Luca");
        var clients = new FakeHubClients();
        var issuedAt = CodiUseCaseHost.Now;
        var message = new CommandIssuedMessageDto(1, Guid.NewGuid(), seed.SessionId, 3, issuedAt,
            EndSessionCommand.RequestName, new DesiredStateDto(nameof(ParticipantView.Goodbye), false));

        // Act
        await Notifier(scenario, clients).SendCommandIssuedAsync(luca.Joined.ParticipantId, message, CancellationToken.None);

        // Assert: la chiusura parte per prima, poi l'ultimo involucro, nello stesso gruppo.
        Assert.Equal(2, clients.Sent.Count);
        Assert.Equal(ParticipantRealtimeEvents.SessionEnded, clients.Sent[0].Method);
        Assert.Equal(CommandIssuedMessageDto.MessageType, clients.Sent[1].Method);
        Assert.All(clients.Sent, sent => Assert.Equal($"group:{CodiHubGroups.Participant(luca.Joined.ParticipantId)}", sent.Target));
        var closing = Assert.IsType<SessionEndedMessageDto>(clients.Sent[0].Arguments[0]);
        Assert.Equal(nameof(ParticipantView.Goodbye), closing.Command.DesiredState.View);
        Assert.Equal(issuedAt.Add(AttemptFinalization.Window), closing.ExpiresAtUtc);
        Assert.Empty(closing.Grants);

        // La forma sul filo è quella che il client legge: `command`, `grants`, `expiresAtUtc`.
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(closing, CodiJson.Payload));
        Assert.Equal(
            ["command", "expiresAtUtc", "grants"],
            wire.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
    }

    /// <summary>Il notificatore con la composizione reale, gli stessi doppi di trasporto usati altrove.</summary>
    private static HubCommandDeliveryNotifier Notifier(OutboxScenario scenario, FakeHubClients clients) =>
        new(new ParticipantHubContext(clients),
            scenario.Host.Services.GetRequiredService<IParticipantConnectionRegistry>(),
            scenario.Host.Services.GetRequiredService<IServerInstance>(),
            scenario.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            scenario.Host.Services.GetRequiredService<TimeProvider>());

    private sealed class ParticipantHubContext(FakeHubClients clients) : IHubContext<ParticipantHub>
    {
        public IHubClients Clients => clients;
        public IGroupManager Groups { get; } = new FakeGroupManager();
    }

    private static CodiRequestContext Keyed(OutboxScenario scenario, string key) =>
        new(true, [CodiPolicy.Admin, CodiPolicy.SessionController], adminUserId: scenario.AdminId, idempotencyKey: key);
    private static Task<TeacherSnapshotDto> Overview(OutboxScenario scenario, Seed seed) =>
        scenario.Host.QueryOneAsAsync<TeacherSnapshotDto>("GetSessionOverview", new { sessionId = seed.SessionId }, scenario.RegiaContext());
}
