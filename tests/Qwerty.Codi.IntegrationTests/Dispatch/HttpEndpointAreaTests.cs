using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Api.Authentication;
using Qwerty.Codi.Api.Dispatch;
using Qwerty.Codi.Application.Dispatch;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Dispatch;

/// <summary>
/// Confine HTTP admin/public con la composizione reale. Un invoker controllato permette di
/// provare anche le policy alunni e regia i cui handler arriveranno negli incrementi successivi.
/// </summary>
public sealed class HttpEndpointAreaTests
{
    [Theory]
    [InlineData(CodiPolicy.Admin, "PrototypeArea", "admin", "command")]
    [InlineData(CodiPolicy.SessionController, "PrototypeArea", "admin", "query")]
    [InlineData(CodiPolicy.AdmittedParticipant, "PrototypeArea", "public", "query")]
    [InlineData(CodiPolicy.ActiveParticipantConnection, "PrototypeArea", "public", "command")]
    [InlineData(CodiPolicy.FinalizingParticipant, "PrototypeArea", "public", "command")]
    [InlineData(CodiPolicy.AnonymousBootstrap, "LoginAdmin", "admin", "command")]
    [InlineData(CodiPolicy.AnonymousBootstrap, "JoinSession", "public", "command")]
    [Trait("Category", "Integration")]
    public async Task Dispatcher_ConPolicyValida_EsegueSoloNellAreaDellaRichiesta(
        CodiPolicy policy, string name, string area, string kind)
    {
        // Arrange: contesto già verificato sostituito; registro ed esecutore restano reali.
        var invoker = new RecordingInvoker("eseguito");
        await using var server = await CreateProbeServerAsync(policy, name, kind, invoker);
        var otherArea = area == "admin" ? "public" : "admin";

        // Act: la stessa credenziale e lo stesso corpo provano entrambi gli ingressi HTTP.
        using var accepted = await server.SendAsync(HttpMethod.Post, $"/api/v1/{area}/execute/{kind}/{name}", new { text = "ciao" });
        using var rejected = await server.SendAsync(HttpMethod.Post, $"/api/v1/{otherArea}/execute/{kind}/{name}", new { text = "ciao" });

        // Assert: la richiesta fuori area non raggiunge l'handler e non pubblica il suo catalogo.
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal("eseguito", await accepted.Content.ReadFromJsonAsync<string>());
        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        Assert.IsType<EchoCommand>(Assert.Single(invoker.Requests));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("public")]
    [Trait("Category", "Integration")]
    public async Task Dispatcher_ConBootstrapLocale_NonLoEsponeInNessunaArea(string area)
    {
        // Arrange / Act
        await using var server = await CodiHttpServer.CreateAsync();
        using var response = await server.SendAsync(HttpMethod.Post,
            $"/api/v1/{area}/execute/command/BootstrapAdminAccount", new { });

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("command/CreateSchool", "CommandNotFound")]
    [InlineData("query/ListSchools", "QueryNotFound")]
    [Trait("Category", "Integration")]
    public async Task DispatcherPubblico_ConCookieAdmin_NonEsegueRichiesteAmministrative(string request, string code)
    {
        // Arrange: il cookie valido non concede l'accesso al catalogo admin dal gruppo public.
        await using var server = await CodiHttpServer.CreateAsync();
        using var login = await server.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // Act
        using var response = await server.SendAsync(HttpMethod.Post, $"/api/v1/public/execute/{request}", new { name = "Non deve essere creata" });

        // Assert: Problem Details e nessuna scrittura attraverso il percorso rifiutato.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
        using var schools = await server.SendAsync(HttpMethod.Post, "/api/v1/admin/execute/query/ListSchools", new { });
        Assert.Equal(HttpStatusCode.OK, schools.StatusCode);
        Assert.Empty((await schools.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("public")]
    [Trait("Category", "Integration")]
    public async Task Bootstrap_ConAreaDichiarata_EmetteLaCoppiaAntiforgery(string area)
    {
        // Arrange / Act
        await using var server = await CodiHttpServer.CreateAsync();
        using var response = await server.SendAsync(HttpMethod.Get, $"/api/v1/{area}/bootstrap");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(CodiAntiforgery.RequestCookieName($"/api/v1/{area}/bootstrap"), server.Cookies.Keys);
        Assert.Contains(CodiAntiforgery.InternalCookieName, server.Cookies.Keys);
    }

    [Theory]
    [InlineData(false, HttpStatusCode.Unauthorized)]
    [InlineData(true, HttpStatusCode.Forbidden)]
    [Trait("Category", "Integration")]
    public async Task Dispatcher_ConAreaCorrettaMaSenzaPermesso_ConservaIlControlloDiPolicy(
        bool authenticated, HttpStatusCode expectedStatus)
    {
        // Arrange: l'area non costituisce una concessione di policy.
        var invoker = new RecordingInvoker("non deve essere eseguito");
        await using var server = await CreateProbeServerAsync(CodiPolicy.Admin, "PrototypeArea", "command", invoker,
            new CodiRequestContext(authenticated, []));

        // Act
        using var response = await server.SendAsync(HttpMethod.Post, "/api/v1/admin/execute/command/PrototypeArea", new { text = "ciao" });

        // Assert
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Empty(invoker.Requests);
    }

    [Theory]
    [InlineData("/api/v1/execute/query/ListSchools")]
    [InlineData("/auth/admin/login")]
    [InlineData("/auth/login")]
    [InlineData("/admin/media-assets")]
    [Trait("Category", "Integration")]
    public async Task RottePrecedenti_DopoLaMigrazione_NonEspongonoUnSecondoIngresso(string path)
    {
        // Arrange / Act
        await using var server = await CodiHttpServer.CreateAsync();
        using var response = await server.SendAsync(HttpMethod.Post, path, new { });

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static Task<CodiHttpServer> CreateProbeServerAsync(
        CodiPolicy policy, string name, string kind, RecordingInvoker invoker, CodiRequestContext? context = null) =>
        CodiHttpServer.CreateAsync(services =>
        {
            var registry = CodiRequestRegistry.FromAssemblies(typeof(CodiRequestExecutor).Assembly);
            var descriptor = new CodiRequestDescriptor(name,
                kind == "command" ? CodiRequestKind.Command : CodiRequestKind.Query,
                policy, typeof(EchoCommand), typeof(string), false, invoker);
            services.AddSingleton(CodiRequestRegistry.FromDescriptors(
                registry.Descriptors.Where(entry => entry.Name != name).Append(descriptor)));
            services.AddSingleton<ICodiRequestContextProvider>(new ProbeContextProvider(
                context ?? new CodiRequestContext(policy != CodiPolicy.AnonymousBootstrap, [policy])));
        });

    private sealed class ProbeContextProvider(CodiRequestContext context) : ICodiRequestContextProvider
    {
        public Task<CodiRequestContext> ResolveRequestContextAsync(HttpContext httpContext, CancellationToken cancellationToken) =>
            Task.FromResult(context);
    }
}
