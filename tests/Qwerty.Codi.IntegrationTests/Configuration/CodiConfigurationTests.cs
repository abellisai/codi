using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Qwerty.Codi.Api.Configuration;
using Qwerty.Codi.Infrastructure.Configuration;
using Qwerty.Codi.Infrastructure.Persistence;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Configuration;

/// <summary>
/// Validazione all'avvio della configurazione di §18.2 e §20.9: l'applicazione non parte con
/// valori mancanti e i percorsi relativi si risolvono contro la base del servizio.
/// </summary>
public sealed class CodiConfigurationTests
{
    [Fact]
    public void AddCodiConfiguration_ConValoriMancanti_FallisceAllaRisoluzione()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        services.AddCodiConfiguration(configuration, CreateEnvironment());

        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<CodiOptions>>().Value);
    }

    [Fact]
    public void AddCodiConfiguration_ConValoriValidi_RisolveIlPercorsoControLaBase()
    {
        var environment = CreateEnvironment();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Codi:DataDirectory"] = "data",
                    ["Codi:MediaDirectory"] = "data/media",
                    ["Codi:Database:FileName"] = "codi.db",
                    ["Codi:Database:BusyTimeoutMs"] = "5000",
                })
            .Build();

        var services = new ServiceCollection();
        services.AddCodiConfiguration(configuration, environment);

        using var provider = services.BuildServiceProvider();
        var database = provider.GetRequiredService<SqliteDatabase>();

        Assert.Equal(
            Path.Combine(environment.ContentRootPath, "data", "codi.db"),
            database.FilePath);
        Assert.Equal(5000, database.BusyTimeoutMs);
    }

    private static IHostEnvironment CreateEnvironment() =>
        new StubHostEnvironment(Path.Combine(Path.GetTempPath(), "codi-t1-config"));

    /// <summary>
    /// Contesto minimo: la composizione legge solo <c>ContentRootPath</c>, quindi il fornitore
    /// di file non viene popolato. L'implementazione concreta di <see cref="IHostEnvironment"/>
    /// non è pubblica e non serve importarla per un test.
    /// </summary>
    private sealed class StubHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "Qwerty.Codi.IntegrationTests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
