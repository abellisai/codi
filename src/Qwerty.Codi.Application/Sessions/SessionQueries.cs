using Qwerty.Codi.Application.Dto;
using System.Text.Json.Serialization;
using Mediator;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Elenco delle sessioni per la pagina admin delle sessioni (§10.5), dalla più recente.
///
/// La query ha policy <see cref="CodiPolicy.Admin"/> e restituisce un solo DTO per sessione con i
/// dati che la pagina deve mostrare; non espone la versione di riga. Il nome pubblico è nuovo
/// rispetto a <see cref="GetSessionQuery"/>, che è già catalogato.
/// </summary>
public sealed record ListSessionsQuery : ICodiQuery<IReadOnlyList<SessionSummaryDto>>
{
    public static string RequestName => "ListSessions";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>
/// Dettaglio di una sessione per la pagina admin (§10.5), con gli stessi dati dell'elenco. Ha
/// policy <see cref="CodiPolicy.SessionController"/>: l'identità si ricava dalla credenziale e la
/// proprietà è verificata nell'handler, perché la sessione di un altro amministratore non è
/// leggibile. La richiesta porta il contesto verificato dal trasporto, non dal payload (§4.4).
/// </summary>
public sealed record GetSessionQuery(Guid SessionId) : ICodiQuery<SessionSummaryDto>, ICodiContextualRequest
{
    public static string RequestName => "GetSession";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Scuole con ingressi disponibili (§10.3): solo quelle non archiviate che hanno almeno una classe
/// con una sessione in play. È una richiesta di sola lettura, invocata per tipo dall'endpoint
/// dedicato della scoperta pubblica, che precede il join.
///
/// Non è una richiesta del catalogo del dispatcher: prima del join non esiste un contesto
/// autenticato, quindi nessuna policy di §9.3 sarebbe applicabile. Implementa soltanto
/// <see cref="IQuery{TResponse}"/> di Mediator e non <c>ICodiQuery</c>: il registro di §4.4
/// riconosce le richieste dall'interfaccia <c>IRequest&lt;&gt;</c>, e <c>IQuery&lt;&gt;</c> non
/// deriva da essa, quindi questa richiesta gli resta invisibile e non entra nel catalogo.
/// </summary>
public sealed record ListAvailableSchoolsQuery : IQuery<IReadOnlyList<AvailableSchoolDto>>;

/// <summary>
/// Classi accessibili di una scuola (§10.3): solo quelle non archiviate che hanno almeno una
/// sessione in play. Richiesta di sola lettura della scoperta pubblica, invocata per tipo
/// dall'endpoint dedicato; resta fuori dal catalogo del dispatcher perché precede il join e non ha
/// un contesto autenticato a cui applicare una policy (§9.3).
///
/// Una scuola inesistente e una scuola senza classi accessibili producono lo stesso esito — elenco
/// vuoto — perché la scoperta è una ricerca, non la lettura di una risorsa: distinguerle rivelerebbe
/// quali identificativi esistono e obbligherebbe la pagina a un caso in più. Il caso dello stato
/// vuoto lo decide l'handler, non il trasporto.
/// </summary>
public sealed record ListAvailableClassroomsQuery(Guid SchoolId) : IQuery<IReadOnlyList<AvailableClassroomDto>>;

/// <summary>
/// La sessione in play di una classe, con la lezione e il corso (§10.3). Richiesta di sola lettura
/// della scoperta pubblica, invocata per tipo dall'endpoint dedicato; resta fuori dal catalogo del
/// dispatcher perché precede il join e non ha un contesto autenticato a cui applicare una policy (§9.3).
///
/// Se la classe non esiste, è archiviata, o non ha una sessione in play, l'handler produce
/// <c>404 ResourceNotFound</c> con il codice del catalogo: qui la risorsa è singolare — «la» sessione
/// in play — e la sua assenza è un «non trovato», non un elenco vuoto. È coerente con le altre
/// letture per identificativo, che non distinguono «non esiste» da «non accessibile».
/// </summary>
public sealed record GetClassroomPlaySessionQuery(Guid ClassroomId) : IQuery<PlaySessionDto>;

/// <summary>
/// C'è una lezione in play in questa installazione? (§10.3). La home dell'alunno la interroga per
/// sapere se deve chiedere il codice: quando nessuna sessione è avviata con gli ingressi aperti,
/// chiedere un codice sarebbe chiedere una cosa che non esiste.
///
/// La risposta è un solo booleano: la pagina non deve sapere **quale** lezione è in corso né in
/// quale classe — per raggiungerla serve il codice — quindi non si restituiscono né identificativi
/// né titoli. Richiesta di sola lettura della scoperta pubblica, invocata per tipo dall'endpoint
/// dedicato e fuori dal catalogo del dispatcher perché precede il join (§9.3).
/// </summary>
public sealed record IsLessonInPlayQuery : IQuery<LessonInPlayDto>;
