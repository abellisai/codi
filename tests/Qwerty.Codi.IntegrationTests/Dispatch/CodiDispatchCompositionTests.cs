using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Dispatch;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Dispatch;

/// <summary>
/// Composizione di §4.4 come la usa l'API: la registrazione applicativa deve rendere risolvibili
/// registro ed esecutore, e il registro deve essere un catalogo condiviso. Che il mediator
/// esista è verificato dai test di compatibilità, che ne controllano anche la provenienza.
/// </summary>
public sealed class CodiDispatchCompositionTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void AddCodiRequests_RendeRisolvibiliRegistroEEsecutore()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCodiRequests();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var registry = scope.ServiceProvider.GetService<CodiRequestRegistry>();
        var executor = scope.ServiceProvider.GetService<CodiRequestExecutor>();

        // Assert
        Assert.NotNull(registry);
        Assert.NotNull(executor);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void AddCodiRequests_RegistraLaStessaIstanzaDiRegistro()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCodiRequests();

        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        // Act: il registro è un catalogo condiviso, non una copia per richiesta.
        var first = firstScope.ServiceProvider.GetRequiredService<CodiRequestRegistry>();
        var second = secondScope.ServiceProvider.GetRequiredService<CodiRequestRegistry>();

        // Assert
        Assert.Same(first, second);
    }
}
