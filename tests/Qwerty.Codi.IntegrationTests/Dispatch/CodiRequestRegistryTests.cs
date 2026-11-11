using Qwerty.Codi.Application.Dispatch;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Dispatch;

/// <summary>
/// Registro delle richieste di §4.4: scoperta dei metadati, elenco chiuso dell'accesso anonimo
/// di §10.4, unicità dei nomi pubblici e separazione dei due canali di §10.1.
/// </summary>
public sealed class CodiRequestRegistryTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void FromTypes_ConRichiesteValide_EsponeNomeNaturaEPolicy()
    {
        var registry = BuildRegistry();

        var echo = Assert.Single(registry.Descriptors, descriptor => descriptor.Name == "PrototypeEcho");
        Assert.Equal(CodiRequestKind.Command, echo.Kind);
        Assert.Equal(CodiPolicy.Admin, echo.Policy);
        Assert.False(echo.IsLocalOnly);
        Assert.Same(typeof(EchoCommand), echo.RequestType);
        Assert.Same(typeof(string), echo.ResponseType);

        var bootstrap = Assert.Single(
            registry.Descriptors,
            descriptor => descriptor.Name == AnonymousBootstrapRequests.BootstrapAdminAccount);
        Assert.True(bootstrap.IsLocalOnly);
        Assert.Equal(CodiPolicy.AnonymousBootstrap, bootstrap.Policy);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Find_SulCanaleSbagliato_NonTrovaLaRichiesta()
    {
        var registry = BuildRegistry();

        Assert.NotNull(registry.Find(CodiRequestKind.Query, "PrototypeProbe"));
        Assert.Null(registry.Find(CodiRequestKind.Command, "PrototypeProbe"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FromTypes_ConTipoCheNonERichiesta_IgnoraIlTipo()
    {
        var registry = CodiRequestRegistry.FromTypes([typeof(string), typeof(CodiRequestRegistryTests)]);

        Assert.Empty(registry.Descriptors);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FromTypes_SenzaRichieste_RiportaTutteLeAnonimeDaImplementare()
    {
        var registry = CodiRequestRegistry.FromTypes([]);

        Assert.Equal(
            AnonymousBootstrapRequests.All.OrderBy(name => name),
            registry.MissingAnonymousBootstrapRequests.OrderBy(name => name));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FromTypes_ConRichiestaSenzaPolicy_RifiutaIlCatalogo()
    {
        var exception = Assert.Throws<CodiDispatchConfigurationException>(() =>
            CodiRequestRegistry.FromTypes([typeof(RequestWithoutPolicy)]));

        Assert.Contains("natura", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FromTypes_ConNomePubblicoNonPascalCase_RifiutaIlCatalogo()
    {
        var exception = Assert.Throws<CodiDispatchConfigurationException>(() =>
            CodiRequestRegistry.FromTypes([typeof(BadNameCommand)]));

        Assert.Contains("PascalCase", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FromTypes_ConNomePubblicoRipetuto_RifiutaIlCatalogo()
    {
        var exception = Assert.Throws<CodiDispatchConfigurationException>(() =>
            CodiRequestRegistry.FromTypes([typeof(DuplicateFirstCommand), typeof(DuplicateSecondCommand)]));

        Assert.Contains("PrototypeDuplicate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FromTypes_ConAccessoAnonimoFuoriElenco_RifiutaIlCatalogo()
    {
        var exception = Assert.Throws<CodiDispatchConfigurationException>(() =>
            CodiRequestRegistry.FromTypes([typeof(UnlistedAnonymousQuery)]));

        Assert.Contains("AnonymousBootstrap", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FromTypes_ConNomeDellElencoNonAnonimo_RifiutaIlCatalogo()
    {
        var exception = Assert.Throws<CodiDispatchConfigurationException>(() =>
            CodiRequestRegistry.FromTypes([typeof(LoginAdminWithAdminPolicyCommand)]));

        Assert.Contains(AnonymousBootstrapRequests.LoginAdmin, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FromTypes_ConRichiestaAnonimaLocaleNonBootstrap_RifiutaIlCatalogo()
    {
        var exception = Assert.Throws<CodiDispatchConfigurationException>(() =>
            CodiRequestRegistry.FromTypes([typeof(LocalJoinCommand)]));

        Assert.Contains("locale", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FromTypes_ConBootstrapAmministrativoViaHttp_RifiutaIlCatalogo()
    {
        var exception = Assert.Throws<CodiDispatchConfigurationException>(() =>
            CodiRequestRegistry.FromTypes([typeof(BootstrapOverHttpCommand)]));

        Assert.Contains("anonima e locale", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FromAssemblies_SullAssemblyDelleRichieste_AccettaUnCatalogoCoerente()
    {
        // Il catalogo reale è ancora vuoto: la richiesta qui è che il registro non rifiuti
        // l'avvio per un assembly senza richieste e che ogni voce scoperta sia coerente.
        var registry = CodiRequestRegistry.FromAssemblies(typeof(ICodiRequest).Assembly);

        Assert.All(registry.Descriptors, descriptor =>
        {
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Name));
            Assert.NotNull(descriptor.RequestType);
            Assert.NotNull(descriptor.ResponseType);
        });
    }

    private static CodiRequestRegistry BuildRegistry() => CodiRequestRegistry.FromTypes(
    [
        typeof(EchoCommand),
        typeof(ProbeQuery),
        typeof(JoinSessionCommand),
        typeof(BootstrapAdminAccountCommand),
    ]);
}
