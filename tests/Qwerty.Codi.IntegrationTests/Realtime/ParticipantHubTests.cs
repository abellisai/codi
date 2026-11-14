using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Api.Hubs;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Sessions;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Realtime;

/// <summary>
/// Protocollo dell'hub del partecipante (§11.1, §11.2, §12.5). Il collegamento è il trasporto e non
/// rende operativo l'alunno: solo l'handshake incrementa l'epoca e rende rinnovabile
/// l'autorizzazione temporanea. Il trasporto è un doppio — nessun SignalR reale — perché ciò che si
/// verifica è che cosa il server decide di inviare e a chi, non come arriva al browser.
/// </summary>
public sealed class ParticipantHubTests : IAsyncLifetime
{
    private const string ConnectionId = "connessione-1";
    private const string OtherConnectionId = "connessione-2";

    private OutboxScenario _scenario = null!;

    public async Task InitializeAsync() => _scenario = await OutboxScenario.CreateAsync();

    public async Task DisposeAsync() => await _scenario.DisposeAsync();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Apertura_ConPartecipazioneAmmessa_AssociaIlGruppoDelPartecipante()
    {
        // Arrange
        var participantId = Guid.NewGuid();
        var groups = new FakeGroupManager();
        var caller = Caller(ConnectionId);
        var hub = Hub(
            new CodiRequestContext(
                isAuthenticated: true,
                [CodiPolicy.AdmittedParticipant],
                credentialSessionId: Guid.NewGuid(),
                participantId: participantId),
            caller,
            groups,
            new FakeHubClients());

        // Act
        await hub.OnConnectedAsync();

        // Assert: il gruppo è deciso dal server, non dichiarato dal client.
        Assert.Equal((ConnectionId, CodiHubGroups.Participant(participantId)), Assert.Single(groups.Added));
        Assert.False(caller.Aborted);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Apertura_SenzaPartecipazioneAmmessa_ChiudeLaConnessione()
    {
        // Arrange: un contesto anonimo, come un browser senza credenziale valida o con la
        // credenziale revocata dopo l'handshake del trasporto (§9.2).
        var groups = new FakeGroupManager();
        var clients = new FakeHubClients();
        var caller = Caller(ConnectionId);
        var hub = Hub(CodiRequestContext.Anonymous, caller, groups, clients);

        // Act
        await hub.OnConnectedAsync();

        // Assert: la connessione si chiude e il client riceve il motivo, senza doverlo dedurre da un
        // errore di trasporto.
        Assert.Empty(groups.Added);
        Assert.True(caller.Aborted);
        Assert.Equal([ParticipantRealtimeEvents.ParticipationRevoked], clients.MethodsTo("caller"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Apertura_SenzaIdentificativoDelPartecipante_ChiudeLaConnessione()
    {
        // Arrange: una concessione di partecipante senza identificativo non basta ad associare un
        // gruppo: manca ciò che identifica il destinatario.
        var groups = new FakeGroupManager();
        var caller = Caller(ConnectionId);
        var hub = Hub(
            new CodiRequestContext(isAuthenticated: true, [CodiPolicy.AdmittedParticipant]),
            caller,
            groups,
            new FakeHubClients());

        // Act
        await hub.OnConnectedAsync();

        // Assert
        Assert.Empty(groups.Added);
        Assert.True(caller.Aborted);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Handshake_ConPartecipanteAmmesso_IncrementaLEpocaEInviaLoSnapshot()
    {
        // Arrange
        var participant = await ParticipantAsync();
        var clients = new FakeHubClients();
        var hub = Hub(participant.Context, Caller(ConnectionId), new FakeGroupManager(), clients);

        // Act
        await hub.InitializeClient(new InitializeClientArguments(1, Guid.NewGuid(), "0.1.0", "gridpath-1"));

        // Assert: l'epoca è persistita e lo snapshot porta lo stato desiderato del partecipante.
        Assert.Equal(1L, await EpochAsync(participant.ParticipantId));

        var snapshot = Snapshot(clients);
        Assert.Equal(participant.ParticipantId, snapshot.ParticipantId);
        Assert.Equal(participant.Seed.SessionId, snapshot.SessionId);
        Assert.Equal(1L, snapshot.ConnectionEpoch);
        Assert.Equal(nameof(Domain.Sessions.ParticipantView.Waiting), snapshot.DesiredState.View);
        Assert.False(snapshot.DesiredState.InteractionEnabled);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Handshake_ConSecondaPagina_MentreLaPrimaEOperativa_RifiutaSenzaToccareLEpoca()
    {
        // Arrange: la prima pagina ha acquisito l'operatività.
        var participant = await ParticipantAsync();
        var first = Hub(participant.Context, Caller(ConnectionId), new FakeGroupManager(), new FakeHubClients());
        await first.InitializeClient(new InitializeClientArguments(1, Guid.NewGuid(), "0.1.0", "gridpath-1"));

        var clients = new FakeHubClients();
        var second = Hub(participant.Context, Caller(OtherConnectionId), new FakeGroupManager(), clients);

        // Act
        await second.InitializeClient(new InitializeClientArguments(1, Guid.NewGuid(), "0.1.0", "gridpath-1"));

        // Assert: la seconda pagina attende, l'epoca resta quella della prima e nessuno snapshot parte.
        Assert.Equal([ParticipantRealtimeEvents.ActiveConnectionExists], clients.MethodsTo("caller"));
        Assert.Equal(1L, await EpochAsync(participant.ParticipantId));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Handshake_DellaStessaPagina_RiacquisisceEAvvisaLaVecchiaConnessione()
    {
        // Arrange: la stessa istanza di pagina si riconnette — un refresh, non una seconda scheda.
        var participant = await ParticipantAsync();
        var pageInstance = Guid.NewGuid();
        var first = Hub(participant.Context, Caller(ConnectionId), new FakeGroupManager(), new FakeHubClients());
        await first.InitializeClient(new InitializeClientArguments(1, pageInstance, "0.1.0", "gridpath-1"));

        var clients = new FakeHubClients();
        var second = Hub(participant.Context, Caller(OtherConnectionId), new FakeGroupManager(), clients);

        // Act
        await second.InitializeClient(new InitializeClientArguments(1, pageInstance, "0.1.0", "gridpath-1"));

        // Assert: l'epoca avanza, la vecchia connessione è avvisata e la nuova riceve lo snapshot.
        Assert.Equal(2L, await EpochAsync(participant.ParticipantId));
        Assert.Equal(
            [ParticipantRealtimeEvents.ConnectionSuperseded],
            clients.MethodsTo($"connection:{ConnectionId}"));
        Assert.Equal([ParticipantRealtimeEvents.SessionSnapshot], clients.MethodsTo("caller"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Handshake_ConProtocolloDiverso_RifiutaSenzaIncrementareLEpoca()
    {
        // Arrange: §11.5 — un client con un protocollo che il server non conosce non deve consumare
        // un'epoca: non saprebbe cosa farsene.
        var participant = await ParticipantAsync();
        var clients = new FakeHubClients();
        var hub = Hub(participant.Context, Caller(ConnectionId), new FakeGroupManager(), clients);

        // Act
        var rejection = await Assert.ThrowsAsync<CodiRequestException>(
            () => hub.InitializeClient(new InitializeClientArguments(99, Guid.NewGuid(), "0.1.0", "gridpath-1")));

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, rejection.Code);
        Assert.Equal(0L, await EpochAsync(participant.ParticipantId));
        Assert.Empty(clients.Sent);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SnapshotApplied_ConRevisioneSuperata_RimandaUnoSnapshot()
    {
        // Arrange: dopo l'handshake la sessione avanza, quindi il client ha caricato uno stato
        // superato e il server non può ritenerlo sincronizzato (§11.2 passo 7).
        var participant = await ParticipantAsync();
        var clients = new FakeHubClients();
        var hub = Hub(participant.Context, Caller(ConnectionId), new FakeGroupManager(), clients);
        await hub.InitializeClient(new InitializeClientArguments(1, Guid.NewGuid(), "0.1.0", "gridpath-1"));
        var first = Snapshot(clients);
        var accepted = await _scenario.ShowWaitingAsync(participant.Seed, first.EffectiveRevision);

        // Act
        await hub.SnapshotApplied(new SnapshotAppliedArguments(first.EffectiveRevision, first.ConnectionEpoch));

        // Assert: l'ultimo snapshot inviato porta la revisione nuova, non quella caricata.
        var resent = Snapshot(clients);
        Assert.Equal(accepted.SessionRevision, resent.EffectiveRevision);
        Assert.True(resent.EffectiveRevision > first.EffectiveRevision);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Heartbeat_ConStatoApplicato_RinnovaLAutorizzazioneTemporanea()
    {
        // Arrange: handshake completato e snapshot applicato alla revisione richiesta (§12.6).
        var participant = await ParticipantAsync();
        var clients = new FakeHubClients();
        var hub = Hub(participant.Context, Caller(ConnectionId), new FakeGroupManager(), clients);
        await hub.InitializeClient(new InitializeClientArguments(1, Guid.NewGuid(), "0.1.0", "gridpath-1"));
        var snapshot = Snapshot(clients);
        await hub.SnapshotApplied(new SnapshotAppliedArguments(snapshot.EffectiveRevision, snapshot.ConnectionEpoch));
        Assert.NotNull(snapshot.Profile);
        Assert.Equal("Luca", snapshot.Profile.DisplayName);
        var corrected = await _scenario.Host.CommandAsAsync<SessionParticipantDto>(UpdateParticipantCommand.RequestName,
            new
            {
                sessionId = participant.Seed.SessionId,
                participantId = participant.ParticipantId,
                displayName = "Luca Rossi",
                baseRowVersion = snapshot.Profile.RowVersion
            }, _scenario.RegiaContext());

        // Act
        await hub.Heartbeat(
            new HeartbeatArguments(snapshot.EffectiveRevision, snapshot.ConnectionEpoch, "nonce-1", PageVisible: true));

        // Assert
        var sent = Assert.Single(clients.Sent, item => item.Method == ParticipantRealtimeEvents.LeaseGranted);
        var lease = Assert.IsType<ExecutionLeaseDto>(sent.Arguments[0]);
        Assert.Equal(snapshot.ConnectionEpoch, lease.ConnectionEpoch);
        Assert.Equal((long)ParticipantProtocol.ExecutionLeaseDuration.TotalSeconds, lease.MaxDurationSeconds);
        Assert.Equal(corrected.DisplayName, lease.Profile?.DisplayName);
        Assert.Equal(corrected.RowVersion, lease.Profile?.RowVersion);
        Assert.Equal(participant.ParticipantId, lease.Profile?.ParticipantId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Heartbeat_ConEpocaSuperata_AvvisaSenzaRinnovare()
    {
        // Arrange: il client dichiara un'epoca che non è più quella corrente.
        var participant = await ParticipantAsync();
        var clients = new FakeHubClients();
        var hub = Hub(participant.Context, Caller(ConnectionId), new FakeGroupManager(), clients);

        // Act
        await hub.Heartbeat(new HeartbeatArguments(1, 99, "nonce-1", PageVisible: true));

        // Assert
        Assert.Equal([ParticipantRealtimeEvents.ConnectionSuperseded], clients.MethodsTo("caller"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Disconnessione_DellaConnessioneSuperata_NonScollegaQuellaNuova()
    {
        // Arrange: la vecchia connessione si chiude **dopo** che la nuova ha acquisito l'operatività.
        var participant = await ParticipantAsync();
        var pageInstance = Guid.NewGuid();
        var first = Hub(participant.Context, Caller(ConnectionId), new FakeGroupManager(), new FakeHubClients());
        await first.InitializeClient(new InitializeClientArguments(1, pageInstance, "0.1.0", "gridpath-1"));

        var second = Hub(participant.Context, Caller(OtherConnectionId), new FakeGroupManager(), new FakeHubClients());
        await second.InitializeClient(new InitializeClientArguments(1, pageInstance, "0.1.0", "gridpath-1"));

        // Act: la disconnessione arriva dalla connessione vecchia, con la sua epoca.
        await first.OnDisconnectedAsync(null);

        // Assert: il registro conserva la nuova connessione e la sua epoca.
        var registry = _scenario.Host.Services.GetRequiredService<IParticipantConnectionRegistry>();
        var state = registry.Get(participant.ParticipantId);
        Assert.NotNull(state);
        Assert.Equal(OtherConnectionId, state.ConnectionId);
        Assert.Equal(2L, state.Epoch);
    }

    private ParticipantHub Hub(
        CodiRequestContext context,
        FakeHubCallerContext caller,
        FakeGroupManager groups,
        FakeHubClients clients) =>
        new(
            new StubContextProvider(context),
            _scenario.Host.Services.GetRequiredService<CodiRequestExecutor>(),
            _scenario.Host.Services.GetRequiredService<IParticipantConnectionRegistry>(),
            _scenario.Host.Services.GetRequiredService<IServerInstance>(),
            _scenario.Host.Services.GetRequiredService<TimeProvider>())
        {
            Context = caller,
            Groups = groups,
            Clients = clients,
        };

    private static FakeHubCallerContext Caller(string connectionId) =>
        new(new DefaultHttpContext(), connectionId);

    /// <summary>Ultimo snapshot inviato al chiamante, nell'ordine di invio.</summary>
    private static ParticipantSnapshotDto Snapshot(FakeHubClients clients) =>
        (ParticipantSnapshotDto)clients
            .Sent.Last(item => item.Method == ParticipantRealtimeEvents.SessionSnapshot).Arguments[0]!;

    private async Task<Participant> ParticipantAsync()
    {
        var seed = await _scenario.StartedSessionAsync();
        var joined = await _scenario.JoinAsync(seed, "Luca");

        return new Participant(
            CodiUseCaseHost.ParticipantContext(joined.Joined.CredentialSessionId, joined.Joined.ParticipantId),
            seed,
            joined.Joined.ParticipantId);
    }

    private async Task<long> EpochAsync(Guid participantId) =>
        await _scenario.ScalarAsync<long>(
            $"SELECT connection_epoch FROM session_participant WHERE id = '{participantId:D}';");

    /// <summary>Contesto del partecipante e catena della sessione a cui appartiene.</summary>
    private sealed record Participant(CodiRequestContext Context, Seed Seed, Guid ParticipantId);
}
