using System.Data.Common;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.Infrastructure.Migrations;
using Qwerty.Codi.Infrastructure.Persistence;
using Qwerty.Codi.Infrastructure.Realtime;
using Qwerty.Codi.IntegrationTests.Realtime;
using Xunit;

namespace Qwerty.Codi.IntegrationTests;

/// <summary>
/// Composizione riutilizzabile dei test di caso d'uso: database temporaneo migrato, registro,
/// mediator, repository e dispatcher reali. Esiste perché le aree di T2 condividono la stessa
/// preparazione, e ricopiarla in ogni file significherebbe provare composizioni diverse da quella
/// vera senza che nulla lo segnali.
///
/// Il tempo è fisso (<see cref="TestTimeProvider"/>): un istante «circa adesso» non si verifica.
/// </summary>
internal sealed class CodiUseCaseHost : IAsyncDisposable
{
    private readonly TempSqliteDatabase _database;
    private readonly ServiceProvider _services;
    private readonly IServiceScope _scope;

    private CodiUseCaseHost(
        TempSqliteDatabase database,
        ServiceProvider services,
        IServiceScope scope)
    {
        _database = database;
        _services = services;
        _scope = scope;
    }

    /// <summary>Esecutore delle richieste, con il registro e il mediator dell'applicazione reale.</summary>
    public CodiRequestExecutor Executor => _scope.ServiceProvider.GetRequiredService<CodiRequestExecutor>();

    /// <summary>Contenitore dello scope corrente.</summary>
    public IServiceProvider Services => _scope.ServiceProvider;

    /// <summary>Istante fisso usato da tutti i casi d'uso di questa composizione.</summary>
    public static DateTimeOffset Now => TestTimeProvider.Instant;

    public static async Task<CodiUseCaseHost> CreateAsync()
    {
        var database = TempSqliteDatabase.Create();

        var runner = new MigrationRunner(database.Factory, new TestTimeProvider());
        await runner.ApplyAsync(
            MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly),
            CancellationToken.None);

        var services = new ServiceCollection();
        services.AddCodiRequests();
        services.AddSingleton(database.Factory);
        services.AddCodiPersistence();
        // Il segnale dell'outbox e il registro delle connessioni sono dipendenze dei comandi di
        // regia e del protocollo realtime: la composizione dei casi d'uso li registra come fa l'API,
        // altrimenti gli handler non si risolverebbero.
        services.AddCodiRealtimeInfrastructure();
        services.AddSingleton<TimeProvider>(new TestTimeProvider());
        // Il trasporto della chat non esiste in questa composizione: l'astrazione dell'avviso viene
        // registrata con un doppio che registra gli invii, come per la consegna dei comandi di regia.
        services.AddSingleton<IParticipantMessageNotifier, RecordingParticipantMessageNotifier>();

        // Durate validate, costruzione no: questa composizione non registra l'impronta delle
        // password, che vive nell'assembly dell'API. La composizione completa, con la validazione
        // della costruzione, è quella dei test di composizione e del servizio vero.
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
        });

        return new CodiUseCaseHost(database, provider, provider.CreateScope());
    }

    /// <summary>Contesto di un docente autenticato: è quello che il trasporto fornisce al dispatcher.</summary>
    public static CodiRequestContext AdminContext() => new(isAuthenticated: true, [CodiPolicy.Admin]);
    /// <summary>
    /// Contesto di un ingresso anonimo (§9.4): nessuna identitÃ , ma i due dati che il trasporto
    /// legge dal cookie di ammissione e dall'intestazione della chiave di idempotenza.
    /// </summary>
    public static CodiRequestContext JoinContext(string admissionScope, string idempotencyKey) =>
        new(
            isAuthenticated: false,
            [CodiPolicy.AnonymousBootstrap],
            credentialSessionId: null,
            adminUserId: null,
            participantId: null,
            admissionScope,
            idempotencyKey);

    /// <summary>Contesto di un partecipante autenticato: la credenziale Ã¨ giÃ  stata verificata.</summary>
    public static CodiRequestContext ParticipantContext(
        Guid credentialSessionId,
        Guid participantId,
        Guid? sessionId = null) =>
        new(
            isAuthenticated: true,
            [CodiPolicy.AdmittedParticipant],
            credentialSessionId,
            adminUserId: null,
            participantId,
            admissionScope: null,
            idempotencyKey: null);
    /// <summary>Corpo JSON nella forma del contratto (§10.2), con i contatori come stringhe.</summary>
    public static JsonDocument Payload(object body) =>
        JsonDocument.Parse(JsonSerializer.Serialize(body, CodiJson.Payload));

    public async Task<object?> ExecuteAsync(CodiRequestKind kind, string name, object body)
    {
        using var payload = Payload(body);

        return await Executor.ExecuteAsync(
            kind, name, CodiRequestChannel.Http, payload.RootElement, AdminContext(), CancellationToken.None);
    }

    /// <summary>Esegue una richiesta con un contesto dichiarato dal caso di prova, non da un docente.</summary>
    public async Task<object?> ExecuteAsAsync(
        CodiRequestKind kind,
        string name,
        object body,
        CodiRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        using var payload = Payload(body);

        return await Executor.ExecuteAsync(
            kind, name, CodiRequestChannel.Http, payload.RootElement, context, CancellationToken.None);
    }

    /// <summary>Comando eseguito con un contesto dichiarato dal caso di prova.</summary>
    public async Task<T> CommandAsAsync<T>(string name, object body, CodiRequestContext context) =>
        Assert.IsAssignableFrom<T>(await ExecuteAsAsync(CodiRequestKind.Command, name, body, context));

    /// <summary>Query eseguita con un contesto dichiarato dal caso di prova.</summary>
    public async Task<T> QueryOneAsAsync<T>(string name, object body, CodiRequestContext context) =>
        Assert.IsType<T>(await ExecuteAsAsync(CodiRequestKind.Query, name, body, context));

    /// <summary>Comando che deve essere rifiutato, con il contesto dichiarato dal caso di prova.</summary>
    public Task<CodiRequestException> ExpectFailureAsAsync(
        string name,
        object body,
        CodiRequestContext context) =>
        Assert.ThrowsAsync<CodiRequestException>(() => ExecuteAsAsync(CodiRequestKind.Command, name, body, context));

    /// <summary>Query che deve essere rifiutata, con il contesto dichiarato dal caso di prova.</summary>
    public Task<CodiRequestException> ExpectQueryFailureAsAsync(
        string name,
        object body,
        CodiRequestContext context) =>
        Assert.ThrowsAsync<CodiRequestException>(() => ExecuteAsAsync(CodiRequestKind.Query, name, body, context));

    public async Task<T> CommandAsync<T>(string name, object body) =>
        Assert.IsType<T>(await ExecuteAsync(CodiRequestKind.Command, name, body));

    /// <summary>Comando che restituisce un elenco: il tipo concreto dipende dal caso d'uso.</summary>
    public async Task<IReadOnlyList<T>> ListCommandAsync<T>(string name, object body) =>
        Assert.IsAssignableFrom<IReadOnlyList<T>>(await ExecuteAsync(CodiRequestKind.Command, name, body));

    /// <summary>Query che restituisce un elenco: il tipo concreto dipende dal caso d'uso.</summary>
    public async Task<IReadOnlyList<T>> QueryAsync<T>(string name, object body) =>
        Assert.IsAssignableFrom<IReadOnlyList<T>>(await ExecuteAsync(CodiRequestKind.Query, name, body));

    /// <summary>Query che restituisce un solo oggetto.</summary>
    public async Task<T> QueryOneAsync<T>(string name, object body) =>
        Assert.IsType<T>(await ExecuteAsync(CodiRequestKind.Query, name, body));

    /// <summary>Esegue un comando che deve essere rifiutato e restituisce l'errore applicativo prodotto.</summary>
    public Task<CodiRequestException> ExpectFailureAsync(string name, object body) =>
        Assert.ThrowsAsync<CodiRequestException>(() => ExecuteAsync(CodiRequestKind.Command, name, body));

    /// <summary>Come sopra, per una query.</summary>
    public Task<CodiRequestException> ExpectQueryFailureAsync(string name, object body) =>
        Assert.ThrowsAsync<CodiRequestException>(() => ExecuteAsync(CodiRequestKind.Query, name, body));

    /// <summary>Connessione diretta al database dei test, per verificare lo schema o le righe scritte.</summary>
    public Task<DbConnection> OpenConnectionAsync() => _database.Factory.OpenAsync(CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        _scope.Dispose();
        await _services.DisposeAsync();
        _database.Dispose();
    }
}
