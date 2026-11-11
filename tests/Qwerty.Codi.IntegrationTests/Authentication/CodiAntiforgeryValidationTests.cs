using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;
using Qwerty.Codi.Api.Authentication;
using Qwerty.Codi.Application.Dispatch;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Authentication;

/// <summary>
/// Antiforgery delle mutazioni autenticate con cookie (§10.2). Il token è verificato davvero, dal
/// servizio di ASP.NET Core configurato come in produzione: un test che fingesse il token
/// verificherebbe il finto, non il controllo.
/// </summary>
public sealed class CodiAntiforgeryValidationTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void RequiresValidation_ConMetodoSicuro_RinunciaAlControllo()
    {
        var context = CreateContext(HttpMethods.Get, authenticated: true);

        Assert.False(CodiAntiforgeryValidationMiddleware.RequiresValidation(context));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RequiresValidation_ConMutazioneAnonima_RinunciaAlControllo()
    {
        // Senza credenziale valida non c'è una richiesta a nome altrui da impedire: il contesto
        // mancante lo segnala già la policy della richiesta.
        var context = CreateContext(HttpMethods.Post, authenticated: false);

        Assert.False(CodiAntiforgeryValidationMiddleware.RequiresValidation(context));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RequiresValidation_ConMutazioneAutenticata_RichiedeIlControllo()
    {
        var context = CreateContext(HttpMethods.Post, authenticated: true);

        Assert.True(CodiAntiforgeryValidationMiddleware.RequiresValidation(context));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InvokeAsync_ConTokenValido_ArrivaAllaRichiesta()
    {
        // Arrange
        var (middleware, antiforgery) = BuildMiddleware();
        var context = CreateSignedRequest(antiforgery, HttpMethods.Post);

        var reached = false;
        RequestDelegate next = _ =>
        {
            reached = true;

            return Task.CompletedTask;
        };
        var middlewareWithNext = new CodiAntiforgeryValidationMiddleware(next);

        // Act
        await middlewareWithNext.InvokeAsync(context, antiforgery);

        // Assert
        Assert.True(reached);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InvokeAsync_ConTokenMancante_SollevaIlRifiuto()
    {
        // Arrange: il caso della pagina ostile che sfrutta il cookie della vittima.
        var (middleware, antiforgery) = BuildMiddleware();
        var context = CreateContext(HttpMethods.Post, authenticated: true);

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(
            () => middleware.InvokeAsync(context, antiforgery));

        // Assert
        Assert.Equal(CodiErrorCodes.AntiforgeryRejected, exception.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InvokeAsync_ConMetodoSicuroSenzaToken_ArrivaAllaRichiesta()
    {
        // Arrange
        var (middleware, antiforgery) = BuildMiddleware();
        var context = CreateContext(HttpMethods.Get, authenticated: true);

        var reached = false;
        RequestDelegate next = _ =>
        {
            reached = true;

            return Task.CompletedTask;
        };
        var middlewareWithNext = new CodiAntiforgeryValidationMiddleware(next);

        // Act
        await middlewareWithNext.InvokeAsync(context, antiforgery);

        // Assert
        Assert.True(reached);
    }

    private static readonly IServiceProvider Services = BuildServices();

    private static (CodiAntiforgeryValidationMiddleware Middleware, IAntiforgery Antiforgery) BuildMiddleware() =>
        (
            new CodiAntiforgeryValidationMiddleware(_ => Task.CompletedTask),
            Services.GetRequiredService<IAntiforgery>());

    /// <summary>
    /// Servizi costruiti con la stessa configurazione della produzione: un token generato da
    /// impostazioni diverse verificherebbe una regola che non è quella che parte davvero.
    /// </summary>
    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // Chiavi isolate in memoria: il test non legge né scrive quelle del profilo utente.
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddAntiforgery(CodiAntiforgery.Configure);

        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext CreateContext(string method, bool authenticated)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = Services,

            // Il cookie del token è `Secure`, quindi la verifica pretende una richiesta HTTPS:
            // è la stessa ragione per cui in laboratorio l'area amministrativa ha bisogno di
            // HTTPS o di un proxy che lo termini (T9).
            Request = { Method = method, IsHttps = true },
        };

        if (authenticated)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "Maestra")],
                AdminCookieAuthentication.SchemeName));
        }

        return context;
    }

    /// <summary>
    /// Costruisce una richiesta con la coppia cookie/header che il browser rimanda. Il token si
    /// genera su un contesto separato perché <c>GetAndStoreTokens</c> legge i cookie della
    /// richiesta e ne fissa la lettura: scrivere il cookie dopo, sullo stesso contesto, non
    /// avrebbe effetto. È anche il percorso reale — il cookie arriva con una risposta e torna con
    /// la richiesta successiva.
    /// </summary>
    private static DefaultHttpContext CreateSignedRequest(IAntiforgery antiforgery, string method)
    {
        var issuing = CreateContext(method, authenticated: true);
        var tokens = antiforgery.GetAndStoreTokens(issuing);

        Assert.False(string.IsNullOrEmpty(tokens.CookieToken));
        Assert.False(string.IsNullOrEmpty(tokens.RequestToken));

        var context = CreateContext(method, authenticated: true);
        context.Request.Headers.Cookie = $"{CodiAntiforgery.InternalCookieName}={tokens.CookieToken}";
        context.Request.Headers[CodiAntiforgery.HeaderName] = tokens.RequestToken;

        return context;
    }
}
