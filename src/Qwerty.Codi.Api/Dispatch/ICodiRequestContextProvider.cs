using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Api.Dispatch;

/// <summary>
/// Costruisce il contesto autorizzativo di una richiesta a partire dal trasporto: identità,
/// concessioni e ambito non arrivano mai dal payload (§4.4, §9.3). È l'unico punto in cui il
/// trasporto dichiara chi sta chiamando, ed è asincrono perché una credenziale va verificata
/// contro il database, non solo contro il cookie.
/// </summary>
internal interface ICodiRequestContextProvider
{
    Task<CodiRequestContext> ResolveRequestContextAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken);
}
