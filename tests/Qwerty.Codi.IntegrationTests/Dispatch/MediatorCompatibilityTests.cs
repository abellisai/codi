using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Dispatch;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Dispatch;

/// <summary>
/// Compatibilità di Mediator 2.1.7 con .NET 10 (D-01) nella forma che la soluzione ha dopo T1.
/// Il prototipo di T0 — richieste, handler e notifiche di prova eseguiti da un mediator generato
/// nel progetto di test — è stato ritirato: il generatore emette un tipo pubblico di
/// registrazione per ogni assembly che lo referenzia, e con il generatore anche in
/// <c>Application</c> la chiamata <c>AddMediator</c> diventa ambigua in questo progetto (CS0121,
/// misurato in build). Il generatore vive quindi in un solo assembly, quello degli handler.
/// Qui si verifica che quella registrazione arrivi al contenitore e da dove proviene il mediator
/// risolto; l'esecuzione di un handler attraverso il mediator è coperta dal primo caso d'uso
/// reale di <c>Application</c>, che non è una richiesta di prova di questo progetto.
/// </summary>
public sealed class MediatorCompatibilityTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void AddCodiRequests_RendeRisolvibileIlMediator()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCodiRequests();

        // Assert
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<IMediator>());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void MediatorRisolto_ProvieneDallAssemblyDelleRichieste()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCodiRequests();

        // Act
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Assert: la registrazione generata è compilata nell'assembly che ospita gli handler, non
        // in quello che la consuma. Se il generatore tornasse anche altrove, il mediator qui non
        // arriverebbe da lì.
        Assert.Equal(typeof(ICodiRequest).Assembly, mediator.GetType().Assembly);
    }
}
