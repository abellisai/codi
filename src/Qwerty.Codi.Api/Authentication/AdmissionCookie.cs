using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Qwerty.Codi.Application.Sessions;

namespace Qwerty.Codi.Api.Authentication;

/// <summary>
/// Ticket di ammissione (§9.4): un cookie temporaneo **privo di privilegi**, emesso dal bootstrap
/// pubblico e usato come scope dell'ingresso idempotente. Non è una credenziale e non identifica
/// nessuno: senza di esso, però, un ingresso ripetuto non avrebbe a chi essere attribuito, perché
/// prima del join non esiste ancora alcuna identità.
///
/// Il valore è protetto dalla Data Protection del processo, quindi il browser non può fabbricarne
/// uno né leggerne il contenuto; il registro di idempotenza conserva l'**impronta** dello scope e
/// mai il ticket (§7.5).
/// </summary>
internal static class AdmissionCookie
{
    public const string CookieName = "__Host-CodiAdmission";

    /// <summary>Limite di lunghezza dello scope: uno scope più lungo non è un ticket nostro.</summary>
    private const int AdmissionScopeMaxLength = 128;

    /// <summary>
    /// Scopo della protezione: cambiarlo invalida i ticket emessi in precedenza, ed è la ragione per
    /// cui porta una versione.
    /// </summary>
    private const string Purpose = "Qwerty.Codi.Admission.v1";

    /// <summary>
    /// Durata del ticket. Non è la durata della lezione: serve a coprire il percorso dalla
    /// scoperta all'ingresso, e un ingresso ripetuto dopo un'ora passa dalla sessione ricevuta.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);

    /// <summary>Emette il ticket se il browser non ne ha uno utilizzabile e restituisce lo scope.</summary>
    public static string EnsureScope(HttpContext context, IDataProtectionProvider dataProtectionProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);

        if (ReadScope(context, dataProtectionProvider) is { } existing)
        {
            return existing;
        }

        var scope = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        context.Response.Cookies.Append(CookieName, Protect(dataProtectionProvider, scope), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.Add(Lifetime),
        });

        return scope;
    }

    /// <summary>
    /// Scope corrente, se il browser presenta un ticket leggibile. Un ticket manomesso o emesso da
    /// un'altra installazione vale come assente: non è un errore da segnalare, è un browser che deve
    /// ricominciare dalla scoperta.
    /// </summary>
    public static string? ReadScope(HttpContext context, IDataProtectionProvider dataProtectionProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);

        var ticket = context.Request.Cookies[CookieName];
        if (string.IsNullOrEmpty(ticket))
        {
            return null;
        }

        try
        {
            var scope = Unprotect(dataProtectionProvider, ticket);

            return string.IsNullOrWhiteSpace(scope) || scope.Length > AdmissionScopeMaxLength ? null : scope;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>
    /// Il ticket ha finito il suo compito: la credenziale emessa dall'ingresso lo sostituisce, e
    /// lasciarlo nel browser significherebbe conservare un secondo scope per un ingresso già fatto.
    /// </summary>
    public static void Clear(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            Secure = true,
            Path = "/",
            SameSite = SameSiteMode.Strict,
        });
    }

    private static string Protect(IDataProtectionProvider provider, string scope) =>
        provider.CreateProtector(Purpose).Protect(scope);

    private static string Unprotect(IDataProtectionProvider provider, string ticket) =>
        provider.CreateProtector(Purpose).Unprotect(ticket);
}
