using Microsoft.AspNetCore.Authentication;
using Qwerty.Codi.Api.Dispatch;
using Qwerty.Codi.Api.Hubs;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Admin;
using Qwerty.Codi.Application.Sessions;

namespace Qwerty.Codi.Api.Authentication;

/// <summary>
/// Componenti di autenticazione dell'installazione: impronta delle password, schemi a cookie delle
/// due aree (§9.1, §9.2) e risoluzione del contesto dalla credenziale.
///
/// I due schemi sono separati ma il framework ne autentica uno solo per richiesta, quello predefinito:
/// lo schema di area sceglie in base al percorso, così una credenziale di alunno non autentica mai
/// una richiesta dell'area docente e viceversa (§9.3: nessuna fusione implicita di privilegi).
/// </summary>
internal static class AuthenticationServiceCollectionExtensions
{
    /// <summary>Nome dello schema che smista verso l'area dichiarata dal percorso.</summary>
    private const string AreaSchemeName = "CodiArea";

    /// <summary>Prefisso dell'area pubblica: tutto il resto è area docente.</summary>
    private const string PublicAreaPrefix = "/api/v1/public";

    public static IServiceCollection AddCodiAuthentication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAdminPasswordHasher, AspNetCorePasswordHasher>();

        services
            .AddAuthentication(AreaSchemeName)
            .AddPolicyScheme(AreaSchemeName, "Area della richiesta", options =>
            {
                // Lo schema di area non autentica: sceglie. Il percorso è l'unica cosa che distingue
                // una richiesta dell'alunno da una del docente prima che la credenziale sia letta.
                // I percorsi degli hub di §11.1 seguono la stessa regola dell'area corrispondente:
                // `/hubs/v1/participant` è del partecipante, `/hubs/v1/teacher` del docente.
                options.ForwardDefaultSelector = httpContext =>
                    IsParticipantArea(httpContext.Request.Path)
                        ? ParticipantCookieAuthentication.SchemeName
                        : AdminCookieAuthentication.SchemeName;
            })
            .AddCookie(
                AdminCookieAuthentication.SchemeName,
                options => AdminCookieAuthentication.Configure(options, AdminCredentialPolicy.SessionLifetime))
            .AddCookie(
                ParticipantCookieAuthentication.SchemeName,
                options => ParticipantCookieAuthentication.Configure(options, ParticipantCredentialPolicy.SessionLifetime));

        // Il provider rilegge la credenziale dal database a ogni richiesta, quindi vive quanto
        // l'ambito della richiesta: registrarlo come singleton lo farebbe dipendere da un servizio
        // con durata minore e la risoluzione fallirebbe alla prima richiesta.
        services.AddScoped<ICodiRequestContextProvider, CookieRequestContextProvider>();

        // §10.2: il token antiforgery protegge le mutazioni che viaggiano con il cookie.
        services.AddAntiforgery(CodiAntiforgery.Configure);

        // L'account iniziale si inserisce alla preparazione del database, quando è già pronto
        // (§9.1): l'inserimento usa il dispatcher, quindi vive quanto l'ambito che lo contiene.
        services.AddScoped<AdminAccountSeed>();

        return services;
    }

    /// <summary>
    /// Vero per i percorsi che usano la credenziale dell'alunno: l'area pubblica di §10.1 e l'hub
    /// del partecipante di §11.1. Tutto il resto usa la credenziale docente.
    /// </summary>
    private static bool IsParticipantArea(PathString path) =>
        path.StartsWithSegments(PublicAreaPrefix, StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments(CodiHubs.ParticipantPath, StringComparison.OrdinalIgnoreCase);
}
