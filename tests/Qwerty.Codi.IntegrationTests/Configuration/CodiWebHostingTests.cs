using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Qwerty.Codi.Api.Configuration;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Configuration;

public sealed class CodiWebHostingTests
{
    [Theory]
    [InlineData("/", "alunni")]
    [InlineData("/join", "alunni")]
    [InlineData("/classroom", "alunni")]
    [InlineData("/admin", "docente")]
    [InlineData("/admin/login", "docente")]
    [InlineData("/admin/courses?tab=media", "docente")]
    [Trait("Category", "Integration")]
    public async Task UseCodiWeb_LinkDiretto_ServeIlBundleDellaSuaArea(string path, string expected)
    {
        // Arrange: bundle distinti in uno storage temporaneo; nessun database operativo.
        await using var host = await StaticHost.CreateAsync();

        // Act.
        using var response = await host.Client.GetAsync(path);

        // Assert.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expected, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/v1/unknown")]
    [InlineData("/auth/unknown")]
    [InlineData("/public/unknown")]
    [InlineData("/health/unknown")]
    [InlineData("/hubs/unknown")]
    [InlineData("/media-assets/00000000-0000-0000-0000-000000000001")]
    [InlineData("/admin/media-assets")]
    [InlineData("/API/unknown")]
    [InlineData("/admin/missing.js")]
    [Trait("Category", "Integration")]
    public async Task UseCodiWeb_RottaBackendOAssetAssente_NonServeIlFrontend(string path)
    {
        // Arrange.
        await using var host = await StaticHost.CreateAsync();

        // Act.
        using var response = await host.Client.GetAsync(path);

        // Assert.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UseCodiWeb_AssetAdmin_ServeIlFileStatico()
    {
        // Arrange.
        await using var host = await StaticHost.CreateAsync();

        // Act.
        using var response = await host.Client.GetAsync("/admin/main.js");

        // Assert.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("console.log('docente');", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UseCodiWeb_BundleAdminAssente_NonRipiegaSugliAlunni()
    {
        // Arrange.
        await using var host = await StaticHost.CreateAsync(includeAdmin: false);

        // Act.
        using var response = await host.Client.GetAsync("/admin/login");

        // Assert.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UseCodiWeb_PostAllaPagina_NonServeHtml()
    {
        // Arrange.
        await using var host = await StaticHost.CreateAsync();

        // Act.
        using var response = await host.Client.PostAsync("/join", null);

        // Assert.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class StaticHost(string directory, WebApplication application, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<StaticHost> CreateAsync(bool includeAdmin = true)
        {
            var directory = Path.Combine(Path.GetTempPath(), "Codi-WebTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory, "wwwroot", "admin"));
            await File.WriteAllTextAsync(Path.Combine(directory, "wwwroot", "index.html"), "alunni");
            if (includeAdmin)
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "wwwroot", "admin", "index.html"), "docente");
            }

            await File.WriteAllTextAsync(Path.Combine(directory, "wwwroot", "admin", "main.js"), "console.log('docente');");
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = directory,
                WebRootPath = "wwwroot",
                EnvironmentName = "Testing",
            });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var application = builder.Build();
            application.UseCodiWeb();
            await application.StartAsync();
            var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
            return new StaticHost(directory, application, client);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await application.DisposeAsync();
            var fullPath = Path.GetFullPath(directory);
            var tempRoot = Path.GetFullPath(Path.GetTempPath());
            if (!fullPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(fullPath).StartsWith("Codi-WebTests-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Directory temporanea non valida.");
            }

            Directory.Delete(fullPath, recursive: true);
        }
    }
}
