using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Qwerty.Codi.Api.Contracts;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Api.Authentication;

/// <summary>
/// Antiforgery delle mutazioni autenticate con cookie (§10.2, §17.2). Il cookie che porta il
/// request token <b>non</b> è il cookie di autenticazione: quello resta <c>HttpOnly</c> e non è
/// leggibile dal frontend, questo deve esserlo perché il client lo copi nell'intestazione.
/// </summary>
internal static class CodiAntiforgery
{
    /// <summary>Intestazione che il client usa per rimandare il request token.</summary>
    public const string HeaderName = "X-Codi-Antiforgery";

    /// <summary>Nome del cookie del request token, leggibile dal frontend.</summary>
    public const string CookieName = "__Host-CodiAntiforgery";

    public const string PublicCookieName = "__Host-CodiPublicAntiforgery";

    public static string RequestCookieName(string path) =>
        path.StartsWith("/api/v1/public", StringComparison.Ordinal) ||
        path.StartsWith("/hubs/v1/participant", StringComparison.Ordinal) ? PublicCookieName : CookieName;

    /// <summary>Cookie interno del framework, distinto dal request token e non leggibile da JS.</summary>
    public const string InternalCookieName = "__Host-CodiAntiforgeryCookie";

    public static void Configure(AntiforgeryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.HeaderName = HeaderName;
        options.Cookie.Name = InternalCookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
        options.Cookie.IsEssential = true;
    }

    /// <summary>Emette la coppia di token per l'identità corrente, anche dopo un cambio di login.</summary>
    public static void IssueRequestToken(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Cookies.Append(RequestCookieName(context.Request.Path.Value ?? ""), tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true,
        });
    }

    /// <summary>
    /// Bootstrap dell'area pubblica e di quella amministrativa. Il primo emette anche il ticket di
    /// ammissione (§9.4): è il momento in cui un browser senza identità riceve lo scope con cui
    /// l'ingresso sarà reso idempotente.
    /// </summary>
    public static IEndpointRouteBuilder MapCodiAntiforgeryBootstrap(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/bootstrap", (
            HttpContext context,
            IAntiforgery antiforgery,
            IDataProtectionProvider dataProtectionProvider) =>
        {
            IssueRequestToken(context, antiforgery);
            AdmissionCookie.EnsureScope(context, dataProtectionProvider);

            return Results.Ok(new AntiforgeryBootstrapResponse(
                RequestCookieName(context.Request.Path.Value ?? ""),
                HeaderName));
        });
        return endpoints;
    }
}

/// <summary>
/// Verifica il token sulle mutazioni che viaggiano con il cookie di autenticazione, dispatcher
/// compreso (§10.2). L'esito negativo è un errore applicativo previsto, non un rifiuto opaco: si
/// presenta come Problem Details con codice stabile.
/// </summary>
internal sealed class CodiAntiforgeryValidationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (!RequiresValidation(context))
        {
            await next(context);

            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException exception)
        {
            throw CodiRequestException.AntiforgeryRejected(exception);
        }

        await next(context);
    }

    /// <summary>
    /// Il token serve solo alle mutazioni che portano le credenziali della vittima: una lettura non
    /// cambia stato, e senza credenziale valida non c'è nessuna richiesta a nome altrui da
    /// impedire — l'assenza di contesto la segnala già la policy della richiesta.
    /// </summary>
    internal static bool RequiresValidation(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var method = context.Request.Method;
        if (HttpMethods.IsGet(method) ||
            HttpMethods.IsHead(method) ||
            HttpMethods.IsOptions(method) ||
            HttpMethods.IsTrace(method))
        {
            return false;
        }

        return context.User.Identity?.IsAuthenticated == true;
    }
}

internal static class CodiAntiforgeryApplicationExtensions
{
    /// <summary>
    /// Va inserito dopo <c>UseAuthentication</c>: la decisione dipende dall'identità già risolta
    /// e prima degli endpoint, perché il controllo non appartiene a una singola rotta.
    /// </summary>
    public static IApplicationBuilder UseCodiAntiforgeryValidation(this IApplicationBuilder application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return application.UseMiddleware<CodiAntiforgeryValidationMiddleware>();
    }
}
