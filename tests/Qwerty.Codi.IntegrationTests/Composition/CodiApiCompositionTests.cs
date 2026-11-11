using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Qwerty.Codi.Api.Authentication;
using Qwerty.Codi.Api.Configuration;
using Qwerty.Codi.Api.Dispatch;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Admin;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Infrastructure.Persistence;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Composition;

/// <summary>
/// Composizione reale dell'applicazione (§18.2, §20.2). Non ricopia le registrazioni: chiama la
/// stessa extension di <c>Program</c>, così il test verifica ciò che parte davvero e non una
/// composizione parallela che può divergere senza che nulla lo segnali.
/// </summary>
/// <remarks>
/// Il motivo per cui questo file esiste è un difetto già osservato: la cache del mediatore risolve
/// tutti gli handler alla prima invocazione, quindi una dipendenza mancante in una sola
/// composizione rompe richieste non correlate, con un messaggio che nomina un altro handler.
/// </remarks>
public sealed class CodiApiCompositionTests : IDisposable
{
    private readonly string _rootPath =
        Path.Combine(Path.GetTempPath(), $"codi-composizione-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
        catch (IOException)
        {
            // Percorso univoco e non riutilizzato: non eliminare un temporaneo non deve
            // trasformare un test superato in un test fallito.
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Composizione_ConValidazioneAllAvvio_RisolveIRegistriSenzaErrori()
    {
        // Act: la costruzione con ValidateOnBuild attraversa tutte le registrazioni, quindi
        // fallisce qui se una dipendenza manca o se una durata è incoerente. L'asserzione osserva
        // l'esito dall'esterno: il registro delle richieste si ottiene e dichiara le attese di §10.4.
        using var provider = BuildProvider();

        var registry = provider.GetRequiredService<CodiRequestRegistry>();

        Assert.NotEmpty(registry.Descriptors);

        // Il conteggio dei tentativi di codice di ingresso è una difesa della composizione reale
        // (§17.3) e vive in memoria come il registro delle connessioni: si ottiene da questa stessa
        // composizione, non da una registrazione parallela dei test.
        Assert.NotNull(provider.GetRequiredService<IJoinCodeAttemptRegistry>());

        // L'elenco chiuso di §10.4 non ha più nomi senza tipo: `JoinSession` è arrivata con l'aula
        // live (T3). Da qui in avanti una richiesta anonima dichiarata ma assente è una regressione.
        Assert.Empty(registry.MissingAnonymousBootstrapRequests);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispatcher_ConComposizioneReale_EsegueIlLoginDiUnAccountInesistente()
    {
        // Arrange: il database della composizione è vuoto e va preparato come all'avvio (§16.1).
        await using var provider = BuildProvider();
        await PrepareDatabaseAsync(provider);

        using var scope = provider.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<CodiRequestExecutor>();

        using var payload = JsonDocument.Parse(
            JsonSerializer.Serialize(new { username = "Nessuno", password = "qualunque" }, CodiJson.Payload));

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                LoginAdminCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                CodiRequestContext.Anonymous,
                CancellationToken.None)
            .AsTask());

        // Assert: un errore applicativo previsto, non un errore di risoluzione delle dipendenze.
        // Se la catena handler → repository → impronta non si risolvesse, qui arriverebbe
        // InvalidOperationException e il test direbbe che il messaggio nomina un handler casuale.
        Assert.Equal(CodiErrorCodes.CredentialsRejected, exception.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ContestoDiRichiesta_ConComposizioneReale_NonConcedeNienteSenzaCredenziale()
    {
        await using var provider = BuildProvider();

        using var scope = provider.CreateAsyncScope();
        var contextProvider = scope.ServiceProvider.GetRequiredService<ICodiRequestContextProvider>();

        var context = await contextProvider
            .ResolveRequestContextAsync(new DefaultHttpContext(), CancellationToken.None);

        Assert.False(context.IsAuthenticated);
        Assert.True(context.Allows(CodiPolicy.AnonymousBootstrap));
        Assert.False(context.Allows(CodiPolicy.Admin));
    }

    private ServiceProvider BuildProvider()
    {
        Directory.CreateDirectory(_rootPath);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Codi:DataDirectory"] = "data",
                ["Codi:MediaDirectory"] = "data/media",
                ["Codi:Database:FileName"] = "codi.db",
                ["Codi:Database:BusyTimeoutMs"] = "5000",
            })
            .Build();

        var services = new ServiceCollection();

        // Il logger lo aggiunge l'host: nella composizione dei test va chiesto esplicitamente,
        // altrimenti la validazione segnala una dipendenza mancante che in produzione esiste.
        services.AddLogging();

        // SignalR si appoggia ai servizi dell'host (durata dell'applicazione): la composizione dei
        // test non ha un host, quindi fornisce quello che `WebApplicationBuilder` aggiungerebbe.
        services.AddSingleton<IHostApplicationLifetime>(new StubApplicationLifetime());
        services.AddCodiApi(configuration, new StubHostEnvironment(_rootPath));

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private static async Task PrepareDatabaseAsync(ServiceProvider provider)
    {
        await provider
            .GetRequiredService<DatabaseBootstrap>()
            .PrepareAsync(CancellationToken.None)
            .ConfigureAwait(false);
    }

    /// <summary>Contesto minimo: la composizione legge solo <c>ContentRootPath</c>.</summary>
    private sealed class StubHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "Qwerty.Codi.IntegrationTests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>
    /// Ciclo di vita dell'applicazione per la composizione senza host: SignalR lo richiede per
    /// il gestore delle connessioni e in esecuzione è l'host a fornirlo.
    /// </summary>
    private sealed class StubApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}
