using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.Infrastructure.Realtime;
using Xunit;

namespace Qwerty.Codi.ProtocolTests;

[Trait("Category", "Unit")]
public sealed class RegiaProtocolTests : IDisposable
{
    private readonly ManualTime _time = new();
    private readonly ServiceProvider _services;
    private readonly IParticipantConnectionRegistry _connections;
    private readonly Guid _participant = Guid.NewGuid();
    private readonly Guid _session = Guid.NewGuid();

    public RegiaProtocolTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddCodiRealtimeInfrastructure();
        _services = services.BuildServiceProvider();
        _connections = _services.GetRequiredService<IParticipantConnectionRegistry>();
        Acquire("first", 1, 9007199254740993);
    }

    [Theory]
    [InlineData(9007199254740992, true)]
    [InlineData(9007199254740994, true)]
    [InlineData(9007199254740993, false)]
    public void Battito_ConRevisioneErrataOPaginaNascosta_NonRinnova(long revision, bool visible)
    {
        Assert.Null(_connections.Heartbeat(new(_participant, "first", 1, "nonce", revision, visible), _time.GetUtcNow()).Lease);
        Assert.Equal(9007199254740993, _connections.Get(_participant)!.AppliedRevision);
    }

    [Fact]
    public void Battito_ConNonceUsatoPrimaDiOttoBattiti_NonRinnova()
    {
        for (var index = 0; index < 20; index++)
            Assert.NotNull(_connections.Heartbeat(new(_participant, "first", 1, $"nonce-{index}", 9007199254740993, true), _time.GetUtcNow()).Lease);
        Assert.Null(_connections.Heartbeat(new(_participant, "first", 1, "nonce-0", 9007199254740993, true), _time.GetUtcNow()).Lease);
    }

    [Fact]
    public void Riconnessione_DisconnessioneVecchia_NonRimuoveLaNuovaEpoca()
    {
        Acquire("new", 2, 9007199254740993);
        _connections.Release(_participant, "first", 1);
        Assert.Equal("new", _connections.Get(_participant)!.ConnectionId);
        Assert.Null(_connections.Heartbeat(new(_participant, "first", 1, "old", 9007199254740993, true), _time.GetUtcNow()).Lease);
        Assert.NotNull(_connections.Heartbeat(new(_participant, "new", 2, "new", 9007199254740993, true), _time.GetUtcNow()).Lease);
    }

    [Fact]
    public void Pausa_ConfermaTardivaDelComandoPrecedente_NonRiabilita()
    {
        _connections.NoteDesiredRevision(_participant, 9007199254740995);
        _connections.NoteAppliedRevision(_participant, "first", 1, 9007199254740994);
        Assert.Null(_connections.Heartbeat(new(_participant, "first", 1, "n", 9007199254740994, true), _time.GetUtcNow()).Lease);
        _connections.NoteAppliedRevision(_participant, "first", 1, 9007199254740995);
        Assert.NotNull(_connections.Heartbeat(new(_participant, "first", 1, "n2", 9007199254740995, true), _time.GetUtcNow()).Lease);
    }

    [Fact]
    public void DodiciConnessioni_PausaIndividuale_ConservaLeAltreAutorizzazioni()
    {
        var participants = Enumerable.Range(0, 12).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in participants)
        {
            _connections.Acquire(new(id, _session, Guid.NewGuid(), id.ToString(), 1, 2), _time.GetUtcNow());
            _connections.MarkSnapshotApplied(new(id, id.ToString(), 1, 2), _time.GetUtcNow());
        }
        _connections.NoteDesiredRevision(participants[0], 3);
        var leases = participants.Select(id => _connections.Heartbeat(new(id, id.ToString(), 1, "n", 2, true), _time.GetUtcNow()).Lease).ToArray();
        Assert.Null(leases[0]); Assert.Equal(11, leases.Count(lease => lease is not null));
        _time.Advance(TimeSpan.FromSeconds(11));
        Assert.All(participants, id => Assert.Equal(ParticipantPresence.NotOperational, _connections.Get(id)!.Presence));
    }

    [Fact]
    public void Telemetria_ScadutaORevocata_NonCompareComeStatoCorrente()
    {
        var cache = _services.GetRequiredService<IParticipantObservationCache>();
        var state = new ParticipantObservationDto(_session, _participant, 1, 2, null, 0, 5, "Ready", null, null, null, null, _time.GetUtcNow());
        Assert.True(cache.Update(state));
        Assert.False(cache.Update(state with { Sequence = 4 }));
        Assert.False(cache.Update(state with { ConnectionEpoch = 0, Sequence = 6 }));
        Assert.True(cache.Update(state with { ConnectionEpoch = 2, Sequence = 1 }));
        _time.Advance(TimeSpan.FromSeconds(31));
        Assert.Null(cache.Get(_participant));
        Assert.True(cache.Update(state with { ReceivedAtUtc = _time.GetUtcNow() }));
        cache.Remove(_participant); Assert.Null(cache.Get(_participant));
    }

    private void Acquire(string connection, long epoch, long revision)
    {
        _connections.Acquire(new(_participant, _session, Guid.NewGuid(), connection, epoch, revision), _time.GetUtcNow());
        Assert.True(_connections.MarkSnapshotApplied(new(_participant, connection, epoch, revision), _time.GetUtcNow()));
    }

    public void Dispose() => _services.Dispose();
    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
