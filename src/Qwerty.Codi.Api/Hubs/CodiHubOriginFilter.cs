using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Qwerty.Codi.Api.Hubs;

/// <summary>
/// Verifica dell'origin della connessione (§11.2 passo 2). L'app non ha CORS e i cookie sono
/// <c>SameSite=Strict</c>, quindi il browser non invia la credenziale su una richiesta cross-site:
/// questo controllo è la seconda difesa, quella che §11.2 chiede esplicitamente e che non dipende dal
/// comportamento di un browser.
///
/// Il filtro copre la connessione e ogni metodo: una connessione già aperta non diventa una via di
/// accesso se l'origin cambia, e un metodo invocato da un client che non ha superato la verifica non
/// viene eseguito. L'<c>Origin</c> assente non è un rifiuto: i client che non sono browser non lo
/// inviano, e per loro la credenziale cookie non esiste.
/// </summary>
internal sealed class CodiHubOriginFilter(
    IOptions<CodiRealtimeOptions> options,
    ILogger<CodiHubOriginFilter> logger) : IHubFilter
{
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        EnsureAllowed(context.Context);

        await next(context).ConfigureAwait(false);
    }

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        EnsureAllowed(invocationContext.Context);

        return await next(invocationContext).ConfigureAwait(false);
    }

    private void EnsureAllowed(HubCallerContext context)
    {
        var httpContext = context.GetHttpContext();
        if (httpContext is null)
        {
            return;
        }

        var origin = httpContext.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin) || IsAllowed(origin, httpContext))
        {
            return;
        }

        logger.LogWarning("Connessione hub rifiutata: origin {Origin} non ammesso.", origin);
        context.Abort();

        throw new HubException("Connessione rifiutata: l'origin non è fra quelli ammessi.");
    }

    /// <summary>
    /// Un origin è ammesso se è dichiarato nella configurazione o se è quello del server stesso:
    /// confrontare le stringhe normalizzate evita che una barra finale o una lettera maiuscola
    /// cambino l'esito, e il confronto è per intero — non per prefisso — perché
    /// <c>https://esempio.it.ostile.test</c> non deve passare per <c>https://esempio.it</c>.
    /// </summary>
    private bool IsAllowed(string origin, HttpContext httpContext)
    {
        var request = httpContext.Request;
        var sameOrigin = $"{request.Scheme}://{request.Host.Value}";

        return Matches(origin, sameOrigin) ||
               options.Value.AllowedOrigins.Any(allowed => Matches(origin, allowed));
    }

    private static bool Matches(string origin, string allowed) =>
        !string.IsNullOrWhiteSpace(allowed) &&
        string.Equals(
            origin.Trim().TrimEnd('/'),
            allowed.Trim().TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);
}
