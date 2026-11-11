using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Api.Dispatch;

/// <summary>
/// Compone il dispatcher per il servizio HTTP: le parti applicative arrivano da
/// <c>Application</c>, qui si aggiunge solo la risoluzione del contesto dal trasporto. Gli
/// endpoint si mappano a parte, quando il pipeline è costruito.
/// </summary>
internal static class DispatchServiceCollectionExtensions
{
    public static IServiceCollection AddCodiDispatch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCodiRequests();

        // Il contesto di richiesta non si registra qui: appartiene all'autenticazione, che è il
        // solo posto in cui si sa leggere una credenziale (AddCodiAuthentication). Registrarlo
        // anche qui lo sostituirebbe, in silenzio e nell'ordine sbagliato.
        return services;
    }
}
