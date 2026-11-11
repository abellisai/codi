using System.Security.Claims;

namespace Qwerty.Codi.Api.Hubs;

internal sealed record TeacherObservation(string ConnectionId, Guid SessionId, ClaimsPrincipal User);

internal sealed class TeacherObservationRegistry
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, TeacherObservation> _rows = [];
    public void Observe(TeacherObservation observation)
    {
        lock (_gate)
        {
            if (_rows.Count >= 64 && !_rows.ContainsKey(observation.ConnectionId))
                throw new Microsoft.AspNetCore.SignalR.HubException("Numero massimo di osservatori raggiunto.");
            _rows[observation.ConnectionId] = observation;
        }
    }
    public void Remove(string connectionId) { lock (_gate) _rows.Remove(connectionId); }
    public IReadOnlyList<TeacherObservation> All() { lock (_gate) return _rows.Values.ToArray(); }
}
