using Microsoft.Extensions.DependencyInjection;

namespace Qwerty.Codi.Application.Dispatch;

/// <summary>
/// Composizione del dispatcher nella parte applicativa: mediator con gli handler di questo
/// assembly, registro delle richieste ed esecutore. Sta qui e non nel progetto API perché la
/// registrazione degli handler è generata nel progetto che contiene record e handler: chiamarla
/// dove vivono gli handler non dipende da come il generatore decide di esporla.
/// </summary>
public static class CodiDispatchServiceCollectionExtensions
{
    /// <summary>
    /// Aggiunge mediator, registro ed esecutore. Il registro si costruisce alla prima
    /// risoluzione, cioè all'avvio del servizio: un catalogo ambiguo impedisce di partire invece
    /// di emergere alla prima richiesta.
    /// </summary>
    public static IServiceCollection AddCodiRequests(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Mediator costruisce la propria cache dei servizi alla prima invocazione e vi risolve
        // **tutti** gli handler registrati, non solo quello della richiesta in corso: se manca la
        // dipendenza di un handler qualsiasi, nessuna richiesta funziona e l'errore nomina un
        // handler che non c'entra. Chi compone il servizio deve quindi registrare le dipendenze
        // dell'intero catalogo, e aggiungere un handler con una dipendenza nuova può richiedere
        // di completare composizioni all'apparenza estranee a quella modifica.
        //
        // Mediator registra mediator e handler come **Singleton** se non si dice altro, mentre i
        // repository hanno durata limitata all'operazione (§4.3): un handler Singleton che li
        // consuma è una configurazione che non regge, e la validazione delle dipendenze la rifiuta
        // all'avvio. Mediator e handler seguono quindi l'ambito della richiesta, come l'esecutore
        // che li invoca.
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);

        services.AddSingleton(_ => CodiRequestRegistry.FromAssemblies(typeof(ICodiRequest).Assembly));

        // L'esecutore vive nell'ambito della richiesta: risolve il mediator dallo stesso
        // contenitore, quindi gli handler conservano la durata loro configurata.
        services.AddScoped<CodiRequestExecutor>();
        services.AddScoped<Qwerty.Codi.Application.Sessions.AttemptSaveService>();
        services.AddScoped<Qwerty.Codi.Application.Sessions.SessionStartupRecovery>();
        services.AddScoped<Qwerty.Codi.Application.Sessions.FinalizationCleanup>();

        return services;
    }
}
