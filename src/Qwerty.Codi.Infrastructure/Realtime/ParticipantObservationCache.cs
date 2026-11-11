using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Sessions;

namespace Qwerty.Codi.Infrastructure.Realtime;

internal sealed class ParticipantObservationCache(TimeProvider time) : IParticipantObservationCache
{
    private const int Capacity = 256;
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, ParticipantObservationDto> _rows = [];

    public bool Update(ParticipantObservationDto observation)
    {
        lock (_gate)
        {
            if (_rows.TryGetValue(observation.ParticipantId, out var prior) &&
                (observation.ConnectionEpoch < prior.ConnectionEpoch ||
                 observation.ConnectionEpoch == prior.ConnectionEpoch && observation.Sequence <= prior.Sequence)) return false;
            if (_rows.Count >= Capacity && !_rows.ContainsKey(observation.ParticipantId))
                _rows.Remove(_rows.MinBy(row => row.Value.ReceivedAtUtc).Key);
            _rows[observation.ParticipantId] = observation;
            return true;
        }
    }

    public ParticipantObservationDto? Get(Guid participantId)
    {
        lock (_gate)
        {
            if (!_rows.TryGetValue(participantId, out var row)) return null;
            if (time.GetUtcNow() - row.ReceivedAtUtc <= Lifetime) return row;
            _rows.Remove(participantId);
            return null;
        }
    }

    public void Remove(Guid participantId) { lock (_gate) _rows.Remove(participantId); }
}
