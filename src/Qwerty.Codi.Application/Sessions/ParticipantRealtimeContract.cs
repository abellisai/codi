using Qwerty.Codi.Application.Abstractions;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Valori del protocollo realtime che server e client devono conoscere insieme (§11.4, §11.5). Sono
/// costanti e non configurazione: cambiarli cambia il contratto, quindi la modifica passa dalla
/// specifica e non da un file di impostazioni.
/// </summary>
public static class ParticipantProtocol
{
    /// <summary>Versione dell'involucro di §11.4: la prima, e per ora l'unica.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Cadenza del battito dichiarata al client (§11.5).</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(3);

    /// <summary>Oltre questo tempo senza battito la presenza è degradata (§11.6).</summary>
    public static readonly TimeSpan FreshPresenceThreshold = TimeSpan.FromSeconds(6);

    /// <summary>Validità massima dell'autorizzazione temporanea di esecuzione (§11.5, §12.6).</summary>
    public static readonly TimeSpan ExecutionLeaseDuration = TimeSpan.FromSeconds(10);

    /// <summary>Attesa di una conferma prima di considerarla mancata (§11.5, §12.2).</summary>
    public static readonly TimeSpan CommandAcknowledgementTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Lunghezza massima ammessa per le versioni dichiarate dal client (§11.2 passo 3).</summary>
    public const int MaxVersionLength = 32;
}

/// <summary>
/// Nomi degli eventi e dei metodi hub di §11.3. Stanno in un solo posto perché sono il contratto fra
/// due applicazioni diverse — il server e il player — che non condividono il codice.
/// </summary>
public static class ParticipantRealtimeEvents
{
    public const string InitializeClient = "InitializeClient";
    public const string SessionSnapshot = "SessionSnapshot";
    public const string SnapshotApplied = "SnapshotApplied";
    public const string Heartbeat = "Heartbeat";
    public const string LeaseGranted = "LeaseGranted";
    public const string CommandIssued = "CommandIssued";
    public const string CommandAcknowledged = "CommandAcknowledged";
    public const string ConnectionSuperseded = "ConnectionSuperseded";
    public const string ActiveConnectionExists = "ActiveConnectionExists";
    public const string ParticipationRevoked = "ParticipationRevoked";

    /// <summary>
    /// Stato terminale della sessione (§11.3): l'alunno riceve l'ultimo involucro insieme ai permessi
    /// di salvataggio conclusivo e alla loro scadenza. Era l'unico evento inviato con il nome scritto
    /// a mano nell'adattatore, e quindi il solo che poteva divergere dal contratto senza che nulla
    /// protestasse.
    /// </summary>
    public const string SessionEnded = "SessionEnded";

    /// <summary>
    /// Messaggio scritto dal docente nella chat del singolo alunno (§8, §15.1). Va solo al gruppo di
    /// quel partecipante: la chat non esiste fra bambini, quindi nessun altro alunno lo riceve.
    /// </summary>
    public const string ParticipantMessageReceived = "ParticipantMessageReceived";
}

/// <summary>
/// Dati che il client dichiara all'handshake (§11.2 passo 3): le proprie versioni e l'istanza di
/// pagina. L'istanza di pagina non è una credenziale (§12.5): serve a distinguere una riconnessione
/// della stessa pagina da una seconda scheda.
/// </summary>
public sealed record InitializeClientArguments(
    int ProtocolVersion,
    Guid ClientInstanceId,
    string AppVersion,
    string EngineVersion);

/// <summary>Dati che il client dichiara alla fine del caricamento iniziale (§11.2 passo 6).</summary>
public sealed record SnapshotAppliedArguments(long SessionRevision, long ConnectionEpoch);

/// <summary>Dati del battito inviato dal client (§11.3).</summary>
public sealed record HeartbeatArguments(long SessionRevision, long ConnectionEpoch, string? Nonce, bool PageVisible,
    Guid? ServerInstanceId = null);

/// <summary>
/// Dati del messaggio scritto dall'alunno nella propria chat (§11.3). La sessione e il partecipante non
/// stanno qui: arrivano dalla credenziale verificata e dal registro delle connessioni (§9.3).
/// </summary>
public sealed record PostMessageArguments(string Text, string ClientMessageKey);

/// <summary>
/// Dati del messaggio scritto dal docente verso un singolo alunno (§11.3). Il docente arriva dalla
/// credenziale; la sessione e il partecipante sono i destinatari che il caso d'uso verifica.
/// </summary>
public sealed record PostTeacherMessageArguments(
    Guid SessionId,
    Guid ParticipantId,
    string Text,
    string ClientMessageKey);
