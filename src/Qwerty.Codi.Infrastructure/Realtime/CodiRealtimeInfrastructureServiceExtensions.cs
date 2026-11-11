using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Abstractions;

namespace Qwerty.Codi.Infrastructure.Realtime;

/// <summary>
/// Registrazione dei servizi che vivono in memoria: segnale dell'outbox, registro delle connessioni,
/// istanza del server, coordinamento delle sessioni, cache di osservazione e anteprima, conteggio
/// dei tentativi di codice di ingresso. Sta in una extension separata perché la composizione dei
/// casi d'uso deve poterla chiamare senza l'hosting: un handler che segnala un messaggio nuovo non
/// si risolve se il segnale non è registrato, e il trasporto degli hub non esiste senza registro.
/// </summary>
public static class CodiRealtimeInfrastructureServiceExtensions
{
    public static IServiceCollection AddCodiRealtimeInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IOutboxSignal, OutboxSignal>();
        services.AddSingleton<IParticipantConnectionRegistry, ParticipantConnectionRegistry>();
        services.AddSingleton<IServerInstance, ServerInstance>();
        services.AddSingleton<ISessionCoordinator, SessionCoordinator>();
        services.AddSingleton<IParticipantObservationCache, ParticipantObservationCache>();
        services.AddSingleton<IParticipantPreviewCache, ParticipantPreviewCache>();
        services.AddSingleton<IParticipantMessageStore, ParticipantMessageStore>();
        services.AddSingleton<IJoinCodeAttemptRegistry, JoinCodeAttemptRegistry>();
        services.AddScoped<Qwerty.Codi.Application.Sessions.ParticipantObservationService>();

        return services;
    }
}
