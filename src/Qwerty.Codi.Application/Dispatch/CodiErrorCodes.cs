namespace Qwerty.Codi.Application.Dispatch;

/// <summary>
/// Codici applicativi stabili di §10.7. Il chiamante decide in base al codice, non al testo del
/// messaggio: il codice è il contratto, il messaggio è per la persona che legge.
/// </summary>
public static class CodiErrorCodes
{
    /// <summary>412 — modifica concorrente ancora presente dopo un numero limitato di riletture.</summary>
    public const string RowVersionMismatch = "RowVersionMismatch";

    /// <summary>400 — il corpo non è deserializzabile nella richiesta indicata.</summary>
    public const string InvalidPayload = "InvalidPayload";

    /// <summary>400 — nome non utilizzabile per la risorsa richiesta.</summary>
    public const string InvalidName = "InvalidName";

    /// <summary>400 — una regola di validazione del caso d'uso non è soddisfatta.</summary>
    public const string ValidationFailed = "ValidationFailed";

    /// <summary>428 — la richiesta modifica una riga ma non dichiara la versione attesa (§10.2).</summary>
    public const string EntityVersionRequired = "EntityVersionRequired";

    /// <summary>404 — la risorsa richiesta non esiste o non è leggibile dal contesto corrente.</summary>
    public const string ResourceNotFound = "ResourceNotFound";

    /// <summary>409 — il nome normalizzato della scuola è già in uso (§7.2, §10.4).</summary>
    public const string SchoolAlreadyExists = "SchoolAlreadyExists";

    /// <summary>409 — nella scuola esiste già una classe con lo stesso nome e anno (§7.2, §10.4).</summary>
    public const string ClassroomAlreadyExists = "ClassroomAlreadyExists";

    /// <summary>409 — la classe ha già una sessione non terminata (§6.1, §7.6, §10.7).</summary>
    public const string SessionAlreadyOpen = "SessionAlreadyOpen";

    /// <summary>409 — la revisione attesa della sessione è superata (§6.4, §10.7).</summary>
    public const string SessionRevisionConflict = "SessionRevisionConflict";

    /// <summary>409 — lo stato della sessione non ammette la transizione richiesta (§6.1, §10.7).</summary>
    public const string SessionStateConflict = "SessionStateConflict";

    /// <summary>409 — gli ingressi della sessione sono chiusi (§6.1, §10.7).</summary>
    public const string AdmissionsClosed = "AdmissionsClosed";

    /// <summary>
    /// 409 — la stessa chiave di idempotenza è già stata usata con un contenuto diverso (§9.4).
    /// Ripetere la richiesta con lo stesso contenuto è invece un recupero legittimo.
    /// </summary>
    public const string IdempotencyKeyConflict = "IdempotencyKeyConflict";

    /// <summary>400 — mutazione autenticata con cookie senza token antiforgery valido (§10.2).</summary>
    public const string AntiforgeryRejected = "AntiforgeryRejected";

    /// <summary>413 — il contenuto inviato supera il limite ammesso (§10.7).</summary>
    public const string PayloadTooLarge = "PayloadTooLarge";

    /// <summary>503 — lo storage dei file non è disponibile (§10.7).</summary>
    public const string StorageUnavailable = "StorageUnavailable";

    /// <summary>401 — manca un contesto autenticato per la policy dichiarata.</summary>
    public const string AuthenticationRequired = "AuthenticationRequired";

    /// <summary>403 — contesto autenticato ma senza la policy richiesta.</summary>
    public const string PermissionDenied = "SessionPermissionDenied";

    /// <summary>401 — credenziali di accesso non corrispondenti.</summary>
    public const string CredentialsRejected = "CredentialsRejected";

    /// <summary>429 — troppi tentativi falliti: l'accesso è temporaneamente bloccato.</summary>
    public const string AccountLocked = "AccountLocked";

    /// <summary>
    /// 404 — il codice di ingresso non corrisponde ad alcuna sessione (§9.4). Il codice errato e
    /// quello di una sessione inesistente danno lo stesso esito: la ricerca di un codice non deve
    /// rivelare quali codici esistono.
    /// </summary>
    public const string InvalidJoinCode = "InvalidJoinCode";

    /// <summary>
    /// 429 — troppi codici errati dallo stesso browser in poco tempo (§17.3). Il blocco dura la
    /// finestra dei tentativi e si azzera al primo ingresso riuscito: non è una condanna.
    /// </summary>
    public const string TooManyJoinAttempts = "TooManyJoinAttempts";

    /// <summary>Thread della chat pieno: il messaggio non entra nella capienza di §15.1.</summary>
    public const string MessageThreadFull = "MessageThreadFull";

    /// <summary>
    /// 409 — l'ultima versione pubblicata di un esercizio non si elimina (§13.8): è quella che le
    /// prossime assegnazioni usano, e al suo posto se ne pubblica una nuova.
    /// </summary>
    public const string LatestExerciseVersion = "LatestExerciseVersion";

    /// <summary>409 — la versione è già stata assegnata a una sessione e non si elimina (§13.8).</summary>
    public const string ExerciseVersionInUse = "ExerciseVersionInUse";

    /// <summary>409 — bootstrap amministrativo richiesto su un'installazione che ha già account attivi.</summary>
    public const string BootstrapNotAllowed = "BootstrapNotAllowed";

    /// <summary>409 — nome normalizzato già in uso da un altro account.</summary>
    public const string AdminAccountAlreadyExists = "AdminAccountAlreadyExists";

    /// <summary>404 — nome di comando sconosciuto sul canale del dispatcher.</summary>
    public const string CommandNotFound = "CommandNotFound";

    /// <summary>404 — nome di query sconosciuto sul canale del dispatcher.</summary>
    public const string QueryNotFound = "QueryNotFound";
}
