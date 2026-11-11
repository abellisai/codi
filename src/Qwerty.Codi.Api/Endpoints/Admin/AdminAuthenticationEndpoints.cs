using Qwerty.Codi.Application.Dto;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Qwerty.Codi.Api.Authentication;
using Qwerty.Codi.Api.Contracts;
using Qwerty.Codi.Api.Dispatch;
using Qwerty.Codi.Application.Admin;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Api.Endpoints.Admin;

/// <summary>
/// I due endpoint dedicati dell'accesso amministrativo (§10.3): emettono e revocano il cookie,
/// cosa che il dispatcher non può fare perché qui il trasporto fa parte del contratto. Non
/// contengono regole applicative: delegano ai comandi e traducono l'esito nel trasporto.
/// </summary>
internal static class AdminAuthenticationEndpoints
{
    private const string LoginRoute = "/auth/login";
    private const string LogoutRoute = "/auth/logout";
    private const string MeRoute = "/auth/me";

    public static IEndpointRouteBuilder MapCodiAdminAuthentication(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(LoginRoute, SignInAsync);
        endpoints.MapPost(LogoutRoute, SignOutAsync);
        endpoints.MapGet(MeRoute, CurrentContextAsync);

        return endpoints;
    }

    /// <summary>
    /// Profilo minimo del contesto corrente (§10.3). Non si limita a leggere i claim: rilegge la
    /// credenziale come fa il dispatcher, così una sessione revocata non risulta più valida solo
    /// perché il cookie è ancora nel browser (§9.2).
    /// </summary>
    private static async Task<IResult> CurrentContextAsync(
        HttpContext httpContext,
        ICodiRequestContextProvider contextProvider,
        CancellationToken cancellationToken)
    {
        var context = await contextProvider
            .ResolveRequestContextAsync(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (!context.Allows(CodiPolicy.Admin))
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new AdminProfileResponse(
            httpContext.User.Identity?.Name ?? string.Empty,
            [nameof(CodiPolicy.Admin)]));
    }

    private static async Task<IResult> SignInAsync(
        HttpContext httpContext,
        CodiRequestExecutor executor,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        using var payload = await DispatcherEndpoints
            .ReadPayloadAsync(httpContext.Request, LoginAdminCommand.RequestName, cancellationToken)
            .ConfigureAwait(false);

        // L'accesso è l'operazione di bootstrap per definizione: se un amministratore è già
        // autenticato e ripete l'accesso, il contesto della richiesta non deve impedirlo.
        var result = await executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                LoginAdminCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                CodiRequestContext.Anonymous,
                cancellationToken)
            .ConfigureAwait(false);

        if (result is not AdminSignedInDto signedIn)
        {
            // Un esito di tipo diverso è un difetto di programmazione, non un errore del client.
            throw new InvalidOperationException(
                "L'accesso amministrativo non ha restituito la credenziale emessa.");
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, signedIn.Username),
                new Claim(CodiClaimTypes.CredentialSessionId, signedIn.CredentialSessionId.ToString("D")),
                new Claim(CodiClaimTypes.CredentialStamp, signedIn.SecurityStamp),
            ],
            AdminCookieAuthentication.SchemeName);

        var principal = new ClaimsPrincipal(identity);
        await httpContext
            .SignInAsync(AdminCookieAuthentication.SchemeName, principal)
            .ConfigureAwait(false);

        // SignInAsync scrive il ticket nella risposta, ma non cambia User su questa richiesta.
        // Il nuovo request token deve essere legato all'identità appena autenticata.
        httpContext.User = principal;
        CodiAntiforgery.IssueRequestToken(httpContext, antiforgery);

        // Profilo minimo: la marca di sicurezza resta dentro il cookie protetto e non compare
        // nella risposta, che il client può registrare o mostrare.
        return Results.Ok(new AdminSignInResponse(signedIn.Username));
    }

    private static async Task<IResult> SignOutAsync(
        HttpContext httpContext,
        CodiRequestExecutor executor,
        ICodiRequestContextProvider contextProvider,
        CancellationToken cancellationToken)
    {
        var context = await contextProvider
            .ResolveRequestContextAsync(httpContext, cancellationToken)
            .ConfigureAwait(false);

        using var payload = JsonDocument.Parse("{}");

        // La revoca passa dal comando, che richiede la policy Admin: senza credenziale valida la
        // richiesta riceve 401, come prescrive §10.3.
        await executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                LogoutAdminCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                context,
                cancellationToken)
            .ConfigureAwait(false);

        await httpContext.SignOutAsync(AdminCookieAuthentication.SchemeName).ConfigureAwait(false);
        httpContext.Response.Cookies.Delete(CodiAntiforgery.CookieName, new CookieOptions
        {
            Secure = true,
            Path = "/",
            SameSite = SameSiteMode.Strict,
        });

        return Results.NoContent();
    }
}
