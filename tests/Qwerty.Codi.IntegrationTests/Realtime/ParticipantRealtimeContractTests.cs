using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Api.Hubs;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Sessions;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Realtime;

/// <summary>
/// Rifiuti del protocollo (§11.2, §11.5, §12.2): ciò che il server non accetta deve essere rifiutato
/// **prima** di lasciare tracce. Un client non allineato, una lezione finita e un destinatario che non
/// ha mai ricevuto il comando sono i tre casi in cui un handshake o una conferma sembrano legittimi e
/// non lo sono.
/// </summary>
public sealed class ParticipantRealtimeContractTests : IAsyncLifetime
{
    private const string ConnectionId = "connessione-1";

    private OutboxScenario _scenario = null!;

    public async Task InitializeAsync() => _scenario = await OutboxScenario.CreateAsync();

    public async Task DisposeAsync() => await _scenario.DisposeAsync();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Handshake_ConVersioneDichiarataVuota_RifiutaSenzaConsumareLEpoca()
    {
        var participant = await ParticipantAsync();
        var hub = Hub(participant);

        var rejection = await Assert.ThrowsAsync<CodiRequestException>(
            () => hub.InitializeClient(new InitializeClientArguments(1, Guid.NewGuid(), "   ", "gridpath-1")));

        Assert.Equal(CodiErrorCodes.ValidationFailed, rejection.Code);
        Assert.Equal(0L, await EpochAsync(participant.ParticipantId));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Handshake_SuLezioneTerminata_RifiutaSenzaConsumareLEpoca()
    {
        // §12.7: la sessione è terminale, quindi non si acquisisce un'operatività che non esiste più.
        var participant = await ParticipantAsync();
        await _scenario.Host.CommandAsAsync<SessionDto>(
            EndSessionCommand.RequestName,
            new { sessionId = participant.SessionId, expectedSessionRevision = 2L },
            _scenario.RegiaContext());

        var rejection = await Assert.ThrowsAsync<CodiRequestException>(
            () => Hub(participant).InitializeClient(new InitializeClientArguments(1, Guid.NewGuid(), "0.1.0", "gridpath-1")));

        Assert.Equal(CodiErrorCodes.SessionStateConflict, rejection.Code);
        Assert.Equal(0L, await EpochAsync(participant.ParticipantId));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StatoCorrente_SenzaIdentita_Rifiuta()
    {
        var rejection = await Assert.ThrowsAsync<CodiRequestException>(
            () => _scenario.Host.QueryOneAsAsync<ParticipantSnapshotDto>(
                GetParticipantSnapshotQuery.RequestName,
                new { },
                CodiRequestContext.Anonymous));

        Assert.Equal(CodiErrorCodes.AuthenticationRequired, rejection.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Conferma_DiUnPartecipanteEntratoDopoIlComando_Rifiuta()
    {
        // Chi entra dopo il comando non ha una consegna: il comando di classe non lo riguarda, e una
        // conferma senza consegna non è una conferma.
        var seed = await _scenario.StartedSessionAsync();
        var accepted = await _scenario.ShowWaitingAsync(seed, expectedSessionRevision: 2L);
        var latecomer = await _scenario.JoinAsync(seed, "Anna", "chiave-di-anna");

        var context = new CodiRequestContext(
            isAuthenticated: true,
            [CodiPolicy.AdmittedParticipant, CodiPolicy.ActiveParticipantConnection],
            latecomer.Joined.CredentialSessionId,
            adminUserId: null,
            latecomer.Joined.ParticipantId);

        var rejection = await Assert.ThrowsAsync<CodiRequestException>(
            () => _scenario.Host.CommandAsAsync<CommandAcknowledgementDto>(
                AcknowledgeCommandCommand.RequestName,
                new
                {
                    commandId = accepted.CommandId,
                    protocolVersion = ParticipantProtocol.CurrentVersion,
                    effectiveRevision = accepted.SessionRevision,
                    connectionEpoch = 0L,
                    outcome = CommandAcknowledgementOutcomes.Applied,
                },
                context));

        Assert.Equal(CodiErrorCodes.ResourceNotFound, rejection.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Handshake_ConVersioneDichiarataTroppoLunga_Rifiuta()
    {
        var participant = await ParticipantAsync();

        var rejection = await Assert.ThrowsAsync<CodiRequestException>(
            () => Hub(participant).InitializeClient(
                new InitializeClientArguments(1, Guid.NewGuid(), "0.1.0", new string('x', 200))));

        Assert.Equal(CodiErrorCodes.ValidationFailed, rejection.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Handshake_DiUnaPartecipazioneInesistente_Rifiuta()
    {
        // Una credenziale che supera il trasporto ma non ha più la riga della partecipazione non
        // acquisisce nulla: è una risorsa assente, non un permesso negato.
        var context = CodiUseCaseHost.ParticipantContext(Guid.NewGuid(), Guid.NewGuid());

        var rejection = await Assert.ThrowsAsync<CodiRequestException>(
            () => _scenario.Host.CommandAsAsync<ParticipantSnapshotDto>(
                InitializeParticipantConnectionCommand.RequestName,
                new
                {
                    protocolVersion = ParticipantProtocol.CurrentVersion,
                    clientInstanceId = Guid.NewGuid(),
                    appVersion = "0.1.0",
                    engineVersion = "gridpath-1",
                },
                context));

        Assert.Equal(CodiErrorCodes.ResourceNotFound, rejection.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StatoCorrente_DiUnaPartecipazioneInesistente_Rifiuta()
    {
        var context = CodiUseCaseHost.ParticipantContext(Guid.NewGuid(), Guid.NewGuid());

        var rejection = await Assert.ThrowsAsync<CodiRequestException>(
            () => _scenario.Host.QueryOneAsAsync<ParticipantSnapshotDto>(
                GetParticipantSnapshotQuery.RequestName,
                new { },
                context));

        Assert.Equal(CodiErrorCodes.ResourceNotFound, rejection.Code);
    }

    private static ParticipantHub Hub(Participant participant) =>
        participant.Hub;

    private async Task<Participant> ParticipantAsync()
    {
        var seed = await _scenario.StartedSessionAsync();
        var joined = await _scenario.JoinAsync(seed, "Luca");
        var context = CodiUseCaseHost.ParticipantContext(joined.Joined.CredentialSessionId, joined.Joined.ParticipantId);

        var hub = new ParticipantHub(
            new StubContextProvider(context),
            _scenario.Host.Services.GetRequiredService<CodiRequestExecutor>(),
            _scenario.Host.Services.GetRequiredService<IParticipantConnectionRegistry>(),
            _scenario.Host.Services.GetRequiredService<IServerInstance>(),
            _scenario.Host.Services.GetRequiredService<TimeProvider>())
        {
            Context = new FakeHubCallerContext(new Microsoft.AspNetCore.Http.DefaultHttpContext(), ConnectionId),
            Groups = new FakeGroupManager(),
            Clients = new FakeHubClients(),
        };

        return new Participant(hub, seed.SessionId, joined.Joined.ParticipantId);
    }

    private async Task<long> EpochAsync(Guid participantId) =>
        await _scenario.ScalarAsync<long>(
            $"SELECT connection_epoch FROM session_participant WHERE id = '{participantId:D}';");

    /// <summary>Hub pronto all'uso e dati della partecipazione a cui appartiene.</summary>
    private sealed record Participant(ParticipantHub Hub, Guid SessionId, Guid ParticipantId);
}
