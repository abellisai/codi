using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Api.Authentication;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Authentication;

/// <summary>
/// Flussi HTTP dell'accesso amministrativo sulla stessa composizione di <c>Program</c> (§9.1,
/// §10.2, §10.3). L'applicazione su socket loopback e la gestione dei cookie le fornisce
/// <see cref="CodiHttpServer"/>.
/// </summary>
public sealed class AdminAuthenticationHttpTests : IAsyncLifetime
{
    private CodiHttpServer _server = null!;

    public async Task InitializeAsync() => _server = await CodiHttpServer.CreateAsync();

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Accesso_ConBootstrapELogout_EmetteTokenPerLaNuovaIdentitaERevocaIlTicket()
    {
        // Arrange: token anonimo e poi token della nuova identità.
        using var bootstrap = await _server.BootstrapAsync();
        Assert.Equal(HttpStatusCode.OK, bootstrap.StatusCode);
        Assert.Contains("httponly", bootstrap.Headers.GetValues("Set-Cookie")
            .Single(cookie => cookie.StartsWith(CodiAntiforgery.InternalCookieName + "=", StringComparison.Ordinal))
            .ToLowerInvariant());
        var anonymousToken = _server.Cookies[CodiAntiforgery.CookieName];
        using var login = await _server.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.NotEqual(anonymousToken, _server.Cookies[CodiAntiforgery.CookieName]);
        Assert.Contains("httponly", login.Headers.GetValues("Set-Cookie")
            .Single(cookie => cookie.StartsWith(AdminCookieAuthentication.CookieName + "=", StringComparison.Ordinal))
            .ToLowerInvariant());
        Assert.DoesNotContain("httponly", login.Headers.GetValues("Set-Cookie")
            .Single(cookie => cookie.StartsWith(CodiAntiforgery.CookieName + "=", StringComparison.Ordinal))
            .ToLowerInvariant());
        var authenticatedCookies = new Dictionary<string, string>(_server.Cookies);

        // Act: il token appena emesso autorizza una richiesta sul dispatcher e il logout.
        using var unknown = await _server.SendAsync(HttpMethod.Post, "/api/v1/admin/execute/command/Unknown", new { });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using var logout = await _server.SendAsync(HttpMethod.Post, "/api/v1/admin/auth/logout", new { });

        // Assert: ticket copiato prima della revoca non torna utilizzabile.
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        _server.ReplaceCookies(authenticatedCookies);
        using var replay = await _server.SendAsync(HttpMethod.Post, "/api/v1/admin/execute/command/LogoutAdmin", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ContestoCorrente_SenzaEConAccesso_DistingueLeDueCondizioni()
    {
        // Arrange: la pagina amministrativa ricaricata deve poter sapere se la sessione esiste.
        using var bootstrap = await _server.BootstrapAsync();
        Assert.Equal(HttpStatusCode.OK, bootstrap.StatusCode);

        // Act / Assert: senza accesso il profilo non è disponibile.
        using var withoutSession = await _server.SendAsync(HttpMethod.Get, "/api/v1/admin/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, withoutSession.StatusCode);

        using var login = await _server.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var profile = await _server.SendAsync(HttpMethod.Get, "/api/v1/admin/auth/me");

        // Assert: il profilo porta il nome e i permessi che il server riconosce davvero.
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        var body = await profile.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Maestra", body.GetProperty("username").GetString());
        Assert.Equal(
            new[] { nameof(CodiPolicy.Admin) },
            body.GetProperty("policies").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ContestoCorrente_DopoIlLogout_NonRiconoscePiuLaCredenziale()
    {
        // Arrange
        using var login = await _server.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var logout = await _server.SendAsync(HttpMethod.Post, "/api/v1/admin/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // Act: il cookie è stato cancellato dalla risposta, ma la verifica non si affida al
        // browser: rilegge la credenziale, che è revocata.
        using var profile = await _server.SendAsync(HttpMethod.Get, "/api/v1/admin/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Logout_ConTokenAssenteOFalso_RifiutaSenzaRevocareLaCredenziale()
    {
        // Arrange
        using var login = await _server.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // Act / Assert
        foreach (var header in new string?[] { null, "token-falso" })
        {
            using var rejected = await _server.SendAsync(
                HttpMethod.Post, "/api/v1/admin/auth/logout", new { }, header, useCookieToken: false);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(CodiErrorCodes.AntiforgeryRejected, problem.GetProperty("code").GetString());
        }
        using var accepted = await _server.SendAsync(HttpMethod.Post, "/api/v1/admin/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LogoutDalDispatcher_ConIdentificativoAltruiNelCorpo_RevocaSoloLaCredenzialeCorrente()
    {
        // Arrange: due accessi indipendenti, con identificativo noto attraverso il comando login.
        using var login = await _server.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // Il login del dispatcher è anonimo: il secondo accesso parte senza cookie, come un
        // secondo browser dello stesso docente.
        using var otherLogin = await _server.SendAnonymousAsync(
            HttpMethod.Post,
            "/api/v1/admin/execute/command/LoginAdmin",
            new { username = "Maestra", password = CodiHttpServer.AdminPassword });

        // Act / Assert
        Assert.Equal(HttpStatusCode.OK, otherLogin.StatusCode);
        var other = await otherLogin.Content.ReadFromJsonAsync<JsonElement>();
        var otherId = other.GetProperty("credentialSessionId").GetGuid();

        // L'identificativo altrui nel corpo non cambia la credenziale revocata: il contesto
        // verificato viene dal trasporto, non dal payload.
        using var logout = await _server.SendAsync(HttpMethod.Post, "/api/v1/admin/execute/command/LogoutAdmin",
            new { credentialSessionId = otherId, context = new { credentialSessionId = otherId } });
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        await using var work = await _server.Services
            .GetRequiredService<IUnitOfWorkContext>()
            .CreateAsync();
        var sessions = work.Repo<ICredentialSessionRepository>();
        var otherStored = await sessions.ReadCredentialAsync(otherId, CancellationToken.None);
        Assert.Null(otherStored!.RevokedAtUtc);

        using var repeat = await _server.SendAsync(HttpMethod.Post, "/api/v1/admin/execute/command/LogoutAdmin", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, repeat.StatusCode);
    }
}
