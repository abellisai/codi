using Qwerty.Codi.Application.Dto;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Qwerty.Codi.Api.Authentication;
using Qwerty.Codi.Api.Configuration;
using Qwerty.Codi.Application.Admin;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Infrastructure.Persistence;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Admin;

/// <summary>
/// Account amministrativo iniziale inserito dalla configurazione alla preparazione del database
/// (§9.1). Deroga richiesta esplicitamente il 30 settembre 2026: l'ambiente che la configura ha un
/// accesso pronto senza eseguire il comando locale, quello che non la configura non crea nulla.
///
/// La composizione è quella vera (<c>AddCodiApi</c>), con l'impronta delle password dell'API: una
/// composizione parallela non proverebbe ciò che parte davvero.
/// </summary>
public sealed class AdminAccountSeedTests : IDisposable
{
    private const string Username = "Admin";
    private const string Password = "password-del-seed";

    private readonly string _rootPath =
        Path.Combine(Path.GetTempPath(), $"codi-seed-{Guid.NewGuid():N}");

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
    public async Task Seed_ConConfigurazione_CreaIlPrimoAccountEIlLoginRiesce()
    {
        await using var provider = BuildProvider(Username, Password);
        await PrepareDatabaseAsync(provider);

        using var scope = provider.CreateAsyncScope();

        var created = await scope.ServiceProvider
            .GetRequiredService<AdminAccountSeed>()
            .EnsureAsync(CancellationToken.None);

        Assert.True(created);

        // Al secondo avvio non c'è niente da fare: il caso d'uso rifiuta un secondo account quando
        // un amministratore è già attivo, e questa non è una condizione di errore.
        var again = await scope.ServiceProvider
            .GetRequiredService<AdminAccountSeed>()
            .EnsureAsync(CancellationToken.None);

        Assert.False(again);

        // Le credenziali configurate funzionano davvero: è il motivo per cui il seed esiste.
        var signedIn = await SignInAsync(scope.ServiceProvider, Username, Password);

        Assert.Equal(Username, signedIn.Username);
        Assert.NotEqual(Guid.Empty, signedIn.AdminUserId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Seed_SenzaConfigurazione_NonCreaNessunAccount()
    {
        await using var provider = BuildProvider(username: null, password: null);
        await PrepareDatabaseAsync(provider);

        using var scope = provider.CreateAsyncScope();

        var created = await scope.ServiceProvider
            .GetRequiredService<AdminAccountSeed>()
            .EnsureAsync(CancellationToken.None);

        Assert.False(created);

        // La prova che non esiste alcun account è l'accesso rifiutato, non l'assenza di una riga:
        // è il comportamento che l'ambiente vede.
        var exception = await Assert.ThrowsAsync<CodiRequestException>(
            () => SignInAsync(scope.ServiceProvider, Username, Password));

        Assert.Equal(CodiErrorCodes.CredentialsRejected, exception.Code);
    }

    private static async Task<AdminSignedInDto> SignInAsync(
        IServiceProvider services,
        string username,
        string password)
    {
        using var payload = JsonDocument.Parse(
            JsonSerializer.Serialize(new { username, password }, CodiJson.Payload));

        var signedIn = await services
            .GetRequiredService<CodiRequestExecutor>()
            .ExecuteAsync(
                CodiRequestKind.Command,
                LoginAdminCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                CodiRequestContext.Anonymous,
                CancellationToken.None);

        return Assert.IsType<AdminSignedInDto>(signedIn);
    }

    private ServiceProvider BuildProvider(string? username, string? password)
    {
        Directory.CreateDirectory(_rootPath);

        var settings = new Dictionary<string, string?>
        {
            ["Codi:DataDirectory"] = "data",
            ["Codi:MediaDirectory"] = "data/media",
            ["Codi:Database:FileName"] = "codi.db",
            ["Codi:Database:BusyTimeoutMs"] = "5000",
        };

        if (username is not null && password is not null)
        {
            settings["Codi:SeedAdmin:Username"] = username;
            settings["Codi:SeedAdmin:Password"] = password;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();

        // Il logger lo aggiunge l'host: senza, la validazione segnalerebbe una dipendenza mancante
        // che in produzione esiste.
        services.AddLogging();

        // SignalR si appoggia ai servizi dell'host: la composizione dei test non ha un host, quindi
        // fornisce quello che `WebApplicationBuilder` aggiungerebbe.
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

    private sealed class StubHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "Qwerty.Codi.IntegrationTests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>
    /// Ciclo di vita dell'applicazione per la composizione senza host: SignalR lo richiede per il
    /// gestore delle connessioni e in esecuzione è l'host a fornirlo.
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
