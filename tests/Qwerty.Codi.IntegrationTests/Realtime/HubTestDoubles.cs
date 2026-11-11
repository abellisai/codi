using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Qwerty.Codi.Api.Dispatch;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.IntegrationTests.Realtime;

/// <summary>
/// Contesto di connessione minimo per provare un hub senza un trasporto SignalR reale: la
/// connessione ha un identificativo, un <see cref="HttpContext"/> da cui si risolve la credenziale
/// e un registro di annullamento osservabile. Serve a verificare la logica di apertura e di gruppo,
/// che la negoziazione HTTP non esercita.
/// </summary>
internal sealed class FakeHubCallerContext : HubCallerContext
{
    private readonly IFeatureCollection _features;

    public FakeHubCallerContext(HttpContext httpContext, string connectionId)
    {
        ConnectionId = connectionId;
        _features = new FeatureCollection();
        _features.Set<IHttpContextFeature>(new HttpContextFeature(httpContext));
    }

    public override string ConnectionId { get; }

    public override string? UserIdentifier => null;

    public override ClaimsPrincipal? User => GetHttpContext().User;

    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

    public override IFeatureCollection Features => _features;

    public override CancellationToken ConnectionAborted => CancellationToken.None;

    /// <summary>Vero se la connessione è stata chiusa dal server, che è quanto l'hub deve fare.</summary>
    public bool Aborted { get; private set; }

    public override void Abort() => Aborted = true;

    private HttpContext GetHttpContext() => _features.Get<IHttpContextFeature>()!.HttpContext!;
}

/// <summary>
/// Feature che SignalR usa per esporre l'<see cref="HttpContext"/> della connessione: è ciò che
/// l'estensione <c>HubCallerContext.GetHttpContext()</c> legge.
/// </summary>
internal sealed class HttpContextFeature(HttpContext httpContext) : IHttpContextFeature
{
    public HttpContext? HttpContext { get; set; } = httpContext;
}

/// <summary>Registro dei gruppi in memoria: registra le aggiunte per poterle verificare.</summary>
internal sealed class FakeGroupManager : IGroupManager
{
    public List<(string ConnectionId, string GroupName)> Added { get; } = [];

    public List<(string ConnectionId, string GroupName)> Removed { get; } = [];

    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        Added.Add((connectionId, groupName));

        return Task.CompletedTask;
    }

    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        Removed.Add((connectionId, groupName));

        return Task.CompletedTask;
    }
}

/// <summary>
/// Risolutore di contesto dichiarato dal caso di prova: l'hub riceve il contesto che il trasporto
/// avrebbe verificato, senza costruire un cookie.
/// </summary>
internal sealed class StubContextProvider(CodiRequestContext context) : ICodiRequestContextProvider
{
    public Task<CodiRequestContext> ResolveRequestContextAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        Task.FromResult(context);
}

/// <summary>
/// Orologio che il test sposta in avanti. Serve alle regole che si esprimono in durate — presenza
/// degradata, autorizzazione scaduta, pagina precedente non più operativa — che con un tempo fisso si
/// potrebbero verificare solo aspettando.
/// </summary>
internal sealed class AdjustableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public void Advance(TimeSpan amount) => _now += amount;

    public override DateTimeOffset GetUtcNow() => _now;
}

/// <summary>
/// Client di un hub in memoria: registra che cosa il server ha inviato a chi. Serve ai casi in cui
/// contano l'evento e il destinatario — uno snapshot, un rifiuto, una connessione superata — non il
/// contenuto del trasporto SignalR.
/// </summary>
internal sealed class FakeHubClients : IHubCallerClients, IHubClients
{
    public List<(string Target, string Method, object?[] Arguments)> Sent { get; } = [];

    public IClientProxy Caller => Proxy("caller");

    public IClientProxy Others => Proxy("others");

    public IClientProxy All => Proxy("all");

    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy("all-except");

    public IClientProxy Client(string connectionId) => Proxy($"connection:{connectionId}");

    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy("connections");

    public IClientProxy Group(string groupName) => Proxy($"group:{groupName}");

    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) =>
        Proxy($"group-except:{groupName}");

    public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy("groups");

    public IClientProxy OthersInGroup(string groupName) => Proxy($"others-in-group:{groupName}");

    public IClientProxy OthersInGroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) =>
        Proxy($"others-in-group-except:{groupName}");

    public IClientProxy User(string userId) => Proxy($"user:{userId}");

    public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy("users");

    /// <summary>Metodi inviati a un destinatario, nell'ordine di invio.</summary>
    public IReadOnlyList<string> MethodsTo(string target) =>
        [.. Sent.Where(sent => sent.Target == target).Select(sent => sent.Method)];

    private IClientProxy Proxy(string target) => new FakeClientProxy(this, target);

    private sealed class FakeClientProxy(FakeHubClients owner, string target) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            owner.Sent.Add((target, method, args));

            return Task.CompletedTask;
        }
    }
}
