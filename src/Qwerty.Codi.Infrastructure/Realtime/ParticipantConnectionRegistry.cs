using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Sessions;

namespace Qwerty.Codi.Infrastructure.Realtime;

/// <summary>
/// Registro delle connessioni dei partecipanti (§11.4, §12.5, §12.6), tenuto in memoria perché
/// presenza, epoca effettiva e autorizzazione temporanea sono stato del processo: un riavvio le
/// invalida tutte, ed è voluto (§11.5).
///
/// Il registro è unico per processo e le operazioni sono brevi: si protegge con un lucchetto invece
/// di strutture senza blocco perché il numero di client è quello di un'aula — dodici — e una regola
/// scritta in modo semplice si verifica più facilmente di una ottimizzata. Il tempo usato per
/// giudicare la presenza è sempre quello **ricevuto dal server** (§11.6): l'orologio del PC
/// dell'alunno non decide se il suo client è operativo.
/// </summary>
internal sealed class ParticipantConnectionRegistry(TimeProvider timeProvider) : IParticipantConnectionRegistry
{
    /// <summary>Numero massimo di nonce distinti per epoch. Il limite impedisce una crescita senza termine.</summary>
    private const int ConsumedNonceCapacity = 16384;

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, ParticipantConnection> _connections = [];

    public ConnectionTakeover CanAcquire(Guid participantId, Guid clientInstanceId, DateTimeOffset nowUtc) =>
        Locked(() =>
        {
            if (!_connections.TryGetValue(participantId, out var current))
            {
                return ConnectionTakeover.Free;
            }

            if (current.ClientInstanceId == clientInstanceId)
            {
                // La stessa pagina si sta riconnettendo: il refresh non crea una seconda
                // partecipazione e riacquisisce incrementando l'epoca (§12.5).
                return ConnectionTakeover.SameInstance;
            }

            // Un'altra pagina è ancora operativa finché non ha superato la validità
            // dell'autorizzazione: da lì in poi non blocca più nessuno.
            return IsOperative(current, nowUtc)
                ? ConnectionTakeover.OtherInstanceActive
                : ConnectionTakeover.Free;
        });

    public string? Acquire(ParticipantConnectionRequest request, DateTimeOffset nowUtc) =>
        Locked(() =>
        {
            ArgumentNullException.ThrowIfNull(request);

            string? supersededConnectionId = null;

            if (_connections.TryGetValue(request.ParticipantId, out var previous) &&
                !string.Equals(previous.ConnectionId, request.ConnectionId, StringComparison.Ordinal))
            {
                supersededConnectionId = previous.ConnectionId;
            }

            _connections[request.ParticipantId] = new ParticipantConnection
            {
                ConnectionId = request.ConnectionId,
                ClientInstanceId = request.ClientInstanceId,
                SessionId = request.SessionId,
                Epoch = request.Epoch,
                RequiredRevision = request.EffectiveRevision,
                AcquiredAtUtc = nowUtc,
            };

            return supersededConnectionId;
        });

    public bool MarkSnapshotApplied(SnapshotAppliedRequest request, DateTimeOffset nowUtc) =>
        Locked(() =>
        {
            ArgumentNullException.ThrowIfNull(request);

            if (!TryGetCurrent(request.ParticipantId, request.ConnectionId, request.Epoch, out var current))
            {
                return false;
            }

            current.SnapshotApplied = true;
            current.AppliedRevision = request.AppliedRevision;

            return true;
        });

    public HeartbeatOutcome Heartbeat(HeartbeatRequest request, DateTimeOffset nowUtc) =>
        Locked(() =>
        {
            ArgumentNullException.ThrowIfNull(request);

            if (!TryGetCurrent(request.ParticipantId, request.ConnectionId, request.Epoch, out var current))
            {
                // Una pagina superata non riceve autorizzazioni: il server la considera fuori dal
                // gioco anche se il trasporto è ancora aperto.
                return new HeartbeatOutcome(ParticipantPresence.Disconnected, Lease: null);
            }

            current.LastHeartbeatUtc = nowUtc;
            current.PageVisible = request.PageVisible;
            var presence = PresenceOf(current, nowUtc);

            // Il nonce è monouso (§12.6): un battito che ripete un nonce già consumato è una
            // richiesta ripetuta, e una risposta arrivata in ritardo non deve estendere il permesso.
            if (string.IsNullOrEmpty(request.Nonce) || !current.ConsumeNonce(request.Nonce))
            {
                return new HeartbeatOutcome(presence, Lease: null);
            }

            var lease = request.AppliedRevision == current.AppliedRevision && request.PageVisible &&
                CanRenew(current, presence) ? GrantLease(current, request.Nonce, nowUtc) : null;

            return new HeartbeatOutcome(presence, lease);
        });

    public void NoteDesiredRevision(Guid participantId, long effectiveRevision) =>
        Locked(() =>
        {
            if (_connections.TryGetValue(participantId, out var current) &&
                effectiveRevision > current.RequiredRevision)
            {
                current.RequiredRevision = effectiveRevision;
            }
        });

    public bool NoteAppliedRevision(
        Guid participantId,
        string connectionId,
        long epoch,
        long appliedRevision) =>
        Locked(() =>
        {
            if (!TryGetCurrent(participantId, connectionId, epoch, out var current))
            {
                return false;
            }

            current.AppliedRevision = Math.Max(current.AppliedRevision, appliedRevision);

            return true;
        });

    public void Release(Guid participantId, string connectionId, long epoch) =>
        Locked(() =>
        {
            // Solo la connessione corrente si rimuove: la disconnessione della vecchia pagina, che
            // arriva dopo l'acquisizione della nuova, non deve scollegare chi ha preso il posto
            // (§12.5).
            if (TryGetCurrent(participantId, connectionId, epoch, out _))
            {
                _connections.Remove(participantId);
            }
        });

    public ParticipantConnectionState? Get(Guid participantId) =>
        Locked(() =>
            _connections.TryGetValue(participantId, out var current)
                ? new ParticipantConnectionState(
                    participantId,
                    current.SessionId,
                    current.ConnectionId,
                    current.ClientInstanceId,
                    current.Epoch,
                    PresenceOf(current, timeProvider.GetUtcNow()),
                    current.SnapshotApplied,
                    current.AppliedRevision,
                    current.RequiredRevision,
                    current.PageVisible,
                    current.AcquiredAtUtc,
                    current.LastHeartbeatUtc)
                : null);

    /// <summary>
    /// Presenza misurata dall'ultimo segno di vita (§11.6): recente, degradata, oppure non più
    /// operativa quando l'autorizzazione temporanea è scaduta.
    /// </summary>
    private static ParticipantPresence PresenceOf(ParticipantConnection current, DateTimeOffset nowUtc)
    {
        var sinceLastSignal = nowUtc - (current.LastHeartbeatUtc ?? current.AcquiredAtUtc);

        if (sinceLastSignal <= ParticipantProtocol.FreshPresenceThreshold)
        {
            return ParticipantPresence.Fresh;
        }

        return sinceLastSignal <= ParticipantProtocol.ExecutionLeaseDuration
            ? ParticipantPresence.Degraded
            : ParticipantPresence.NotOperational;
    }

    /// <summary>Una connessione è operativa finché l'autorizzazione temporanea non è scaduta.</summary>
    private static bool IsOperative(ParticipantConnection current, DateTimeOffset nowUtc) =>
        PresenceOf(current, nowUtc) != ParticipantPresence.NotOperational;

    /// <summary>
    /// Il rinnovo richiede identità corrente, caricamento concluso e stato applicato (§12.6): un
    /// comando consegnato e non ancora confermato sospende il rinnovo, così il player non continua a
    /// eseguire su uno stato che il server considera superato.
    /// </summary>
    private static bool CanRenew(ParticipantConnection current, ParticipantPresence presence) =>
        current.SnapshotApplied &&
        presence != ParticipantPresence.NotOperational &&
        current.AppliedRevision >= current.RequiredRevision;

    private ExecutionLease GrantLease(ParticipantConnection current, string nonce, DateTimeOffset nowUtc) =>
        new(
            nonce,
            current.AppliedRevision,
            current.Epoch,
            ParticipantProtocol.ExecutionLeaseDuration,
            nowUtc + ParticipantProtocol.ExecutionLeaseDuration);

    private bool TryGetCurrent(
        Guid participantId,
        string connectionId,
        long epoch,
        out ParticipantConnection current)
    {
        if (_connections.TryGetValue(participantId, out var stored) &&
            string.Equals(stored.ConnectionId, connectionId, StringComparison.Ordinal) &&
            stored.Epoch == epoch)
        {
            current = stored;

            return true;
        }

        current = null!;

        return false;
    }

    private T Locked<T>(Func<T> operation)
    {
        lock (_gate)
        {
            return operation();
        }
    }

    private void Locked(Action operation)
    {
        lock (_gate)
        {
            operation();
        }
    }

    /// <summary>Stato di una connessione operativa, tenuto per partecipante.</summary>
    private sealed class ParticipantConnection
    {
        public required string ConnectionId { get; init; }

        public required Guid ClientInstanceId { get; init; }

        public required Guid SessionId { get; init; }

        public required long Epoch { get; init; }

        public required DateTimeOffset AcquiredAtUtc { get; init; }

        /// <summary>Revisione che il client deve avere applicato per essere operativo (§12.6).</summary>
        public long RequiredRevision { get; set; }

        public long AppliedRevision { get; set; }

        public bool SnapshotApplied { get; set; }

        public bool PageVisible { get; set; } = true;

        public DateTimeOffset? LastHeartbeatUtc { get; set; }

        /// <summary>
        /// Nonce già usati da questa connessione. Si conservano fino al prossimo handshake.
        /// Al limite di capacità, il rinnovo richiede un nuovo handshake.
        /// </summary>
        private readonly HashSet<string> _consumedNonces = new(StringComparer.Ordinal);

        /// <summary>Consuma un nonce nuovo; falso se era già stato usato (§12.6).</summary>
        public bool ConsumeNonce(string nonce)
        {
            if (nonce.Length > 128 || _consumedNonces.Count >= ConsumedNonceCapacity || !_consumedNonces.Add(nonce))
            {
                return false;
            }

            return true;
        }
    }
}
