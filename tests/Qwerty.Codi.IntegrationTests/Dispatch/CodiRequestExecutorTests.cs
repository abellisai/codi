using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Dispatch;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Dispatch;

/// <summary>
/// Esecuzione delle richieste (§4.4, §10.1): risoluzione del nome nel canale giusto, controllo
/// del canale prima della policy, policy prima dell'handler, deserializzazione del corpo e
/// propagazione del token. L'invocazione è controllata perché il progetto di test non può
/// ospitare il generatore di Mediator; ciò che si verifica qui è la parte del dispatcher, dalla
/// richiesta fino all'handler che riceve il payload deserializzato.
/// </summary>
public sealed class CodiRequestExecutorTests
{
    private static readonly Type[] ValidRequests =
    [
        typeof(EchoCommand),
        typeof(ProbeQuery),
        typeof(JoinSessionCommand),
        typeof(BootstrapAdminAccountCommand),
    ];

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConComandoValido_EsegueLHandlerEPercorreIlRegistro()
    {
        // Arrange
        await using var provider = CreateProvider();
        var (executor, log) = CreateExecutor(provider);
        using var payload = JsonDocument.Parse("""{"text":"ciao"}""");

        // Act
        var result = await executor.ExecuteAsync(
            CodiRequestKind.Command,
            "PrototypeEcho",
            CodiRequestChannel.Http,
            payload.RootElement,
            AuthenticatedContext(CodiPolicy.Admin),
            CancellationToken.None);

        // Assert
        Assert.Equal("echo:ciao", result);
        var request = Assert.IsType<EchoCommand>(Assert.Single(log.Entries));
        Assert.Equal("ciao", request.Text);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConQueryValida_RestituisceIlDtoEPropagaIlToken()
    {
        // Arrange
        await using var provider = CreateProvider();
        var (executor, log) = CreateExecutor(provider);
        using var payload = JsonDocument.Parse("""{"filter":"rossi"}""");
        using var cancellation = new CancellationTokenSource();

        // Act
        var result = await executor.ExecuteAsync(
            CodiRequestKind.Query,
            "PrototypeProbe",
            CodiRequestChannel.Http,
            payload.RootElement,
            AuthenticatedContext(CodiPolicy.Admin),
            cancellation.Token);

        // Assert
        var dto = Assert.IsType<ProbeDto>(result);
        Assert.Equal("risposta-finta", dto.Filter);
        Assert.Equal("rossi", Assert.IsType<ProbeQuery>(Assert.Single(log.Entries)).Filter);
        Assert.True(
            Assert.Single(log.Tokens).CanBeCanceled,
            "Il token della richiesta deve arrivare fino all'handler.");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConNomeSconosciuto_SollevaIlCodiceDelCanale()
    {
        // Arrange
        await using var provider = CreateProvider();
        var (executor, log) = CreateExecutor(provider);
        using var payload = JsonDocument.Parse("{}");

        // Act
        var command = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                "NonEsiste",
                CodiRequestChannel.Http,
                payload.RootElement,
                AuthenticatedContext(CodiPolicy.Admin),
                CancellationToken.None)
            .AsTask());

        var query = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Query,
                "NonEsiste",
                CodiRequestChannel.Http,
                payload.RootElement,
                AuthenticatedContext(CodiPolicy.Admin),
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.CommandNotFound, command.Code);
        Assert.Equal(CodiErrorCodes.QueryNotFound, query.Code);

        // Il messaggio nomina la richiesta ricevuta ma non i nomi validi: da un errore non si
        // ricostruisce il catalogo (§10.7).
        Assert.Contains("NonEsiste", command.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("PrototypeEcho", command.Message, StringComparison.Ordinal);
        Assert.Empty(log.Entries);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConQuerySulCanaleDeiComandi_NonLaTrova()
    {
        // Arrange
        await using var provider = CreateProvider();
        var (executor, _) = CreateExecutor(provider);
        using var payload = JsonDocument.Parse("{}");

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                "PrototypeProbe",
                CodiRequestChannel.Http,
                payload.RootElement,
                AuthenticatedContext(CodiPolicy.Admin),
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.CommandNotFound, exception.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SenzaContestoAutenticato_RifiutaPrimaDellHandler()
    {
        // Arrange
        await using var provider = CreateProvider();
        var (executor, log) = CreateExecutor(provider);
        using var payload = JsonDocument.Parse("""{"text":"ciao"}""");

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                "PrototypeEcho",
                CodiRequestChannel.Http,
                payload.RootElement,
                CodiRequestContext.Anonymous,
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.AuthenticationRequired, exception.Code);
        Assert.Empty(log.Entries);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConContestoAutenticatoSenzaPolicy_RifiutaPrimaDellHandler()
    {
        // Arrange
        await using var provider = CreateProvider();
        var (executor, log) = CreateExecutor(provider);
        using var payload = JsonDocument.Parse("""{"text":"ciao"}""");

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                "PrototypeEcho",
                CodiRequestChannel.Http,
                payload.RootElement,
                AuthenticatedContext(CodiPolicy.AdmittedParticipant),
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.PermissionDenied, exception.Code);
        Assert.Empty(log.Entries);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConContestoAnonimo_SuRichiestaAnonima_Esegue()
    {
        // Arrange
        await using var provider = CreateProvider();
        var (executor, log) = CreateExecutor(provider);
        using var payload = JsonDocument.Parse("""{"displayName":"Luca"}""");

        // Act
        var result = await executor.ExecuteAsync(
            CodiRequestKind.Command,
            AnonymousBootstrapRequests.JoinSession,
            CodiRequestChannel.Http,
            payload.RootElement,
            CodiRequestContext.Anonymous,
            CancellationToken.None);

        // Assert
        Assert.Equal("joined:Luca", result);
        Assert.Equal("Luca", Assert.IsType<JoinSessionCommand>(Assert.Single(log.Entries)).DisplayName);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConRichiestaLocale_ViaHttpNonEsisteEViaCanaleLocaleEsegue()
    {
        // Arrange
        await using var provider = CreateProvider();
        var (executor, log) = CreateExecutor(provider);
        using var payload = JsonDocument.Parse("""{"username":"Maestra"}""");

        // Act: la richiesta locale non si distingue da un nome inesistente sul canale HTTP.
        var viaHttp = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                AnonymousBootstrapRequests.BootstrapAdminAccount,
                CodiRequestChannel.Http,
                payload.RootElement,
                CodiRequestContext.Anonymous,
                CancellationToken.None)
            .AsTask());

        var viaLocale = await executor.ExecuteAsync(
            CodiRequestKind.Command,
            AnonymousBootstrapRequests.BootstrapAdminAccount,
            CodiRequestChannel.Local,
            payload.RootElement,
            CodiRequestContext.Anonymous,
            CancellationToken.None);

        // Assert
        Assert.Equal(CodiErrorCodes.CommandNotFound, viaHttp.Code);
        Assert.Equal("bootstrapped:Maestra", viaLocale);
        Assert.Equal(
            "Maestra",
            Assert.IsType<BootstrapAdminAccountCommand>(Assert.Single(log.Entries)).Username);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConCorpoNonOggetto_SollevaInvalidPayload()
    {
        // Arrange
        await using var provider = CreateProvider();
        var (executor, log) = CreateExecutor(provider);
        using var payload = JsonDocument.Parse("""["ciao"]""");

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                "PrototypeEcho",
                CodiRequestChannel.Http,
                payload.RootElement,
                AuthenticatedContext(CodiPolicy.Admin),
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.InvalidPayload, exception.Code);
        Assert.Empty(log.Entries);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConCorpoIncompleto_SollevaInvalidPayload()
    {
        // Arrange
        await using var provider = CreateProvider();
        var (executor, log) = CreateExecutor(provider);
        using var payload = JsonDocument.Parse("{}");

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                "PrototypeEcho",
                CodiRequestChannel.Http,
                payload.RootElement,
                AuthenticatedContext(CodiPolicy.Admin),
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.InvalidPayload, exception.Code);
        Assert.Empty(log.Entries);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConCampoFacoltativoOmesso_ConsegnaLaRichiestaConValorePredefinito()
    {
        // Arrange: `ProbeQuery` dà un valore predefinito al filtro, quindi il campo è facoltativo
        // e può mancare. La regola è misurata: la sola nullabilità non basta, perché un campo
        // annullabile senza valore predefinito resta obbligatorio.
        await using var provider = CreateProvider();
        var (executor, log) = CreateExecutor(provider);
        using var payload = JsonDocument.Parse("{}");

        // Act
        var result = await executor.ExecuteAsync(
            CodiRequestKind.Query,
            "PrototypeProbe",
            CodiRequestChannel.Http,
            payload.RootElement,
            AuthenticatedContext(CodiPolicy.Admin),
            CancellationToken.None);

        // Assert
        Assert.IsType<ProbeDto>(result);
        Assert.Null(Assert.IsType<ProbeQuery>(Assert.Single(log.Entries)).Filter);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SenzaMediatorRegistrato_SollevaUnErroreEsplicito()
    {
        // Arrange: descrittori reali, quindi invocazione che passa dal mediator, ma contenitore
        // senza mediator. È il guasto di composizione che il dispatcher deve rendere evidente
        // invece di fallire in modo opaco.
        var registry = CodiRequestRegistry.FromTypes(ValidRequests);
        await using var services = new ServiceCollection().BuildServiceProvider();
        var executor = new CodiRequestExecutor(registry, services);
        using var payload = JsonDocument.Parse("""{"text":"ciao"}""");

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                "PrototypeEcho",
                CodiRequestChannel.Http,
                payload.RootElement,
                AuthenticatedContext(CodiPolicy.Admin),
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Contains("mediator", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static DispatcherFixture CreateProvider() => new();

    private static (CodiRequestExecutor Executor, RecognizedInvocations Log) CreateExecutor(
        DispatcherFixture provider) =>
        (provider.Executor, provider.Received);

    private static CodiRequestContext AuthenticatedContext(CodiPolicy policy) =>
        new(isAuthenticated: true, [policy]);

    /// <summary>
    /// Catalogo esplicito di descrittori con invocazioni controllate: il percorso verificato è
    /// quello del dispatcher, dal nome pubblico fino alla delega, senza dipendere da come il
    /// generatore di Mediator registra gli handler nel contenitore.
    /// </summary>
    private sealed class DispatcherFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();

        public DispatcherFixture()
        {
            var echo = new RecordingInvoker("echo:ciao");
            var probe = new RecordingInvoker(new ProbeDto("risposta-finta", false));
            var join = new RecordingInvoker("joined:Luca");
            var bootstrap = new RecordingInvoker("bootstrapped:Maestra");

            Received = new RecognizedInvocations([echo, probe, join, bootstrap]);

            var registry = CodiRequestRegistry.FromDescriptors(
            [
                Descriptor("PrototypeEcho", CodiRequestKind.Command, CodiPolicy.Admin, typeof(EchoCommand), typeof(string), echo),
                Descriptor("PrototypeProbe", CodiRequestKind.Query, CodiPolicy.Admin, typeof(ProbeQuery), typeof(ProbeDto), probe),
                Descriptor(AnonymousBootstrapRequests.JoinSession, CodiRequestKind.Command, CodiPolicy.AnonymousBootstrap, typeof(JoinSessionCommand), typeof(string), join),
                Descriptor(AnonymousBootstrapRequests.BootstrapAdminAccount, CodiRequestKind.Command, CodiPolicy.AnonymousBootstrap, typeof(BootstrapAdminAccountCommand), typeof(string), bootstrap, isLocalOnly: true),
            ]);

            Executor = new CodiRequestExecutor(registry, _services);
        }

        public RecognizedInvocations Received { get; }

        public CodiRequestExecutor Executor { get; }

        public ValueTask DisposeAsync() => _services.DisposeAsync();

        private static CodiRequestDescriptor Descriptor(
            string name,
            CodiRequestKind kind,
            CodiPolicy policy,
            Type requestType,
            Type responseType,
            ICodiRequestInvoker invoker,
            bool isLocalOnly = false) =>
            new(name, kind, policy, requestType, responseType, isLocalOnly, invoker);
    }

    /// <summary>Vista aggregata di ciò che è arrivato a tutte le invocazioni controllate.</summary>
    private sealed class RecognizedInvocations(IReadOnlyList<RecordingInvoker> invokers)
    {
        public IReadOnlyList<object> Entries =>
            [.. invokers.SelectMany(invoker => invoker.Requests)];

        public IReadOnlyList<CancellationToken> Tokens =>
            [.. invokers.SelectMany(invoker => invoker.Tokens)];
    }
}
