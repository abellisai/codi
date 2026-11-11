using Microsoft.Extensions.Options;
using Qwerty.Codi.Api.Authentication;
using Qwerty.Codi.Api.Dispatch;
using Qwerty.Codi.Api.Health;
using Qwerty.Codi.Api.Hubs;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Infrastructure.Configuration;
using Qwerty.Codi.Infrastructure.Migrations;
using Qwerty.Codi.Infrastructure.Persistence;
using Qwerty.Codi.Infrastructure.Realtime;

namespace Qwerty.Codi.Api.Configuration;

/// <summary>
/// Compone configurazione e servizi di avvio (§18.2, §20.9). Le impostazioni obbligatorie sono
/// validate all'avvio, così l'applicazione non parte con configurazione incompleta e i percorsi
/// relativi vengono risolti contro la base del servizio, non contro la directory di lavoro.
/// </summary>
public static class CodiServiceCollectionExtensions
{
    /// <summary>
    /// Registra tutti i servizi dell'installazione in un punto solo. L'avvio e i test di
    /// composizione chiamano questo metodo: se i test ricopiassero le registrazioni, potrebbero
    /// divergere da quelle vere senza che nulla lo segnali, e non proverebbero niente.
    /// </summary>
    public static IServiceCollection AddCodiApi(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCodiConfiguration(configuration, environment);
        services.AddCodiDatabase();
        services.AddCodiAuthentication();
        services.AddCodiDispatch();
        services.AddCodiRealtimeInfrastructure();
        services.AddCodiRealtime();
        services.AddProblemDetails();

        // Gli endpoint dedicati restituiscono DTO con gli stessi campi del dispatcher: le
        // convenzioni di §10.2 valgono anche qui, altrimenti un contatore a 64 bit uscirebbe come
        // numero da un endpoint e come stringa da un altro.
        services.ConfigureHttpJsonOptions(options => CodiJson.ApplyTo(options.SerializerOptions));
        services.AddExceptionHandler<CodiRequestExceptionHandler>();
        services
            .AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        return services;
    }

    /// <summary>
    /// Collega la sezione <c>Codi</c>, ne valida i valori e registra i percorsi già risolti.
    /// </summary>
    public static IServiceCollection AddCodiConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services
            .AddOptions<CodiOptions>()
            .Bind(configuration.GetSection("Codi"))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.DataDirectory),
                "Codi:DataDirectory è obbligatoria.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Database.FileName),
                "Codi:Database:FileName è obbligatorio.")
            .Validate(
                options => !Path.IsPathRooted(options.Database.FileName),
                "Codi:Database:FileName deve essere un nome di file, non un percorso.")
            .Validate(
                options => options.Database.BusyTimeoutMs > 0,
                "Codi:Database:BusyTimeoutMs deve essere maggiore di zero.")
            .Validate(
                options => (options.SeedAdmin.Username.Length == 0) == (options.SeedAdmin.Password.Length == 0),
                "Codi:SeedAdmin:Username e Codi:SeedAdmin:Password vanno impostate insieme, o lasciate entrambe vuote.")
            .ValidateOnStart();

        services.AddSingleton(provider => SqliteDatabase.FromOptions(
            environment.ContentRootPath,
            provider.GetRequiredService<IOptions<CodiOptions>>().Value));

        return services;
    }

    /// <summary>
    /// Registra la factory di connessione, il catalogo delle migrazioni, il runner e la
    /// preparazione del database. Il catalogo si costruisce alla prima risoluzione, cioè durante
    /// la preparazione: un catalogo incoerente impedisce l'avvio e non emerge alla prima richiesta.
    /// </summary>
    public static IServiceCollection AddCodiDatabase(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly));
        services.AddSingleton<MigrationRunner>();
        services.AddSingleton<DatabaseReadinessProbe>();
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<DatabaseBootstrap>();

        // I repository hanno durata limitata all'operazione (§4.3): una connessione per unità di
        // lavoro, condivisa dai repository che partecipano alla stessa transazione.
        services.AddCodiPersistence();

        return services;
    }
}
