using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Api.Dispatch;

/// <summary>
/// Gestore delle eccezioni di §4.4: converte gli errori applicativi tipizzati in Problem Details
/// con codice stabile (§10.7). Gli altri errori restano al gestore predefinito: un guasto
/// imprevisto non deve diventare una risposta di errore prevista e riconoscibile.
/// </summary>
internal sealed class CodiRequestExceptionHandler(ILogger<CodiRequestExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not CodiRequestException requestException)
        {
            return false;
        }

        var status = CodiErrorStatus.Resolve(requestException.Code);

        // Log strutturato senza payload, credenziali o nomi di alunni (§20.2).
        logger.LogInformation(
            "Richiesta rifiutata: codice {ErrorCode}, stato {StatusCode}, percorso {RequestPath}, correlazione {TraceId}.",
            requestException.Code,
            status,
            httpContext.Request.Path.Value,
            httpContext.TraceIdentifier);

        httpContext.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = CodiErrorStatus.Title(requestException.Code),
            Detail = requestException.Message,
            Instance = httpContext.Request.Path.Value,
        };

        // Il codice applicativo e la correlazione sono parte del contratto di §10.2; il testo di
        // `detail` è per chi legge, non per il codice del client.
        problem.Extensions["code"] = requestException.Code;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken).ConfigureAwait(false);

        return true;
    }
}
