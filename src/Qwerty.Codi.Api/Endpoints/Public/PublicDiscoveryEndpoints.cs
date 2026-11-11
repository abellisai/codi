using Mediator;
using Qwerty.Codi.Application.Sessions;

namespace Qwerty.Codi.Api.Endpoints.Public;

/// <summary>
/// Endpoint dedicati della scoperta pubblica (§10.3): la home dell'alunno scopre se c'è una lezione
/// in play e, se serve, la catena scuola → classe → sessione che porta alla lezione della classe.
/// Precedono il join, quindi non passano dal dispatcher — che richiede una policy di §9.3,
/// inapplicabile quando non esiste ancora un contesto autenticato — e invocano per tipo le query
/// applicative.
///
/// Sono anonimi e di sola lettura: nessun antiforgery su un <c>GET</c> e nessuna scrittura. Nessuna
/// regola applicativa vive qui: la selezione «in play» e i filtri di archiviazione stanno nei casi
/// d'uso, e il trasporto si limita a delegare e a restituire l'esito.
/// </summary>
internal static class PublicDiscoveryEndpoints
{
    public static IEndpointRouteBuilder MapCodiPublicDiscovery(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/schools", ListAvailableSchoolsAsync);
        endpoints.MapGet("/schools/{id:guid}/classrooms", ListAvailableClassroomsAsync);
        endpoints.MapGet("/classrooms/{id:guid}/sessions", GetPlaySessionAsync);
        endpoints.MapGet("/in-play", GetLessonInPlayAsync);

        return endpoints;
    }

    private static async Task<IResult> ListAvailableSchoolsAsync(
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var schools = await mediator
            .Send(new ListAvailableSchoolsQuery(), cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(schools);
    }

    private static async Task<IResult> ListAvailableClassroomsAsync(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        // Una scuola inesistente e una scuola senza classi accessibili danno lo stesso esito —
        // elenco vuoto — perché la scoperta è una ricerca, non la lettura di una risorsa: l'elenco
        // vuoto è una risposta legittima e non rivela quali identificativi esistono.
        var classrooms = await mediator
            .Send(new ListAvailableClassroomsQuery(id), cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(classrooms);
    }

    private static async Task<IResult> GetPlaySessionAsync(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        // Qui la risorsa è singolare: «la» sessione in play della classe. Se non c'è, non c'è nulla
        // da mostrare né da joinare, quindi l'handler produce 404 con il codice del catalogo
        // (ResourceNotFound), coerente con le altre letture per identificativo; l'elenco vuoto è
        // riservato alle due collezioni precedenti. L'errore previsto diventa Problem Details nel
        // gestore delle eccezioni, come per il dispatcher.
        var session = await mediator
            .Send(new GetClassroomPlaySessionQuery(id), cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(session);
    }

    /// <summary>
    /// Presenza di una lezione in play (§10.3): è la lettura che permette alla home dell'alunno di
    /// accorgersi che il maestro ha avviato la lezione. Risponde con il solo booleano — quale
    /// lezione sia, e in quale classe, non si dice: per raggiungerla serve il codice, che il maestro
    /// detta a voce. Anche qui il trasporto si limita a delegare.
    /// </summary>
    private static async Task<IResult> GetLessonInPlayAsync(
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var presence = await mediator
            .Send(new IsLessonInPlayQuery(), cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(presence);
    }
}
