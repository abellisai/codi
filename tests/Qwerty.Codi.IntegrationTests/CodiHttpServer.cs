using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Qwerty.Codi.Api.Authentication;
using Qwerty.Codi.Api.Configuration;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Infrastructure.Persistence;

namespace Qwerty.Codi.IntegrationTests;

/// <summary>
/// Applicazione HTTP reale su socket loopback, con database temporaneo e chiavi di protezione
/// effimere: la stessa composizione di <c>Program</c>, senza certificati installati. Solo lo
/// schema HTTPS è simulato, perché i cookie protetti lo pretendono (§18.3).
///
/// I cookie si gestiscono a mano: un client che li conservasse da solo nasconderebbe proprio le
/// transizioni che i test devono osservare, come il cambio di token dopo l'accesso.
/// </summary>
internal sealed class CodiHttpServer : IAsyncDisposable
{
    private const string BootstrapPassword = "password-di-test";

    private readonly TempSqliteDatabase _database;
    private readonly WebApplication _application;
    private readonly HttpClient _client;
    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

    private CodiHttpServer(
        TempSqliteDatabase database,
        WebApplication application,
        HttpClient client,
        string mediaDirectory)
    {
        _database = database;
        _application = application;
        _client = client;
        MediaDirectory = mediaDirectory;
    }

    /// <summary>Contenitore dei servizi dell'applicazione avviata.</summary>
    public IServiceProvider Services => _application.Services;

    /// <summary>
    /// Directory degli asset di questa istanza. Serve ai casi che devono far sparire un file per
    /// mostrare che cosa risponde il server quando i byte non ci sono più: è una condizione che non
    /// si può provocare dall'esterno senza toccare il disco.
    /// </summary>
    public string MediaDirectory { get; }

    /// <summary>Password dell'account creato dal bootstrap, per i test che devono accedere.</summary>
    public static string AdminPassword => BootstrapPassword;

    /// <summary>Cookie correnti, per costruire richieste che li portano o li omettono.</summary>
    public IReadOnlyDictionary<string, string> Cookies => _cookies;

    /// <summary>Avvia l'applicazione con un database vuoto e un account amministrativo creato.</summary>
    public static async Task<CodiHttpServer> CreateAsync(Action<IServiceCollection>? configureServices = null)
    {
        var database = TempSqliteDatabase.Create();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ContentRootPath = Path.GetDirectoryName(database.Database.FilePath),
        });
        builder.Logging.ClearProviders();
        var mediaDirectory = Path.Combine(Path.GetDirectoryName(database.Database.FilePath)!, "media");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Codi:DataDirectory"] = Path.GetDirectoryName(database.Database.FilePath),
            ["Codi:MediaDirectory"] = mediaDirectory,
            ["Codi:Database:FileName"] = "codi.db",
            ["Codi:Database:BusyTimeoutMs"] = "5000",
        });
        builder.Services.AddCodiApi(builder.Configuration, builder.Environment);
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        configureServices?.Invoke(builder.Services);

        var application = builder.Build();

        // La richiesta arriva su http perché il socket di prova è in chiaro; i cookie sono
        // `Secure` e senza questo passaggio non verrebbero né emessi né riletti.
        application.Use((context, next) =>
        {
            context.Request.Scheme = "https";

            return next(context);
        });
        application.UseCodiApi();

        await application.Services
            .GetRequiredService<DatabaseBootstrap>()
            .PrepareAsync(CancellationToken.None);

        using (var scope = application.Services.CreateScope())
        {
            using var payload = JsonDocument.Parse(
                JsonSerializer.Serialize(new { username = "Maestra", password = BootstrapPassword }));

            await scope.ServiceProvider.GetRequiredService<CodiRequestExecutor>().ExecuteAsync(
                CodiRequestKind.Command,
                "BootstrapAdminAccount",
                CodiRequestChannel.Local,
                payload.RootElement,
                CodiRequestContext.Anonymous,
                CancellationToken.None);
        }

        await application.StartAsync();

        var client = new HttpClient(new HttpClientHandler { UseCookies = false })
        {
            BaseAddress = new Uri(application.Urls.Single()),
        };

        return new CodiHttpServer(database, application, client, mediaDirectory);
    }

    /// <summary>
    /// Invia una richiesta portando i cookie correnti e, per impostazione predefinita, il token
    /// antiforgery nell'intestazione. `useCookieToken: false` serve ai casi che devono mostrarne
    /// l'assenza o la falsità.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body = null,
        string? header = null,
        bool useCookieToken = true)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.TryAddWithoutValidation(
            "Cookie", string.Join("; ", _cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));

        var token = useCookieToken ? _cookies.GetValueOrDefault(CodiAntiforgery.RequestCookieName(path)) : header;
        if (token is not null)
        {
            request.Headers.Add(CodiAntiforgery.HeaderName, token);
        }

        var response = await _client.SendAsync(request);
        CaptureCookies(response);

        return response;
    }

    /// <summary>
    /// Invia un corpo che non è JSON — per esempio multipart — portando i cookie correnti e il
    /// token antiforgery, come <see cref="SendAsync"/>. Gli endpoint di §10.3 ricevono i byte di
    /// un file, non un documento JSON. `withCredential: false` serve a mostrare l'assenza di
    /// credenziale su un endpoint dedicato.
    /// </summary>
    public Task<HttpResponseMessage> SendContentAsync(
        HttpMethod method,
        string path,
        HttpContent content,
        bool withCredential = true) =>
        SendCoreAsync(method, path, content, withCredential, headerName: null, headerValue: null);

    /// <summary>
    /// Invia una richiesta aggiungendo un'intestazione arbitraria, per esempio <c>Range</c> per il
    /// download parziale di §10.3, portando i cookie correnti.
    /// </summary>
    public Task<HttpResponseMessage> SendWithHeaderAsync(
        HttpMethod method,
        string path,
        string headerName,
        string headerValue) =>
        SendCoreAsync(method, path, content: null, withCredential: true, headerName, headerValue);

    /// <summary>
    /// Invia una richiesta con un corpo e un'intestazione aggiuntiva, portando i cookie correnti:
    /// serve ai comandi ripetibili, che dichiarano la chiave di idempotenza nell'intestazione (§10.2).
    /// </summary>
    public Task<HttpResponseMessage> SendWithHeaderAsync(
        HttpMethod method,
        string path,
        object body,
        string headerName,
        string headerValue) =>
        SendCoreAsync(method, path, JsonContent.Create(body), withCredential: true, headerName, headerValue);

    private async Task<HttpResponseMessage> SendCoreAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        bool withCredential,
        string? headerName,
        string? headerValue)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };

        if (withCredential)
        {
            request.Headers.TryAddWithoutValidation(
                "Cookie", string.Join("; ", _cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));

            var token = _cookies.GetValueOrDefault(CodiAntiforgery.RequestCookieName(path));
            if (token is not null)
            {
                request.Headers.Add(CodiAntiforgery.HeaderName, token);
            }
        }

        if (headerName is not null && headerValue is not null)
        {
            request.Headers.TryAddWithoutValidation(headerName, headerValue);
        }

        var response = await _client.SendAsync(request);
        CaptureCookies(response);

        return response;
    }

    /// <summary>
    /// I cookie si aggiornano come farebbe un browser: senza questo passaggio il token emesso
    /// dall'accesso non arriverebbe alla richiesta successiva.
    /// </summary>
    private void CaptureCookies(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return;
        }

        foreach (var cookie in cookies)
        {
            var pair = cookie.Split(';')[0].Split('=', 2);
            _cookies[pair[0]] = pair[1];
        }
    }

    /// <summary>Ottiene la coppia di token antiforgery dal bootstrap pubblico (§10.3).</summary>
    public Task<HttpResponseMessage> BootstrapAsync() => SendAsync(HttpMethod.Get, "/api/v1/admin/bootstrap");

    /// <summary>
    /// Invia una richiesta senza cookie né token, come farebbe un secondo browser: serve ai casi
    /// che devono mostrare che una credenziale non basta a farsi riconoscere da un'altra sessione.
    /// </summary>
    public Task<HttpResponseMessage> SendAnonymousAsync(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _client.SendAsync(request);
    }

    /// <summary>Accede come amministratore con le credenziali del bootstrap.</summary>
    public Task<HttpResponseMessage> LoginAsync() => SendAsync(
        HttpMethod.Post, "/api/v1/admin/auth/login", new { username = "Maestra", password = BootstrapPassword });

    /// <summary>Sostituisce i cookie correnti, per riprodurre una copia anteriore alla revoca.</summary>
    public void ReplaceCookies(IEnumerable<KeyValuePair<string, string>> cookies)
    {
        _cookies.Clear();
        foreach (var cookie in cookies)
        {
            _cookies[cookie.Key] = cookie.Value;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _application.StopAsync();
        await _application.DisposeAsync();
        _database.Dispose();
    }
}
