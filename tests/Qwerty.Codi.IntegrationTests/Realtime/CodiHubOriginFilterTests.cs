using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qwerty.Codi.Api.Hubs;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Realtime;

/// <summary>
/// Verifica dell'origin della connessione (§11.2 passo 2, deroga A-17). È la difesa che non dipende
/// dal comportamento di un browser: un origin non dichiarato non apre la connessione e non può
/// invocare metodi su una connessione già aperta.
///
/// Il caso di prova verifica il filtro direttamente: il trasporto SignalR reale aggiungerebbe solo
/// rumore, e ciò che conta è la decisione — ammettere, rifiutare, chiudere.
/// </summary>
public sealed class CodiHubOriginFilterTests
{
    private const string AllowedOrigin = "https://localhost:4200";

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Connessione_ConOriginAmmesso_Prosegue()
    {
        var filter = Filter(AllowedOrigin);
        var context = CallerContext(AllowedOrigin);
        var proceeded = false;

        await filter.OnConnectedAsync(
            LifetimeContext(context),
            _ =>
            {
                proceeded = true;

                return Task.CompletedTask;
            });

        Assert.True(proceeded);
        Assert.False(context.Aborted);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Connessione_ConOriginNonAmmesso_ChiudeSenzaProseguire()
    {
        var filter = Filter(AllowedOrigin);
        var context = CallerContext("https://esempio.ostile.test");
        var proceeded = false;

        await Assert.ThrowsAsync<HubException>(() =>
            filter.OnConnectedAsync(
                LifetimeContext(context),
                _ =>
                {
                    proceeded = true;

                    return Task.CompletedTask;
                }));

        Assert.False(proceeded);
        Assert.True(context.Aborted);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Connessione_ConOriginDelloStessoServer_ProsegueSenzaElenco()
    {
        // In produzione il backend serve i bundle dallo stesso origin: la stessa origine della
        // richiesta è ammessa per definizione, anche con l'elenco vuoto.
        var filter = Filter();
        var context = CallerContext("https://localhost:5200");

        await filter.OnConnectedAsync(LifetimeContext(context), _ => Task.CompletedTask);

        Assert.False(context.Aborted);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Connessione_SenzaOrigin_Prosegue()
    {
        // Un client che non è un browser non invia `Origin`, e non ha cookie da presentare: rifiutarlo
        // non aggiungerebbe sicurezza, impedirebbe solo un collaudo da riga di comando.
        var filter = Filter(AllowedOrigin);
        var context = CallerContext(origin: null);

        await filter.OnConnectedAsync(LifetimeContext(context), _ => Task.CompletedTask);

        Assert.False(context.Aborted);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Metodo_ConOriginNonAmmesso_NonVieneEseguito()
    {
        // Una connessione già aperta non diventa una via di accesso se l'origin cambia.
        var filter = Filter(AllowedOrigin);
        var context = CallerContext("https://esempio.ostile.test");
        var executed = false;

        await Assert.ThrowsAsync<HubException>(() =>
            filter.InvokeMethodAsync(
                InvocationContext(context),
                _ =>
                {
                    executed = true;

                    return ValueTask.FromResult<object?>(null);
                }).AsTask());

        Assert.False(executed);
        Assert.True(context.Aborted);
    }

    private static CodiHubOriginFilter Filter(params string[] allowedOrigins) =>
        new(
            Options.Create(new CodiRealtimeOptions { AllowedOrigins = allowedOrigins }),
            NullLogger<CodiHubOriginFilter>.Instance);

    private static FakeHubCallerContext CallerContext(string? origin)
    {
        var httpContext = new DefaultHttpContext
        {
            Request =
            {
                Scheme = "https",
                Host = new HostString("localhost", 5200),
            },
        };

        if (origin is not null)
        {
            httpContext.Request.Headers.Origin = origin;
        }

        return new FakeHubCallerContext(httpContext, "connessione-1");
    }

    private static HubLifetimeContext LifetimeContext(HubCallerContext context) =>
        new(context, new EmptyServiceProvider(), new ParticipantHubStub());

    private static HubInvocationContext InvocationContext(HubCallerContext context) =>
        new(
            context,
            new EmptyServiceProvider(),
            new ParticipantHubStub(),
            typeof(ParticipantHubStub).GetMethod(nameof(ParticipantHubStub.Heartbeat))!,
            []);

    /// <summary>
    /// Hub senza regole: il filtro lo attraversa prima di arrivare al metodo, quindi l'oggetto reale
    /// non aggiungerebbe nulla a ciò che si verifica.
    /// </summary>
    private sealed class ParticipantHubStub : Hub
    {
        public Task Heartbeat() => Task.CompletedTask;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
