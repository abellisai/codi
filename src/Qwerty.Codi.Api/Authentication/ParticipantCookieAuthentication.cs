using Microsoft.AspNetCore.Authentication.Cookies;

namespace Qwerty.Codi.Api.Authentication;

/// <summary>
/// Schema di autenticazione dei partecipanti (§9.2): cookie separato da quello amministrativo, con
/// gli stessi attributi di protezione e una durata più corta. La separazione non è formale: i due
/// cookie possono convivere nello stesso browser — un docente che prova un ingresso dal proprio PC —
/// e la policy di una richiesta deve dipendere da una sola credenziale, mai dalla fusione di due.
/// </summary>
internal static class ParticipantCookieAuthentication
{
    /// <summary>Nome dello schema registrato nella composizione.</summary>
    public const string SchemeName = "CodiParticipant";

    /// <summary>
    /// Nome del cookie. Come per l'area amministrativa il prefisso <c>__Host-</c> impone al browser
    /// <c>Secure</c>, <c>Path=/</c> e nessun <c>Domain</c>: su HTTP in chiaro il cookie non viene
    /// conservato, quindi l'aula live richiede HTTPS o un proxy che lo termini (T9).
    /// </summary>
    public const string CookieName = "__Host-CodiParticipant";

    public static void Configure(CookieAuthenticationOptions options, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Cookie.Name = CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
        options.ExpireTimeSpan = lifetime;

        // Scadenza assoluta: la credenziale dell'alunno non si rinnova da sola mentre la usa (§9.2).
        options.SlidingExpiration = false;

        // Un'API non reindirizza: senza credenziale risponde 401 e il client decide cosa mostrare.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;

            return Task.CompletedTask;
        };
    }
}
