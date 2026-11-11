using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Qwerty.Codi.Api.Dispatch;
using Qwerty.Codi.Application.Dispatch;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Dispatch;

/// <summary>
/// Gestore delle eccezioni di §4.4: gli errori applicativi tipizzati diventano Problem Details
/// con codice stabile e stato di §10.7, il resto non viene intercettato. La risposta si compone
/// su un contesto HTTP reale, senza avviare un server.
/// </summary>
public sealed class CodiRequestExceptionHandlerTests
{
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(CodiErrorCodes.InvalidPayload, StatusCodes.Status400BadRequest)]
    [InlineData(CodiErrorCodes.AuthenticationRequired, StatusCodes.Status401Unauthorized)]
    [InlineData(CodiErrorCodes.PermissionDenied, StatusCodes.Status403Forbidden)]
    [InlineData(CodiErrorCodes.CommandNotFound, StatusCodes.Status404NotFound)]
    [InlineData(CodiErrorCodes.QueryNotFound, StatusCodes.Status404NotFound)]
    [InlineData(CodiErrorCodes.RowVersionMismatch, StatusCodes.Status412PreconditionFailed)]
    public async Task TryHandleAsync_ConCodiceApplicativo_RispondeConStatoCodiceECorrelazione(
        string code,
        int expectedStatus)
    {
        // Arrange
        await using var provider = BuildProvider();
        var httpContext = CreateHttpContext(provider);
        var handler = CreateHandler(provider);

        // Act
        var handled = await handler.TryHandleAsync(
            httpContext,
            new CodiRequestException(code, "messaggio di prova"),
            CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(expectedStatus, httpContext.Response.StatusCode);

        var problem = await ReadProblemAsync(httpContext);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal(expectedStatus, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TryHandleAsync_ConEccezioneNonApplicativa_NonLaGestisce()
    {
        // Arrange
        await using var provider = BuildProvider();
        var httpContext = CreateHttpContext(provider);
        var handler = CreateHandler(provider);

        // Act
        var handled = await handler.TryHandleAsync(
            httpContext,
            new InvalidOperationException("guasto imprevisto"),
            CancellationToken.None);

        // Assert: non gestita significa che la risposta non è stata composta qui.
        Assert.False(handled);
    }

    private static CodiRequestExceptionHandler CreateHandler(IServiceProvider provider) =>
        new(provider.GetRequiredService<ILogger<CodiRequestExceptionHandler>>());

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        services.ConfigureHttpJsonOptions(_ => { });

        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext CreateHttpContext(IServiceProvider provider)
    {
        var httpContext = new DefaultHttpContext { RequestServices = provider };
        httpContext.Request.Path = "/api/v1/admin/execute/command/PrototypeEcho";
        httpContext.Response.Body = new MemoryStream();

        return httpContext;
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpContext httpContext)
    {
        httpContext.Response.Body.Position = 0;

        using var document = await JsonDocument.ParseAsync(httpContext.Response.Body);
        return document.RootElement.Clone();
    }
}
