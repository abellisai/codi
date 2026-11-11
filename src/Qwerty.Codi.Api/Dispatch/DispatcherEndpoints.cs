using System.Text.Json;
using Qwerty.Codi.Api.Endpoints;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Api.Dispatch;

/// <summary>
/// I due endpoint generici di §10.1. Il trasporto si limita a leggere il corpo, a chiedere il
/// contesto e a restituire l'esito: nessuna regola applicativa, nessuna rotta per singolo caso
/// d'uso. Gli errori previsti diventano Problem Details nel gestore dedicato.
/// </summary>
internal static class DispatcherEndpoints
{
    private const string CommandRoute = "/execute/command/{commandName}";
    private const string QueryRoute = "/execute/query/{queryName}";

    public static IEndpointRouteBuilder MapCodiDispatcher(this IEndpointRouteBuilder endpoints, CodiHttpRequestArea area)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(CommandRoute, (
            string commandName,
            HttpContext httpContext,
            CodiRequestExecutor executor,
            CodiRequestRegistry registry,
            ICodiRequestContextProvider contextProvider,
            ILoggerFactory loggerFactory) =>
            ExecuteAsync(
                CodiRequestKind.Command,
                area,
                commandName,
                httpContext,
                executor,
                registry,
                contextProvider,
                loggerFactory));

        endpoints.MapPost(QueryRoute, (
            string queryName,
            HttpContext httpContext,
            CodiRequestExecutor executor,
            CodiRequestRegistry registry,
            ICodiRequestContextProvider contextProvider,
            ILoggerFactory loggerFactory) =>
            ExecuteAsync(
                CodiRequestKind.Query,
                area,
                queryName,
                httpContext,
                executor,
                registry,
                contextProvider,
                loggerFactory));

        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync(
        CodiRequestKind kind,
        CodiHttpRequestArea area,
        string requestName,
        HttpContext httpContext,
        CodiRequestExecutor executor,
        CodiRequestRegistry registry,
        ICodiRequestContextProvider contextProvider,
        ILoggerFactory loggerFactory)
    {
        var cancellationToken = httpContext.RequestAborted;

        // Un nome fuori area è assente dal catalogo HTTP, anche con un cookie admin valido.
        // L'esecutore conserva poi i propri controlli di canale e policy prima dell'handler.
        var descriptor = registry.Find(kind, requestName);
        if (descriptor is null || !area.Allows(descriptor))
        {
            throw CodiRequestException.NotFound(kind, requestName);
        }

        // §18.4: log strutturati con il nome della richiesta, il canale, l'esito e il traceId. Il
        // corpo non viene registrato: può contenere dati di persone e contenuti dei progetti.
        var logger = loggerFactory.CreateLogger("Qwerty.Codi.Api.Dispatch");

        // Il documento resta vivo fino a fine esecuzione: JsonElement non possiede i propri byte.
        using var document = await ReadPayloadAsync(httpContext.Request, requestName, cancellationToken)
            .ConfigureAwait(false);

        var context = await contextProvider
            .ResolveRequestContextAsync(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (kind == CodiRequestKind.Command && descriptor.Policy == CodiPolicy.SessionController &&
            document.RootElement.TryGetProperty("sessionId", out _) &&
            (string.IsNullOrWhiteSpace(context.IdempotencyKey) || context.IdempotencyKey.Length > 128))
        {
            throw CodiRequestException.InvalidPayload(requestName);
        }

        object? result;

        try
        {
            result = await executor
                .ExecuteAsync(kind, requestName, CodiRequestChannel.Http, document.RootElement, context, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CodiRequestException exception)
        {
            logger.LogWarning(
                "Richiesta {RequestKind} {RequestName} rifiutata con codice {ErrorCode}. TraceId {TraceId}.",
                kind,
                requestName,
                exception.Code,
                httpContext.TraceIdentifier);

            // L'errore resta un errore applicativo previsto: lo traduce il gestore delle eccezioni.
            throw;
        }

        logger.LogInformation(
            "Richiesta {RequestKind} {RequestName} completata sul canale {Channel}. TraceId {TraceId}.",
            kind,
            requestName,
            CodiRequestChannel.Http,
            httpContext.TraceIdentifier);

        // Le opzioni di serializzazione sono le stesse dei corpi in ingresso: il formato dei
        // messaggi del dispatcher è un contratto solo (§10.2), non due. §10.1: un comando registrato
        // per essere consegnato risponde 202 con lo stato delle consegne; un comando senza
        // distribuzione e una query rispondono 200.
        var statusCode = result is ICodiAcceptedOutcome
            ? StatusCodes.Status202Accepted
            : StatusCodes.Status200OK;

        return Results.Json(result, CodiJson.Payload, statusCode: statusCode);
    }

    /// <summary>
    /// Legge il corpo come oggetto JSON. Un corpo assente equivale a un oggetto vuoto: una query
    /// senza parametri è una richiesta legittima, mentre un corpo non oggetto o malformato è un
    /// client non allineato al record (§10.1).
    /// </summary>
    internal static async Task<JsonDocument> ReadPayloadAsync(
        HttpRequest request,
        string requestName,
        CancellationToken cancellationToken)
    {
        const int maxBytes = 2 * 1024 * 1024;
        if (request.ContentLength > maxBytes)
            throw new CodiRequestException(CodiErrorCodes.PayloadTooLarge, "Il corpo della richiesta supera 2 MiB.");
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await request.Body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + count > maxBytes)
                throw new CodiRequestException(CodiErrorCodes.PayloadTooLarge, "Il corpo della richiesta supera 2 MiB.");
            buffer.Write(chunk, 0, count);
        }
        var body = System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        if (string.IsNullOrWhiteSpace(body))
        {
            return JsonDocument.Parse("{}");
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw CodiRequestException.InvalidPayload(requestName, exception);
        }
    }
}
