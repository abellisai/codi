using Qwerty.Codi.Application.Dto;
using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Istanzia una lezione in una classe: crea la sessione del momento in <c>Lobby</c> con gli
/// ingressi chiusi (§6.1, §10.4). Non crea associazioni permanenti tra lezione e classe.
///
/// L'amministratore proprietario è quello della credenziale verificata dal trasporto, mai un
/// identificativo del payload: il contesto è associato dall'esecutore prima dell'handler (§9.3).
/// </summary>
public sealed record CreateSessionCommand(Guid LessonId, Guid ClassroomId)
    : ICodiCommand<SessionDto>, ICodiContextualRequest
{
    public static string RequestName => "CreateSession";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Avvia una sessione in <c>Lobby</c>: la porta in play, apre gli ingressi e imposta la vista di
/// attesa come default della classe, senza attività assegnata (§6.1, §10.4).
///
/// <c>ExpectedSessionRevision</c> è la revisione letta dal docente: protegge dai comandi di regia
/// applicati su uno stato superato (§6.4). È facoltativa nel record perché la sua assenza ha un
/// esito proprio — <c>428 EntityVersionRequired</c> di §10.2 — e non un corpo non deserializzabile.
/// Il proprietario della sessione è quello della credenziale verificata dal trasporto, non un
/// identificativo del payload.
/// </summary>
public sealed record StartSessionCommand(Guid SessionId, long? ExpectedSessionRevision = null)
    : ICodiCommand<SessionDto>, ICodiContextualRequest
{
    public static string RequestName => "StartSession";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Mette in pausa la sessione con una pausa di classe (§6.1, §6.4, §10.4): la porta da
/// <c>Active</c> a <c>Paused</c>. La transizione da <c>Lobby</c> non esiste, quindi una sessione
/// non ancora avviata non si mette in pausa; gli ingressi restano com'erano.
///
/// In questa porzione esiste il solo ambito <c>Class</c>: il campo <c>target</c> di §6.5, che
/// distingue l'ambito collettivo da quello di un singolo partecipante, arriva con i comandi ai
/// partecipanti e qui non è previsto. Aggiungerlo prima significherebbe fissare nel contratto un
/// caso d'uso che non esiste ancora.
///
/// <c>ExpectedSessionRevision</c> è la revisione letta dal docente: protegge dai comandi di regia
/// applicati su uno stato superato (§6.4). È facoltativa nel record perché la sua assenza ha un
/// esito proprio — <c>428 EntityVersionRequired</c> di §10.2 — e non un corpo non deserializzabile.
/// Il proprietario della sessione è quello della credenziale verificata dal trasporto, non un
/// identificativo del payload.
/// </summary>
public sealed record PauseCommand(Guid SessionId, long? ExpectedSessionRevision = null, Guid? ParticipantId = null)
    : ICodiCommand<SessionDto>, ICodiContextualRequest
{
    public static string RequestName => "Pause";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Riprende la sessione da una pausa di classe (§6.1, §6.4, §10.4): la porta da <c>Paused</c> a
/// <c>Active</c>. Gli ingressi restano com'erano.
///
/// In questa porzione esiste il solo ambito <c>Class</c>: il campo <c>target</c> di §6.5 arriva
/// con i comandi ai partecipanti e qui non è previsto.
///
/// <c>ExpectedSessionRevision</c> è la revisione letta dal docente e protegge dai comandi di
/// regia applicati su uno stato superato (§6.4): la sua assenza ha l'esito proprio
/// <c>428 EntityVersionRequired</c> di §10.2.
/// </summary>
public sealed record ResumeCommand(Guid SessionId, long? ExpectedSessionRevision = null, Guid? ParticipantId = null)
    : ICodiCommand<SessionDto>, ICodiContextualRequest
{
    public static string RequestName => "Resume";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Conclude la sessione (§6.1, §6.4, §10.4): la porta a <c>Ended</c> da qualunque stato non
/// terminale, <c>Lobby</c> compreso, e chiude gli ingressi. <c>Ended</c> è terminale e la
/// sessione non si riapre: per una nuova lezione operativa se ne crea e avvia una nuova.
///
/// In questa porzione esiste il solo ambito <c>Class</c>: il campo <c>target</c> di §6.5 arriva
/// con i comandi ai partecipanti e qui non è previsto. La finestra di salvataggio finale (§12.7)
/// non è ancora aperta perché tentativi e grant non esistono: è uno scostamento dichiarato.
///
/// <c>ExpectedSessionRevision</c> è la revisione letta dal docente e protegge dai comandi di
/// regia applicati su uno stato superato (§6.4): la sua assenza ha l'esito proprio
/// <c>428 EntityVersionRequired</c> di §10.2.
/// </summary>
public sealed record EndSessionCommand(Guid SessionId, long? ExpectedSessionRevision = null)
    : ICodiCommand<SessionDto>, ICodiContextualRequest
{
    public static string RequestName => "EndSession";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Elimina definitivamente una sessione conclusa o mai avviata (§6.1, §10.4) insieme ai dati
/// tecnici che le appartengono: partecipanti e loro credenziali, stati desiderati, comandi,
/// consegne, messaggi in coda, assegnazioni, tentativi e permessi conclusivi. Scuola, classe,
/// lezione, esercizio e versioni pubblicate restano: non appartengono alla sessione.
///
/// Una sessione in corso non si elimina, perché la chiusura è un atto distinto e riconoscibile
/// (§6.1): si conclude prima con <c>EndSession</c> e solo dopo si cancella. Una sessione in
/// <c>Lobby</c> non ha alunni al lavoro ed è eliminabile, così una prova istanziata per errore non
/// resta nell'elenco.
///
/// <c>ExpectedSessionRevision</c> è la revisione letta dal docente e protegge dalla cancellazione
/// di una sessione cambiata nel frattempo (§6.4): la sua assenza ha l'esito proprio
/// <c>428 EntityVersionRequired</c> di §10.2. Il proprietario è quello della credenziale verificata
/// dal trasporto, non un identificativo del payload.
/// </summary>
public sealed record DeleteSessionCommand(Guid SessionId, long? ExpectedSessionRevision = null)
    : ICodiCommand<SessionDeletedDto>, ICodiContextualRequest
{
    public static string RequestName => "DeleteSession";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Mostra l'attesa alla classe (§6.4, §6.5, §10.4): sospende il lavoro e riporta la vista a
/// <c>Waiting</c>, senza terminare la sessione e senza perdere il lavoro già svolto. È il primo
/// comando che cambia lo **stato desiderato**: il default di classe vale per i nuovi ingressi,
/// mentre gli stati già materializzati dei partecipanti li aggiorna l'handler nella stessa
/// transazione, così nessun alunno resta con la vista di prima.
///
/// Ammessa da qualunque stato non terminale, <c>Lobby</c> compreso. In questa porzione esiste il
/// solo ambito <c>Class</c>: il campo <c>target</c> di §6.5 arriva con i comandi individuali e qui
/// non è previsto, come per <see cref="PauseCommand"/>.
///
/// Il riferimento al lavoro sospeso (<c>resumeContextJson</c> di §6.2) **non** viene scritto:
/// assegnazioni e tentativi non esistono ancora, quindi non c'è un lavoro da riferire. È uno
/// scostamento dichiarato, non un dettaglio nascosto: arriverà con <c>AssignExercise</c>.
///
/// <c>ExpectedSessionRevision</c> è la revisione letta dal docente e protegge dai comandi di regia
/// applicati su uno stato superato (§6.4): la sua assenza ha l'esito proprio
/// <c>428 EntityVersionRequired</c> di §10.2.
/// </summary>
public sealed record ShowWaitingCommand(Guid SessionId, long? ExpectedSessionRevision = null, Guid? ParticipantId = null)
    : ICodiCommand<CommandAcceptedDto>, ICodiContextualRequest
{
    public static string RequestName => "ShowWaiting";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Apre gli ingressi di una sessione avviata (§6.1, §6.4, §10.4): cambia soltanto
/// <c>admissionsOpen</c> e lascia <c>status</c> invariato, così si possono riaprire gli ingressi
/// durante la lezione. Una sessione già in play con gli ingressi aperti non cambia: ripetere il
/// comando non incrementa la revisione.
///
/// <c>ExpectedSessionRevision</c> è la revisione letta dal docente e protegge dai comandi di regia
/// applicati su uno stato superato (§6.4): la sua assenza ha l'esito proprio
/// <c>428 EntityVersionRequired</c> di §10.2.
/// </summary>
public sealed record OpenAdmissionsCommand(Guid SessionId, long? ExpectedSessionRevision = null)
    : ICodiCommand<SessionDto>, ICodiContextualRequest
{
    public static string RequestName => "OpenAdmissions";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Chiude gli ingressi di una sessione non terminata (§6.1, §6.4, §10.4): come
/// <see cref="OpenAdmissionsCommand"/> cambia soltanto <c>admissionsOpen</c>. Chiudere ingressi già
/// chiusi non cambia nulla e non incrementa la revisione.
///
/// <c>ExpectedSessionRevision</c> è la revisione letta dal docente e protegge dai comandi di regia
/// applicati su uno stato superato (§6.4): la sua assenza ha l'esito proprio
/// <c>428 EntityVersionRequired</c> di §10.2.
/// </summary>
public sealed record CloseAdmissionsCommand(Guid SessionId, long? ExpectedSessionRevision = null)
    : ICodiCommand<SessionDto>, ICodiContextualRequest
{
    public static string RequestName => "CloseAdmissions";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}
