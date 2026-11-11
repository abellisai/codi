using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.Infrastructure.Realtime;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Realtime;

/// <summary>
/// Regole del registro delle connessioni (§11.6, §12.5, §12.6): chi può acquisire l'operatività, chi
/// blocca chi, quando si rinnova l'autorizzazione temporanea e quando il registro dimentica. Sono
/// regole di tempo, quindi il tempo è quello di prova e si sposta senza aspettare.
/// </summary>
public sealed class ParticipantConnectionRegistryTests
{
    private static readonly Guid ParticipantId = Guid.Parse("ce760e35-cd4d-4a38-932a-a37c3bb91978");
    private static readonly Guid SessionId = Guid.Parse("a970f347-5052-46ad-b8c5-2390ec5bd986");

    private readonly AdjustableTimeProvider _time = new(TestTimeProvider.Instant);
    private readonly IParticipantConnectionRegistry _registry;

    public ParticipantConnectionRegistryTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddCodiRealtimeInfrastructure();

        _registry = services.BuildServiceProvider().GetRequiredService<IParticipantConnectionRegistry>();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Acquisizione_SenzaAltreConnessioni_Elibera()
    {
        Assert.Equal(ConnectionTakeover.Free, _registry.CanAcquire(ParticipantId, Guid.NewGuid(), _time.GetUtcNow()));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Acquisizione_DellaStessaPagina_EUnaRiconnessione()
    {
        var pageInstance = Guid.NewGuid();
        Acquire(pageInstance, "connessione-1", epoch: 1);

        Assert.Equal(
            ConnectionTakeover.SameInstance,
            _registry.CanAcquire(ParticipantId, pageInstance, _time.GetUtcNow()));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Acquisizione_DiUnaPaginaDiversa_MentreLaPrimaEOperativa_Attende()
    {
        Acquire(Guid.NewGuid(), "connessione-1", epoch: 1);

        Assert.Equal(
            ConnectionTakeover.OtherInstanceActive,
            _registry.CanAcquire(ParticipantId, Guid.NewGuid(), _time.GetUtcNow()));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Acquisizione_DiUnaPaginaDiversa_DopoLaScadenza_Riesce()
    {
        Acquire(Guid.NewGuid(), "connessione-1", epoch: 1);
        _time.Advance(ParticipantProtocol.ExecutionLeaseDuration + TimeSpan.FromSeconds(1));

        // Una connessione che non dà più segni di vita non blocca nessuno: il refresh di un alunno
        // non può restare in attesa di una pagina chiusa da un quarto d'ora (§12.5).
        Assert.Equal(
            ConnectionTakeover.Free,
            _registry.CanAcquire(ParticipantId, Guid.NewGuid(), _time.GetUtcNow()));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Presenza_SenzaBattito_DiventaDegradataEPoiNonOperativa()
    {
        Acquire(Guid.NewGuid(), "connessione-1", epoch: 1);

        Assert.Equal(ParticipantPresence.Fresh, _registry.Get(ParticipantId)!.Presence);

        _time.Advance(ParticipantProtocol.FreshPresenceThreshold + TimeSpan.FromSeconds(1));
        Assert.Equal(ParticipantPresence.Degraded, _registry.Get(ParticipantId)!.Presence);

        _time.Advance(ParticipantProtocol.ExecutionLeaseDuration + TimeSpan.FromSeconds(1));
        Assert.Equal(ParticipantPresence.NotOperational, _registry.Get(ParticipantId)!.Presence);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Autorizzazione_ConStatoApplicato_EConcessaConIlNonceDelClient()
    {
        Acquire(Guid.NewGuid(), "connessione-1", epoch: 1, revision: 7);
        Assert.True(_registry.MarkSnapshotApplied(new SnapshotAppliedRequest(ParticipantId, "connessione-1", 1, 7), _time.GetUtcNow()));

        var first = _registry.Heartbeat(new HeartbeatRequest(ParticipantId, "connessione-1", 1, "nonce-1", 7, true), _time.GetUtcNow());
        var second = _registry.Heartbeat(new HeartbeatRequest(ParticipantId, "connessione-1", 1, "nonce-2", 7, true), _time.GetUtcNow());

        Assert.NotNull(first.Lease);
        Assert.NotNull(second.Lease);
        Assert.Equal("nonce-1", first.Lease.Nonce);
        Assert.Equal("nonce-2", second.Lease.Nonce);
        Assert.Equal(
            ParticipantProtocol.ExecutionLeaseDuration,
            second.Lease.ValidUntilUtc - _time.GetUtcNow());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Autorizzazione_ConStatoNonApplicato_NonEConcessa()
    {
        Acquire(Guid.NewGuid(), "connessione-1", epoch: 1, revision: 7);

        // Il caricamento non è concluso: la connessione è aperta ma il player non è operativo.
        var outcome = _registry.Heartbeat(new HeartbeatRequest(ParticipantId, "connessione-1", 1, "nonce-1", 7, true), _time.GetUtcNow());

        Assert.Equal(ParticipantPresence.Fresh, outcome.Presence);
        Assert.Null(outcome.Lease);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Autorizzazione_SenzaNonce_NonEConcessa()
    {
        Acquire(Guid.NewGuid(), "connessione-1", epoch: 1, revision: 7);
        _registry.MarkSnapshotApplied(new SnapshotAppliedRequest(ParticipantId, "connessione-1", 1, 7), _time.GetUtcNow());

        var outcome = _registry.Heartbeat(new HeartbeatRequest(ParticipantId, "connessione-1", 1, null, 7, true), _time.GetUtcNow());

        Assert.Null(outcome.Lease);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Autorizzazione_ConComandoNonApplicato_NonEConcessa()
    {
        Acquire(Guid.NewGuid(), "connessione-1", epoch: 1, revision: 7);
        _registry.MarkSnapshotApplied(new SnapshotAppliedRequest(ParticipantId, "connessione-1", 1, 7), _time.GetUtcNow());

        // Un comando è stato consegnato: il client deve applicarlo e confermarlo prima di riprendere.
        _registry.NoteDesiredRevision(ParticipantId, 8);

        var behind = _registry.Heartbeat(new HeartbeatRequest(ParticipantId, "connessione-1", 1, "nonce-1", 7, true), _time.GetUtcNow());
        Assert.Null(behind.Lease);

        // La conferma dell'applicazione riabilita il rinnovo.
        Assert.True(_registry.NoteAppliedRevision(ParticipantId, "connessione-1", 1, 8));
        var caughtUp = _registry.Heartbeat(new HeartbeatRequest(ParticipantId, "connessione-1", 1, "nonce-2", 8, true), _time.GetUtcNow());
        Assert.NotNull(caughtUp.Lease);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Autorizzazione_ConNonceRipetuto_NonEConcessa()
    {
        Acquire(Guid.NewGuid(), "connessione-1", epoch: 1, revision: 7);
        _registry.MarkSnapshotApplied(new SnapshotAppliedRequest(ParticipantId, "connessione-1", 1, 7), _time.GetUtcNow());
        var granted = _registry.Heartbeat(new HeartbeatRequest(ParticipantId, "connessione-1", 1, "nonce-1", 7, true), _time.GetUtcNow());
        Assert.NotNull(granted.Lease);

        // Ripetere il battito con lo stesso nonce è una richiesta ripetuta: non estende il permesso.
        var repeated = _registry.Heartbeat(
            new HeartbeatRequest(ParticipantId, "connessione-1", 1, "nonce-1", 7, true),
            _time.GetUtcNow());

        Assert.Null(repeated.Lease);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Battito_ConEpocaSuperata_NonRinnovaENonAggiornaLaPresenza()
    {
        Acquire(Guid.NewGuid(), "connessione-1", epoch: 1, revision: 7);

        var outcome = _registry.Heartbeat(new HeartbeatRequest(ParticipantId, "connessione-1", 99, null, 7, true), _time.GetUtcNow());

        Assert.Equal(ParticipantPresence.Disconnected, outcome.Presence);
        Assert.Null(outcome.Lease);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Rilascio_DellaConnessioneSuperata_NonRimuoveQuellaCorrente()
    {
        Acquire(Guid.NewGuid(), "connessione-1", epoch: 1, revision: 7);
        _registry.Acquire(
            new ParticipantConnectionRequest(ParticipantId, SessionId, Guid.NewGuid(), "connessione-2", 2, 7),
            _time.GetUtcNow());

        _registry.Release(ParticipantId, "connessione-1", 1);

        var current = _registry.Get(ParticipantId);
        Assert.NotNull(current);
        Assert.Equal("connessione-2", current.ConnectionId);
    }

    private void Acquire(Guid pageInstance, string connectionId, long epoch, long revision = 1) =>
        _registry.Acquire(
            new ParticipantConnectionRequest(ParticipantId, SessionId, pageInstance, connectionId, epoch, revision),
            _time.GetUtcNow());
}
