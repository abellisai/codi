using Microsoft.AspNetCore.Authentication.Cookies;

namespace Qwerty.Codi.Api.Authentication;

/// <summary>
/// Schema di autenticazione dell'area amministrativa (§9.1): cookie con scadenza assoluta, non
/// rinnovato dall'attività.
/// </summary>
internal static class AdminCookieAuthentication
{
    /// <summary>Nome dello schema registrato nella composizione.</summary>
    public const string SchemeName = "CodiAdmin";

    /// <summary>
    /// Nome del cookie. Il prefisso <c>__Host-</c> non è decorativo: il browser lo accetta solo se
    /// il cookie è <c>Secure</c>, ha <c>Path=/</c> e non ha <c>Domain</c>, quindi nessun
    /// sottodominio può sovrascriverlo. Conseguenza pratica da tenere presente in laboratorio: su
    /// HTTP in chiaro il browser non lo conserva, quindi l'area amministrativa richiede HTTPS o un
    /// proxy che lo termini (T9).
    /// </summary>
    public const string CookieName = "__Host-CodiAdmin";

    public static void Configure(CookieAuthenticationOptions options, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Cookie.Name = CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
        options.ExpireTimeSpan = lifetime;

        // Scadenza assoluta: una sessione non si rinnova da sola finché la si usa (§9.1).
        options.SlidingExpiration = false;

        // Un'API non reindirizza a una pagina di accesso: senza credenziale risponde 401, e cosa
        // mostrare è una decisione del client.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;

            return Task.CompletedTask;
        };
    }
}
