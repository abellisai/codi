namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Registro delle connessioni operative dei partecipanti (§11.4, §12.5): chi è connesso, con quale
/// epoca, da quale istanza di pagina, quando ha dato l'ultimo segno di vita e se ha completato
/// l'handshake. Vive **in memoria** perché è stato del processo: presenza, autorizzazione temporanea
/// e sostituzione delle connessioni non sopravvivono a un riavvio, e il riavvio è una delle
/// condizioni che le invalidano (§15.1).
///
/// L'epoca persistita sulla partecipazione dice chi ha acquisito l'operatività; qui si sa se quella
/// connessione è ancora viva e sincronizzata. Le due informazioni non si sostituiscono: la prima
/// ordina i messaggi nel tempo, la seconda decide se inviarli o sospendere il player.
/// </summary>
public interface IParticipantConnectionRegistry
{
    /// <summary>
    /// Decide se una pagina può acquisire l'operatività **prima** che l'epoca venga incrementata
    /// (§12.5): la stessa istanza di pagina si riconnette e riacquisisce, una pagina diversa attende
    /// finché la precedente è operativa, e una connessione scaduta non blocca nessuno.
    /// </summary>
    ConnectionTakeover CanAcquire(Guid participantId, Guid clientInstanceId, DateTimeOffset nowUtc);

    /// <summary>
    /// Associa la connessione con l'epoca appena persistita e restituisce l'identificativo della
    /// connessione che ha perso l'operatività, se ce n'era una: è quella da avvisare con
    /// <c>ConnectionSuperseded</c> prima di chiuderla.
    /// </summary>
    string? Acquire(ParticipantConnectionRequest request, DateTimeOffset nowUtc);

    /// <summary>
    /// Registra la fine del caricamento iniziale (§11.2 passo 6) e restituisce falso se la
    /// connessione non è più quella corrente. Se lo stato è avanzato durante il caricamento lo decide
    /// chi chiama, confrontando la revisione persistita con quella dichiarata: la revisione
    /// autorevole è quella della sessione, non quella che il registro ricorda.
    /// </summary>
    bool MarkSnapshotApplied(SnapshotAppliedRequest request, DateTimeOffset nowUtc);

    /// <summary>
    /// Battito del client e rinnovo dell'autorizzazione temporanea (§11.6, §12.6). Il rinnovo è
    /// concesso solo a identità, epoca e stato applicato correnti; altrimenti il client resta senza
    /// autorizzazione e sospende il player.
    /// </summary>
    HeartbeatOutcome Heartbeat(HeartbeatRequest request, DateTimeOffset nowUtc);

    /// <summary>
    /// Registra la revisione che un comando consegnato ha reso necessaria: l'autorizzazione
    /// temporanea si rinnova solo a stato applicato, quindi un comando non ancora confermato
    /// sospende il rinnovo finché non arriva la conferma (§12.6).
    /// </summary>
    void NoteDesiredRevision(Guid participantId, long effectiveRevision);

    /// <summary>
    /// Registra la revisione che il client dichiara di avere applicato (§12.2). Restituisce falso se
    /// la connessione che ha risposto non è più quella corrente: una conferma arrivata da una pagina
    /// superata non riabilita nulla.
    /// </summary>
    bool NoteAppliedRevision(Guid participantId, string connectionId, long epoch, long appliedRevision);

    /// <summary>
    /// Chiude la connessione. La rimozione avviene solo se connessione ed epoca coincidono ancora:
    /// così la disconnessione della vecchia pagina non scollega quella che l'ha sostituita (§12.5).
    /// </summary>
    void Release(Guid participantId, string connectionId, long epoch);

    /// <summary>Stato della connessione, o <c>null</c> se il partecipante non ne ha una nota.</summary>
    ParticipantConnectionState? Get(Guid participantId);
}

/// <summary>
/// Esito della domanda «posso acquisire l'operatività?» (§12.5). Non è una risposta al client: il
/// trasporto la traduce nell'evento <c>ActiveConnectionExists</c> quando la pagina deve attendere.
/// </summary>
public enum ConnectionTakeover
{
    /// <summary>Nessuna connessione operativa: la nuova pagina acquisisce subito.</summary>
    Free,

    /// <summary>La stessa istanza di pagina si sta riconnettendo: riacquisisce incrementando l'epoca.</summary>
    SameInstance,

    /// <summary>Un'altra istanza di pagina è ancora operativa: la nuova resta in attesa.</summary>
    OtherInstanceActive,
}

/// <summary>Connessione che ha acquisito l'operatività, con l'epoca appena persistita.</summary>
public sealed record ParticipantConnectionRequest(
    Guid ParticipantId,
    Guid SessionId,
    Guid ClientInstanceId,
    string ConnectionId,
    long Epoch,
    long EffectiveRevision);

/// <summary>
/// Fine del caricamento iniziale (§11.2 passo 6): la revisione e l'epoca che il client dichiara di
/// avere applicato. Sono dati di sincronizzazione, non credenziali.
/// </summary>
public sealed record SnapshotAppliedRequest(
    Guid ParticipantId,
    string ConnectionId,
    long Epoch,
    long AppliedRevision);

/// <summary>
/// Battito del client (§11.3): identifica la connessione, dichiara la revisione applicata e se la
/// pagina è visibile. Il nonce lo genera il client a ogni battito e il server lo restituisce
/// nell'autorizzazione: riusare un nonce già consumato non rinnova il permesso, perché sarebbe una
/// richiesta ripetuta e non un nuovo segno di vita (§12.6).
/// </summary>
public sealed record HeartbeatRequest(
    Guid ParticipantId,
    string ConnectionId,
    long Epoch,
    string? Nonce,
    long AppliedRevision,
    bool PageVisible);

/// <summary>
/// Esito del battito: la presenza misurata dal server e, quando il rinnovo è concesso,
/// l'autorizzazione temporanea successiva. Nessuna scrittura: la presenza non si persiste.
/// </summary>
public sealed record HeartbeatOutcome(ParticipantPresence Presence, ExecutionLease? Lease);

/// <summary>
/// Presenza misurata **dal tempo di ricezione del server** (§11.6): un orologio del PC dell'alunno
/// non decide se un client è operativo.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ParticipantPresence>))]
public enum ParticipantPresence
{
    /// <summary>Segno di vita recente: il client è operativo.</summary>
    Fresh,

    /// <summary>Segno di vita vecchio ma dentro la validità dell'autorizzazione: presenza degradata.</summary>
    Degraded,

    /// <summary>Autorizzazione scaduta: il client non è operativo anche se il trasporto è aperto.</summary>
    NotOperational,

    /// <summary>Nessuna connessione registrata, o trasporto chiuso.</summary>
    Disconnected,
}

/// <summary>
/// Autorizzazione temporanea di esecuzione (§12.6). Il client misura la scadenza con il proprio
/// orologio monotono a partire dall'invio del battito, quindi il server dichiara anche la durata
/// massima e non solo l'istante di scadenza.
/// </summary>
public sealed record ExecutionLease(
    string Nonce,
    long EffectiveRevision,
    long ConnectionEpoch,
    TimeSpan MaxDuration,
    DateTimeOffset ValidUntilUtc);

/// <summary>
/// Fotografia della connessione di un partecipante (§11.6): quanto basta per decidere se inviare un
/// comando, se rinnovare un'autorizzazione e cosa mostrare al docente.
/// </summary>
public sealed record ParticipantConnectionState(
    Guid ParticipantId,
    Guid SessionId,
    string ConnectionId,
    Guid ClientInstanceId,
    long Epoch,
    ParticipantPresence Presence,
    bool SnapshotApplied,
    long AppliedRevision,
    long RequiredRevision,
    bool PageVisible,
    DateTimeOffset AcquiredAtUtc,
    DateTimeOffset? LastHeartbeatUtc);
