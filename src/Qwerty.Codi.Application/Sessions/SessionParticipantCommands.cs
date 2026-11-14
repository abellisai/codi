using Qwerty.Codi.Application.Dto;
using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Ingresso idempotente di un alunno nella sessione (§9.4, §10.4), in **due forme**: con il
/// **codice di ingresso** che il docente detta a voce — otto cifre — oppure con la **catena** che il
/// client ha scelto, cioè scuola, classe e sessione. Ne va dichiarata esattamente una: due forme
/// nella stessa richiesta lascerebbero al server la scelta di quale credere, e nessuna delle due non
/// è un ingresso.
///
/// La forma con codice è quella pensata per l'aula: il bambino digita otto cifre e il proprio nome,
/// senza conoscere identificativi che non ha modo di vedere. La forma a catena resta l'alternativa
/// — un docente senza codice sotto mano, una sessione avviata prima che il codice esistesse — e
/// segue le stesse verifiche: catena coerente, sessione in play, ingressi aperti.
///
/// Due dati non arrivano dal corpo e non potrebbero: lo **scope di ammissione**, che il trasporto
/// legge dal cookie emesso dal bootstrap, e la **chiave di idempotenza**, che arriva
/// nell'intestazione (§10.2). Entrambi viaggiano nel contesto della richiesta, così nessun corpo
/// può attribuire a un altro browser un ingresso ripetuto.
///
/// <c>BrowserKey</c> è facoltativa e non autorizza nulla: serve solo a ricordare da quale postazione
/// è entrato l'alunno (§7.2, §9.5). Senza di essa la partecipazione resta senza postazione.
/// </summary>
public sealed record JoinSessionCommand(
    string DisplayName,
    Guid? SchoolId = null,
    Guid? ClassroomId = null,
    Guid? SessionId = null,
    string? BrowserKey = null,
    string? JoinCode = null) : ICodiCommand<JoinSessionOutcome>, ICodiContextualRequest
{
    public static string RequestName => AnonymousBootstrapRequests.JoinSession;

    public static CodiPolicy Policy => CodiPolicy.AnonymousBootstrap;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Esito dell'ingresso per il trasporto: i dati della partecipazione più la marca che il cookie
/// deve portare. La marca esce dal caso d'uso soltanto verso il trasporto — che la scrive nel
/// cookie e non nel corpo della risposta — perché è l'unico modo per legare il ticket alla riga
/// della credenziale senza conservare il ticket (§9.2).
/// </summary>
public sealed record JoinSessionOutcome(ParticipantJoinedDto Joined, string CredentialStamp);

/// <summary>
/// Uscita volontaria dell'alunno dalla sessione (§9.2, §10.3): l'ultima azione che la credenziale
/// consente prima di essere revocata. L'alunno la richiede dal proprio PC — per esempio quando il
/// turno è finito — e il trasporto, dopo l'esito, cancella il cookie.
///
/// L'uscita registra il proprio istante e revoca le credenziali. La partecipazione e i lavori
/// restano nello storico, ma la regia e i comandi collettivi non includono più chi è uscito.
/// Una credenziale revocata non si riapre ripetendo l'uscita (§9.4): la ripetizione è rifiutata
/// come richiesta senza identità, perché la revoca è una decisione e non una risposta persa.
///
/// Il corpo non dichiara nulla: l'identità — partecipazione e credenziale — arriva dal contesto
/// verificato dal trasporto (§4.4, §9.3), quindi un alunno non può far uscire un altro.
/// </summary>
public sealed record LeaveSessionCommand : ICodiCommand<bool>, ICodiContextualRequest
{
    public static string RequestName => "LeaveSession";

    public static CodiPolicy Policy => CodiPolicy.AdmittedParticipant;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Corregge il nome visualizzato e la postazione di un partecipante (§10.4, §9.5). È una modifica
/// di una riga già esistente, quindi dichiara la versione attesa (<c>baseRowVersion</c>): una
/// correzione su una riga cambiata nel frattempo è rifiutata con <c>412</c> invece di sovrascrivere
/// (§10.2).
///
/// La sessione è dichiarata perché il proprietario della regia e l'appartenenza del partecipante
/// si verificano contro di essa; non cambia lo stato della sessione né la sua revisione. La
/// postazione è facoltativa: assente significa «PC non noto», che è uno stato legittimo (§7.2).
/// </summary>
public sealed record UpdateParticipantCommand(
    Guid SessionId,
    Guid ParticipantId,
    string DisplayName,
    Guid? StationId = null,
    long? BaseRowVersion = null) : ICodiCommand<SessionParticipantDto>, ICodiContextualRequest
{
    public static string RequestName => "UpdateParticipant";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Esclude un partecipante dalla sessione (§10.4): revoca operativa, senza cancellare la riga, così
/// il lavoro già svolto non si perde. Modifica una riga esistente e dichiara la versione attesa
/// (<c>baseRowVersion</c>), perché una doppia esclusione concorrente non deve sovrascriversi (§10.2).
///
/// La chiusura della connessione del partecipante appartiene agli hub (§12.5) e non esiste ancora:
/// qui si persiste lo stato di ammissione, che è ciò che il contesto verifica a ogni richiesta
/// (§9.2), quindi l'esclusione toglie subito l'operatività anche senza chiudere il trasporto.
/// </summary>
public sealed record ExcludeParticipantCommand(
    Guid SessionId,
    Guid ParticipantId,
    long? BaseRowVersion = null) : ICodiCommand<SessionParticipantDto>, ICodiContextualRequest
{
    public static string RequestName => "ExcludeParticipant";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}
