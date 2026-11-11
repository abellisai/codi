using Microsoft.AspNetCore.SignalR;
using Qwerty.Codi.Api.BackgroundJobs;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Sessions;

namespace Qwerty.Codi.Api.Hubs;

/// <summary>
/// Composizione del trasporto realtime (§11.2, §11.5, §11.7). I limiti sono configurati esplicitamente
/// e non lasciati ai valori predefiniti: sono contratti del protocollo, e un valore implicito cambia
/// senza che nessuno lo decida.
/// </summary>
internal static class CodiRealtimeServiceCollectionExtensions
{
    /// <summary>Dimensione massima di un messaggio SignalR in ingresso (§11.5).</summary>
    private const int MaximumMessageSizeBytes = 32 * 1024;

    public static IServiceCollection AddCodiRealtime(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Gli hub dichiarano `[Authorize]`: la middleware di autorizzazione deve poter risolvere i
        // servizi, anche se gli endpoint del dispatcher non usano `RequireAuthorization`.
        services.AddAuthorization();

        // Gli origin ammessi si configurano (§11.2 passo 2): in sviluppo il browser parla con
        // un'altra porta, in produzione con lo stesso origin del server.
        services.AddOptions<CodiRealtimeOptions>().BindConfiguration(CodiRealtimeOptions.SectionName);

        services
            .AddSignalR(options =>
            {
                options.MaximumReceiveMessageSize = MaximumMessageSizeBytes;
                options.KeepAliveInterval = TimeSpan.FromSeconds(5);
                options.ClientTimeoutInterval = TimeSpan.FromSeconds(20);

                // La verifica dell'origin (§11.2 passo 2) vale per la connessione e per ogni metodo.
                options.AddFilter<CodiHubOriginFilter>();
            })
            // L'involucro di §11.4 è lo stesso del dispatcher: i contatori a 64 bit restano stringhe
            // decimali anche sull'hub, altrimenti lo stesso campo avrebbe due forme a seconda del
            // canale da cui esce.
            .AddJsonProtocol(protocol => CodiJson.ApplyTo(protocol.PayloadSerializerOptions));

        // La distribuzione dei comandi (§12.1–§12.2): il dispatcher è un servizio dell'applicazione,
        // la consegna ai gruppi è del trasporto, il ciclo che li unisce vive con il processo.
        services.AddSingleton<ICommandDeliveryNotifier, HubCommandDeliveryNotifier>();
        services.AddSingleton<OutboxDispatcher>();
        services.AddHostedService<OutboxDispatchService>();
        services.AddSingleton<TeacherObservationRegistry>();
        services.AddSingleton<WorkspacePreviewNotifier>();
        services.AddSingleton<IParticipantMessageNotifier, ParticipantMessageNotifier>();
        services.AddHostedService<TeacherObservationService>();
        services.AddHostedService<FinalizationCleanupService>();

        return services;
    }

    /// <summary>Mappa i due hub con gli schemi di autenticazione distinti (§11.1).</summary>
    public static IEndpointRouteBuilder MapCodiHubs(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHub<ParticipantHub>(CodiHubs.ParticipantPath);
        endpoints.MapHub<TeacherHub>(CodiHubs.TeacherPath);

        return endpoints;
    }
}
