using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Sessions;

namespace Qwerty.Codi.Infrastructure.Realtime;

/// <summary>
/// Cache limitata dell'anteprima (§15.5): una per partecipante, con scadenza. Oltre la scadenza il
/// docente vede l'anteprima come assente finché il client non ne manda una nuova: un'anteprima
/// vecchia mostrata come fresca sarebbe una bugia, non un'ottimizzazione.
/// </summary>
internal sealed class ParticipantPreviewCache(TimeProvider time) : IParticipantPreviewCache
{
    private const int Capacity = 128;

    /// <summary>Durata locale della freschezza: il client rimanda l'anteprima a ogni modifica.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, ParticipantPreviewDto> _rows = [];

    public bool Update(ParticipantPreviewDto preview)
    {
        lock (_gate)
        {
            if (_rows.TryGetValue(preview.ParticipantId, out var prior) &&
                (preview.ConnectionEpoch < prior.ConnectionEpoch ||
                 preview.ConnectionEpoch == prior.ConnectionEpoch && preview.ProjectRevision <= prior.ProjectRevision))
                return false;
            if (_rows.Count >= Capacity && !_rows.ContainsKey(preview.ParticipantId))
                _rows.Remove(_rows.MinBy(row => row.Value.ReceivedAtUtc).Key);
            _rows[preview.ParticipantId] = preview;
            return true;
        }
    }

    public ParticipantPreviewDto? Get(Guid participantId)
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
