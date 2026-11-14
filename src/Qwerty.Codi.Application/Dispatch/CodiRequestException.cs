namespace Qwerty.Codi.Application.Dispatch;

/// <summary>
/// Errore applicativo previsto dal contratto (§10.7): porta un codice stabile che il trasporto
/// traduce in uno stato HTTP, senza dedurlo dal testo. Solo gli errori previsti hanno un tipo
/// proprio: quelli imprevisti restano eccezioni comuni e non diventano risposte di successo.
/// </summary>
public sealed class CodiRequestException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>Codice applicativo fra quelli di <see cref="CodiErrorCodes"/>.</summary>
    public string Code { get; } = code;

    /// <summary>Nome di richiesta sconosciuto, o richiesta non ammessa su questo canale (§10.1).</summary>
    public static CodiRequestException NotFound(CodiRequestKind kind, string requestName)
    {
        var code = kind == CodiRequestKind.Command
            ? CodiErrorCodes.CommandNotFound
            : CodiErrorCodes.QueryNotFound;
        var subject = kind == CodiRequestKind.Command ? "comando" : "query";

        // Il messaggio non elenca i nomi validi: un client non allineato non deve poter
        // ricostruire il catalogo da una risposta di errore.
        return new CodiRequestException(code, $"Il {subject} '{requestName}' non esiste.");
    }

    /// <summary>Nessuna identità autenticata: si risponde 401, non 403.</summary>
    public static CodiRequestException AuthenticationRequired(string requestName) =>
        new(
            CodiErrorCodes.AuthenticationRequired,
            $"La richiesta '{requestName}' richiede un accesso autenticato.");

    /// <summary>Identità presente ma policy mancante: la richiesta non è autorizzata.</summary>
    public static CodiRequestException PolicyDenied(string requestName, CodiPolicy policy) =>
        new(
            CodiErrorCodes.PermissionDenied,
            $"La richiesta '{requestName}' richiede il permesso '{policy}'.");

    /// <summary>Corpo incoerente con la richiesta indicata (§10.1).</summary>
    public static CodiRequestException InvalidPayload(string requestName, Exception? cause = null) =>
        new(
            CodiErrorCodes.InvalidPayload,
            $"Il corpo della richiesta '{requestName}' non corrisponde al formato atteso.",
            cause);

    /// <summary>
    /// Mutazione autenticata con cookie senza token antiforgery valido (§10.2). Il messaggio
    /// spiega come rimediare, senza dire se il problema sia il token mancante o quello scaduto:
    /// la differenza non cambia cosa deve fare chi chiama.
    /// </summary>
    public static CodiRequestException AntiforgeryRejected(Exception? cause = null) =>
        new(
            CodiErrorCodes.AntiforgeryRejected,
            "Il token antiforgery manca o non è valido: ripetere la richiesta dalla pagina che l'ha emesso.",
            cause);

    /// <summary>
    /// Credenziali non corrispondenti. Il messaggio è lo stesso sia che il nome non esista sia
    /// che la password sia sbagliata: distinguerli direbbe a chi prova quali nomi esistono.
    /// </summary>
    public static CodiRequestException CredentialsRejected() =>
        new(CodiErrorCodes.CredentialsRejected, "Nome utente o password non corrispondono.");

    /// <summary>Accesso temporaneamente bloccato dopo troppi tentativi falliti (§9.1).</summary>
    public static CodiRequestException AccountLocked(TimeSpan remaining) =>
        new(
            CodiErrorCodes.AccountLocked,
            $"Troppi tentativi falliti: l'accesso resta bloccato per {Math.Ceiling(remaining.TotalMinutes)} minuti.");

    /// <summary>
    /// La richiesta modifica una riga esistente senza dichiarare la versione attesa (§10.2). Il
    /// messaggio non dice se la risorsa esista: la versione manca prima di poterla leggere.
    /// </summary>
    public static CodiRequestException VersionRequired(string requestName) =>
        new(
            CodiErrorCodes.EntityVersionRequired,
            $"La richiesta '{requestName}' deve dichiarare la versione attesa della riga.");

    /// <summary>
    /// La riga è cambiata fra la lettura e la scrittura (§10.2). Chi chiama rilegge e ripete la
    /// modifica sulla versione aggiornata: ripetere la stessa richiesta non è una strategia.
    /// </summary>
    public static CodiRequestException RowVersionMismatch() =>
        new(
            CodiErrorCodes.RowVersionMismatch,
            "La riga è stata modificata nel frattempo: ricaricare i dati e ripetere la modifica.");

    /// <summary>
    /// Risorsa assente o non leggibile dal contesto corrente (§10.7). Non si distingue il caso
    /// «non esiste» dal caso «esiste ma non è tua»: la differenza direbbe a chi prova quali
    /// identificativi sono validi.
    /// </summary>
    public static CodiRequestException ResourceNotFound() =>
        new(CodiErrorCodes.ResourceNotFound, "La risorsa richiesta non esiste o non è accessibile.");

    /// <summary>
    /// La sessione non accetta ingressi in questo momento (§6.1, §10.7): è avviata ma gli ingressi
    /// sono chiusi. Il messaggio è per l'alunno, che non può fare nulla se non aspettare.
    /// </summary>
    public static CodiRequestException AdmissionsClosed() =>
        new(CodiErrorCodes.AdmissionsClosed, "La lezione non accetta nuovi ingressi: chiedi all’insegnante.");

    /// <summary>
    /// La stessa chiave di idempotenza con un contenuto diverso (§9.4): ripetere la richiesta non
    /// recupera un esito, perché l'esito registrato appartiene a un'altra richiesta.
    /// </summary>
    public static CodiRequestException IdempotencyKeyConflict() =>
        new(
            CodiErrorCodes.IdempotencyKeyConflict,
            "La stessa chiave di richiesta è già stata usata con un contenuto diverso.");

    /// <summary>
    /// Il codice di ingresso non corrisponde ad alcuna sessione (§9.4, §10.7). Un codice mai
    /// assegnato e uno scritto male hanno lo stesso esito: la ricerca non deve rivelare quali codici
    /// esistono. Il messaggio è per l'alunno, che può solo rileggere il codice e riprovare.
    /// </summary>
    public static CodiRequestException InvalidJoinCode() =>
        new(
            CodiErrorCodes.InvalidJoinCode,
            "Il codice non è valido: controlla le otto cifre e chiedile di nuovo all’insegnante.");

    /// <summary>
    /// Troppi codici errati in poco tempo dallo stesso browser o dallo stesso indirizzo (§17.3):
    /// il tentativo è sospeso per la finestra dei tentativi. Non è una condanna — un ingresso
    /// riuscito azzera il conteggio — ma nemmeno un avviso da ignorare.
    /// </summary>
    public static CodiRequestException TooManyJoinAttempts() =>
        new(
            CodiErrorCodes.TooManyJoinAttempts,
            "Troppi codici errati: aspetta un momento e riprova, oppure chiedi all’insegnante.");
}
