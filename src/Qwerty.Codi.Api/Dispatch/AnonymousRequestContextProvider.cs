using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Api.Dispatch;

/// <summary>
/// Contesto sempre anonimo: era la risposta corretta finché nessuno schema di autenticazione era
/// registrato. Ora <c>CookieRequestContextProvider</c> restituisce il contesto anonimo quando la
/// credenziale manca o non è più utilizzabile, quindi questa implementazione non è più registrata
/// e va rimossa: resta compilabile solo per non rompere la build se la cancellazione arriva dopo.
/// </summary>
internal sealed class AnonymousRequestContextProvider : ICodiRequestContextProvider
{
    public Task<CodiRequestContext> ResolveRequestContextAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        Task.FromResult(CodiRequestContext.Anonymous);
}
