namespace Qwerty.Codi.Domain.Sessions;

/// <summary>
/// Sessione di lezione (§6.1): l'istanza del momento che lega una lezione a una classe e assegna
/// la regia a un amministratore proprietario. Non è un'associazione permanente fra lezione e
/// classe: nasce alla creazione e termina con lo Stop.
///
/// L'avvio è una transizione di stato con revisione controllata. Il caso d'uso verifica revisione
/// e stato attesi **prima** di invocare <see cref="Start"/> e traduce il rifiuto nel codice di
/// §10.7; <see cref="Start"/> riafferma qui l'invariante, che un uso scorretto del dominio non deve
/// poter aggirare.
/// </summary>
public sealed class LessonSession
{
    /// <summary>
    /// Default di classe alla creazione: la vista di attesa, senza attività assegnata (§6.1).
    /// Il valore è il JSON persistito in <c>class_default_json</c>.
    /// </summary>
    public const string WaitingClassJson = "{\"view\":\"Waiting\"}";

    private LessonSession(
        Guid id,
        Guid lessonId,
        Guid classroomId,
        Guid controllerAdminId,
        SessionStatus status,
        bool admissionsOpen,
        long sessionRevision,
        string classDefaultJson,
        SessionJoinCode? joinCode,
        DateTimeOffset? startedAtUtc,
        DateTimeOffset? endedAtUtc,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        long rowVersion)
    {
        Id = id;
        LessonId = lessonId;
        ClassroomId = classroomId;
        ControllerAdminId = controllerAdminId;
        Status = status;
        AdmissionsOpen = admissionsOpen;
        SessionRevision = sessionRevision;
        ClassDefaultJson = classDefaultJson;
        JoinCode = joinCode;
        StartedAtUtc = startedAtUtc;
        EndedAtUtc = endedAtUtc;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        RowVersion = rowVersion;
    }

    public Guid Id { get; }

    /// <summary>Lezione istanziata nella classe.</summary>
    public Guid LessonId { get; }

    /// <summary>Classe che ospita la sessione: al più una sessione non terminata per classe (§7.6).</summary>
    public Guid ClassroomId { get; }

    /// <summary>Amministratore proprietario della regia: deciso alla creazione, non cambia.</summary>
    public Guid ControllerAdminId { get; }

    /// <summary>Stato corrente della sessione (§6.1).</summary>
    public SessionStatus Status { get; private set; }

    /// <summary>
    /// Ingressi aperti o chiusi. È indipendente dallo stato (§6.1): la creazione li chiude, l'avvio
    /// li apre, e i comandi dedicati li riaprono o richiudono senza toccare lo stato.
    /// </summary>
    public bool AdmissionsOpen { get; private set; }

    /// <summary>Revisione monotona delle mutazioni della sessione (§5.3): ordinamento dei comandi di regia.</summary>
    public long SessionRevision { get; private set; }

    /// <summary>JSON dello stato di classe predefinito per i nuovi ingressi.</summary>
    public string ClassDefaultJson { get; private set; }

    /// <summary>
    /// Codice di ingresso dettato a voce (§9.4): generato all'avvio, resta lo stesso per tutta la
    /// sessione — pausa e ripresa comprese — e non si riusa in un'altra sessione. È nullo in
    /// <see cref="SessionStatus.Lobby"/> e nelle sessioni avviate prima dell'introduzione del
    /// codice, che restano raggiungibili dalla sola catena scuola → classe → sessione.
    /// </summary>
    public SessionJoinCode? JoinCode { get; private set; }

    /// <summary>Istante di avvio; nullo finché la sessione è in <see cref="SessionStatus.Lobby"/>.</summary>
    public DateTimeOffset? StartedAtUtc { get; private set; }

    /// <summary>Istante di termine; nullo finché la sessione non è <see cref="SessionStatus.Ended"/>.</summary>
    public DateTimeOffset? EndedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Versione della riga per la concorrenza ottimistica (§7.1).</summary>
    public long RowVersion { get; }

    /// <summary>
    /// Crea la sessione del momento in <see cref="SessionStatus.Lobby"/>: ingressi chiusi, revisione
    /// iniziale a uno, vista di attesa come default della classe e nessun avvio registrato (§6.1).
    /// </summary>
    public static LessonSession Create(Guid lessonId, Guid classroomId, Guid controllerAdminId, DateTimeOffset nowUtc)
    {
        EnsureIdentifier(lessonId, nameof(lessonId), "Una sessione riferisce una lezione.");
        EnsureIdentifier(classroomId, nameof(classroomId), "Una sessione riferisce una classe.");
        EnsureIdentifier(controllerAdminId, nameof(controllerAdminId), "Una sessione ha un amministratore di regia.");
        EnsureUtc(nowUtc, nameof(nowUtc));

        return new LessonSession(
            Guid.NewGuid(), lessonId, classroomId, controllerAdminId,
            SessionStatus.Lobby, admissionsOpen: false, sessionRevision: 1, WaitingClassJson,
            joinCode: null,
            startedAtUtc: null, endedAtUtc: null,
            createdAtUtc: nowUtc, updatedAtUtc: nowUtc, rowVersion: 1);
    }

    /// <summary>Ricostruisce la sessione da quanto è stato persistito, validando i dati letti.</summary>
    public static LessonSession Restore(
        Guid id,
        Guid lessonId,
        Guid classroomId,
        Guid controllerAdminId,
        SessionStatus status,
        bool admissionsOpen,
        long sessionRevision,
        string classDefaultJson,
        SessionJoinCode? joinCode,
        DateTimeOffset? startedAtUtc,
        DateTimeOffset? endedAtUtc,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        long rowVersion)
    {
        EnsureIdentifier(id, nameof(id), "L'identificativo della sessione è obbligatorio.");
        EnsureIdentifier(lessonId, nameof(lessonId), "Una sessione riferisce una lezione.");
        EnsureIdentifier(classroomId, nameof(classroomId), "Una sessione riferisce una classe.");
        EnsureIdentifier(controllerAdminId, nameof(controllerAdminId), "Una sessione ha un amministratore di regia.");

        if (string.IsNullOrWhiteSpace(classDefaultJson))
        {
            throw new ArgumentException(
                "Lo stato di classe predefinito della sessione è obbligatorio.", nameof(classDefaultJson));
        }

        if (sessionRevision < 1)
        {
            throw new ArgumentException("La revisione della sessione deve essere positiva.", nameof(sessionRevision));
        }

        if (updatedAtUtc < createdAtUtc)
        {
            throw new ArgumentException("L'ultima modifica non può precedere la creazione.", nameof(updatedAtUtc));
        }

        if (rowVersion < 1)
        {
            throw new ArgumentException("La versione di riga deve essere positiva.", nameof(rowVersion));
        }

        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        if (startedAtUtc is { } started)
        {
            EnsureUtc(started, nameof(startedAtUtc));
        }

        if (endedAtUtc is { } ended)
        {
            EnsureUtc(ended, nameof(endedAtUtc));
        }

        EnsureStateCoherence(status, startedAtUtc, endedAtUtc);
        EnsureJoinCodeCoherence(status, joinCode);

        return new LessonSession(
            id, lessonId, classroomId, controllerAdminId,
            status, admissionsOpen, sessionRevision, classDefaultJson, joinCode,
            startedAtUtc, endedAtUtc, createdAtUtc, updatedAtUtc, rowVersion);
    }

    /// <summary>
    /// Porta la sessione da <see cref="SessionStatus.Lobby"/> ad <see cref="SessionStatus.Active"/>:
    /// apre gli ingressi, registra l'avvio, riceve il **codice di ingresso** che il docente detterà e
    /// incrementa la revisione (§6.1).
    ///
    /// Il codice arriva dal chiamante e non è generato qui: il caso d'uso deve poterlo rigenerare se
    /// il database rifiuta un codice già assegnato, mentre il dominio non conosce il database. Un
    /// codice mancante è un uso scorretto del dominio, non un avvio senza codice.
    ///
    /// La revisione attesa e lo stato sono invarianti: se non reggono, il chiamante ha usato il
    /// dominio in modo scorretto — il rifiuto previsto del comando va prodotto dal caso d'uso con
    /// i codici di §10.7, non da qui.
    /// </summary>
    public void Start(long expectedSessionRevision, DateTimeOffset nowUtc, SessionJoinCode joinCode)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));

        if (joinCode.Value is null)
        {
            throw new ArgumentException(
                "L'avvio genera un codice di ingresso: un codice assente o vuoto non è un codice.", nameof(joinCode));
        }

        if (expectedSessionRevision != SessionRevision)
        {
            throw new InvalidOperationException(
                "La revisione attesa non corrisponde a quella della sessione: il caso d'uso deve produrre SessionRevisionConflict.");
        }

        if (Status != SessionStatus.Lobby)
        {
            throw new InvalidOperationException(
                "Solo una sessione in Lobby può essere avviata: il caso d'uso deve produrre SessionStateConflict.");
        }

        Status = SessionStatus.Active;
        AdmissionsOpen = true;
        JoinCode = joinCode;
        StartedAtUtc = nowUtc;
        SessionRevision = checked(SessionRevision + 1);
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Sospende la sessione con una pausa di classe (§6.1, §6.4): transizione ammessa solo da
    /// <see cref="SessionStatus.Active"/>, perché non si mette in pausa una sessione in
    /// <see cref="SessionStatus.Lobby"/> e la transizione <c>Lobby → Paused</c> non esiste. Gli
    /// ingressi e gli istanti restano com'è: la pausa non chiude gli ingressi.
    ///
    /// Revisione attesa e stato sono invarianti: se non reggono, il chiamante ha usato il dominio
    /// in modo scorretto — il rifiuto previsto del comando va prodotto dal caso d'uso con i codici
    /// di §10.7, non da qui.
    /// </summary>
    public void Pause(long expectedSessionRevision, DateTimeOffset nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));

        if (expectedSessionRevision != SessionRevision)
        {
            throw new InvalidOperationException(
                "La revisione attesa non corrisponde a quella della sessione: il caso d'uso deve produrre SessionRevisionConflict.");
        }

        if (Status != SessionStatus.Active)
        {
            throw new InvalidOperationException(
                "Solo una sessione in Active può essere messa in pausa: il caso d'uso deve produrre SessionStateConflict.");
        }

        Status = SessionStatus.Paused;
        SessionRevision = checked(SessionRevision + 1);
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Riprende la sessione da una pausa di classe (§6.1, §6.4): transizione ammessa solo da
    /// <see cref="SessionStatus.Paused"/>. Gli ingressi e gli istanti restano invariati.
    ///
    /// Revisione attesa e stato sono invarianti, come per <see cref="Pause"/>.
    /// </summary>
    public void Resume(long expectedSessionRevision, DateTimeOffset nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));

        if (expectedSessionRevision != SessionRevision)
        {
            throw new InvalidOperationException(
                "La revisione attesa non corrisponde a quella della sessione: il caso d'uso deve produrre SessionRevisionConflict.");
        }

        if (Status != SessionStatus.Paused)
        {
            throw new InvalidOperationException(
                "Solo una sessione in Paused può essere ripresa: il caso d'uso deve produrre SessionStateConflict.");
        }

        Status = SessionStatus.Active;
        SessionRevision = checked(SessionRevision + 1);
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Termina la sessione (§6.1, §6.4): transizione ammessa da qualunque stato non terminale,
    /// <see cref="SessionStatus.Lobby"/> compreso. Chiude gli ingressi, registra l'istante di
    /// termine e porta la sessione in <see cref="SessionStatus.Ended"/>, che è terminale.
    ///
    /// Revisione attesa e stato sono invarianti, come per <see cref="Pause"/>: una sessione già
    /// <see cref="SessionStatus.Ended"/> non si termina di nuovo.
    /// </summary>
    public void End(long expectedSessionRevision, DateTimeOffset nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));

        if (expectedSessionRevision != SessionRevision)
        {
            throw new InvalidOperationException(
                "La revisione attesa non corrisponde a quella della sessione: il caso d'uso deve produrre SessionRevisionConflict.");
        }

        if (Status == SessionStatus.Ended)
        {
            throw new InvalidOperationException(
                "Una sessione in Ended non si termina di nuovo: il caso d'uso deve produrre SessionStateConflict.");
        }

        Status = SessionStatus.Ended;
        AdmissionsOpen = false;
        EndedAtUtc = nowUtc;
        SessionRevision = checked(SessionRevision + 1);
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Apre gli ingressi (§6.1, §10.4): cambia soltanto <see cref="AdmissionsOpen"/> senza toccare
    /// lo stato, così il docente può riaprirli dopo l'avvio. In <see cref="SessionStatus.Ended"/>
    /// gli ingressi restano chiusi e la richiesta è rifiutata dal caso d'uso.
    ///
    /// Ripetere l'apertura su ingressi già aperti non cambia nulla e non incrementa la revisione:
    /// è la stessa idempotenza dell'archiviazione ripetuta. Restituisce <c>true</c> se la sessione
    /// è stata modificata.
    /// </summary>
    public bool OpenAdmissions(long expectedSessionRevision, DateTimeOffset nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        EnsureRevisionMatches(expectedSessionRevision);
        EnsureNotEnded();

        if (AdmissionsOpen)
        {
            return false;
        }

        AdmissionsOpen = true;
        SessionRevision = checked(SessionRevision + 1);
        UpdatedAtUtc = nowUtc;

        return true;
    }

    /// <summary>
    /// Chiude gli ingressi (§6.1, §10.4): come <see cref="OpenAdmissions"/> cambia soltanto
    /// <see cref="AdmissionsOpen"/>. Ripetere la chiusura su ingressi già chiusi non cambia nulla.
    /// </summary>
    public bool CloseAdmissions(long expectedSessionRevision, DateTimeOffset nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        EnsureRevisionMatches(expectedSessionRevision);
        EnsureNotEnded();

        if (!AdmissionsOpen)
        {
            return false;
        }

        AdmissionsOpen = false;
        SessionRevision = checked(SessionRevision + 1);
        UpdatedAtUtc = nowUtc;

        return true;
    }

    /// <summary>
    /// Mostra l'attesa alla classe (§6.4, §6.5): riporta il default di classe alla vista di attesa
    /// e incrementa la revisione. È il primo comando che cambia lo **stato desiderato**: il default
    /// vale per i nuovi ingressi, mentre gli stati già materializzati dei partecipanti li aggiorna
    /// il caso d'uso, che li legge e li scrive nella stessa transazione.
    ///
    /// Ammessa da qualunque stato non terminale, <c>Lobby</c> compreso, come <c>EndSession</c>: il
    /// lavoro sospeso non si perde, perché la sessione non cambia stato.
    /// </summary>
    public void ShowWaiting(long expectedSessionRevision, DateTimeOffset nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        EnsureRevisionMatches(expectedSessionRevision);
        EnsureNotEnded();

        ClassDefaultJson = WaitingClassJson;
        SessionRevision = checked(SessionRevision + 1);
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Registra un cambiamento dello stato desiderato che **non** cambia il default di classe (§6.5).
    /// L'assegnazione destina materiale a destinatari già presenti — la classe intera o un singolo
    /// alunno — mentre il default resta quello di partenza per chi entra dopo: un ingresso successivo
    /// parte dalla vista di attesa finché la regia non lo assegna di nuovo. La revisione avanza
    /// comunque, perché lo stato desiderato della sessione è cambiato, e il caso d'uso scrive la
    /// stessa revisione sulle righe di stato che ha aggiornato.
    /// </summary>
    public void RegisterDesiredStateChange(long expectedSessionRevision, DateTimeOffset nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        EnsureRevisionMatches(expectedSessionRevision);
        EnsureNotEnded();

        SessionRevision = checked(SessionRevision + 1);
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Copia con la versione di riga assegnata dalla persistenza.</summary>
    public LessonSession WithRowVersion(long rowVersion)
    {
        if (rowVersion < 1)
        {
            throw new ArgumentException("La versione di riga deve essere positiva.", nameof(rowVersion));
        }

        return new LessonSession(
            Id, LessonId, ClassroomId, ControllerAdminId,
            Status, AdmissionsOpen, SessionRevision, ClassDefaultJson, JoinCode,
            StartedAtUtc, EndedAtUtc, CreatedAtUtc, UpdatedAtUtc, rowVersion);
    }

    /// <summary>Revisione attesa diversa da quella corrente: il caso d'uso deve produrre il conflitto.</summary>
    private void EnsureRevisionMatches(long expectedSessionRevision)
    {
        if (expectedSessionRevision != SessionRevision)
        {
            throw new InvalidOperationException(
                "La revisione attesa non corrisponde a quella della sessione: il caso d'uso deve produrre SessionRevisionConflict.");
        }
    }

    /// <summary>Una sessione terminata non accetta più mutazioni di regia (§6.1).</summary>
    private void EnsureNotEnded()
    {
        if (Status == SessionStatus.Ended)
        {
            throw new InvalidOperationException(
                "Una sessione terminata non accetta comandi di regia: il caso d'uso deve produrre SessionStateConflict.");
        }
    }

    /// <summary>
    /// Coerenza fra stato e istanti letti: la sessione in attesa non ha né avvio né termine; una
    /// sessione avviata ha l'avvio e non il termine; la sessione terminata ha sempre il termine, e
    /// può non avere l'avvio quando lo Stop arriva direttamente dalla <see cref="SessionStatus.Lobby"/> (§6.1).
    /// </summary>
    private static void EnsureStateCoherence(
        SessionStatus status,
        DateTimeOffset? startedAtUtc,
        DateTimeOffset? endedAtUtc)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Stato della sessione non riconosciuto.");
        }

        if (status == SessionStatus.Lobby && (startedAtUtc is not null || endedAtUtc is not null))
        {
            throw new ArgumentException(
                "Una sessione in attesa non è ancora avviata né terminata.", nameof(startedAtUtc));
        }

        if (status is SessionStatus.Active or SessionStatus.Paused && (startedAtUtc is null || endedAtUtc is not null))
        {
            throw new ArgumentException(
                "Una sessione avviata ha un istante di avvio e non ancora di termine.", nameof(startedAtUtc));
        }

        if (status == SessionStatus.Ended && endedAtUtc is null)
        {
            throw new ArgumentException(
                "Una sessione terminata ha un istante di termine.", nameof(endedAtUtc));
        }
    }

    /// <summary>
    /// Coerenza del codice di ingresso con lo stato letto: una sessione in attesa non ha codice,
    /// perché il codice nasce con l'avvio. Un codice assente su una sessione avviata o terminata è
    /// invece ammesso: è lo stato delle sessioni create prima dell'introduzione del codice, che
    /// restano raggiungibili dalla sola catena scuola → classe → sessione.
    /// </summary>
    private static void EnsureJoinCodeCoherence(SessionStatus status, SessionJoinCode? joinCode)
    {
        if (status == SessionStatus.Lobby && joinCode is not null)
        {
            throw new ArgumentException(
                "Una sessione in attesa non ha un codice di ingresso: il codice nasce con l'avvio.",
                nameof(joinCode));
        }

        if (joinCode is { Value: null })
        {
            throw new ArgumentException(
                "Un codice di ingresso vuoto non è un codice.", nameof(joinCode));
        }
    }

    private static void EnsureIdentifier(Guid value, string parameterName, string message)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(message, parameterName);
        }
    }

    private static void EnsureUtc(DateTimeOffset instant, string parameterName)
    {
        if (instant.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("L'istante deve essere in UTC.", parameterName);
        }
    }
}
