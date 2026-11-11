using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Qwerty.Codi.Api.Authentication;
using Qwerty.Codi.Api.Hubs;
using Qwerty.Codi.Application.Lessons;
using Qwerty.Codi.Application.Organization;
using Qwerty.Codi.Application.Sessions;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Realtime;

/// <summary>
/// Negoziazione degli hub sul trasporto reale (§11.1): i due percorsi usano schemi di
/// autenticazione distinti e la credenziale di un'area non autentica l'altra. La negoziazione è il
/// primo passo di una connessione: se la credenziale non è quella dell'area, la connessione non
/// nasce, esattamente come per gli endpoint del dispatcher (§9.3).
/// </summary>
public sealed class HubNegotiationTests
{
    private const string IdempotencyHeader = "Idempotency-Key";

    private static readonly string ParticipantNegotiate = $"{CodiHubs.ParticipantPath}/negotiate?negotiateVersion=1";

    private static readonly string TeacherNegotiate = $"{CodiHubs.TeacherPath}/negotiate?negotiateVersion=1";

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Negoziazione_HubPartecipante_SenzaCredenziale_RifiutaConAccessoRichiesto()
    {
        // Arrange
        await using var server = await CodiHttpServer.CreateAsync();

        // Act
        using var response = await server.SendAnonymousAsync(HttpMethod.Post, ParticipantNegotiate);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Negoziazione_HubPartecipante_ConCredenzialeDelPartecipante_Riesce()
    {
        // Arrange
        await using var server = await CodiHttpServer.CreateAsync();
        var seed = await SeedStartedSessionAsync(server);
        await JoinAsync(server, seed, "Luca");

        // Act
        using var response = await server.SendAsync(HttpMethod.Post, ParticipantNegotiate);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Negoziazione_HubPartecipante_ConCredenzialeDocente_RifiutaConAccessoRichiesto()
    {
        // Arrange: il percorso dell'hub del partecipante seleziona la credenziale dell'alunno, quindi
        // il cookie docente non autentica la connessione (§9.3, §11.1).
        await using var server = await CodiHttpServer.CreateAsync();
        await SeedStartedSessionAsync(server);

        // Act
        using var response = await server.SendAsync(HttpMethod.Post, ParticipantNegotiate);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Negoziazione_HubDocente_ConCredenzialeDocente_Riesce()
    {
        // Arrange
        await using var server = await CodiHttpServer.CreateAsync();
        await SeedStartedSessionAsync(server);

        // Act
        using var response = await server.SendAsync(HttpMethod.Post, TeacherNegotiate);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Negoziazione_HubDocente_ConCredenzialeDelPartecipante_RifiutaConAccessoRichiesto()
    {
        // Arrange
        await using var server = await CodiHttpServer.CreateAsync();
        var seed = await SeedStartedSessionAsync(server);
        await JoinAsync(server, seed, "Luca");

        // Act
        using var response = await server.SendAsync(HttpMethod.Post, TeacherNegotiate);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Catena reale con la sessione in play, creata dalla credenziale del login.</summary>
    private static async Task<SessionSeed> SeedStartedSessionAsync(CodiHttpServer server)
    {
        using var login = await server.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var schoolId = (await PostCommandAsync(server, CreateSchoolCommand.RequestName, new { name = "Scuola Manzoni" }))
            .GetProperty("id").GetGuid();
        var classroomId = (await PostCommandAsync(
                server, CreateClassroomCommand.RequestName, new { schoolId, name = "Terza A", schoolYear = "2026/2027" }))
            .GetProperty("id").GetGuid();
        var courseId = (await PostCommandAsync(server, CreateCourseCommand.RequestName, new { title = "Informatica di base" }))
            .GetProperty("id").GetGuid();
        var lessonId = (await PostCommandAsync(server, CreateLessonCommand.RequestName, new { courseId, title = "Prima lezione" }))
            .GetProperty("id").GetGuid();
        var sessionId = (await PostCommandAsync(server, CreateSessionCommand.RequestName, new { lessonId, classroomId }))
            .GetProperty("id").GetGuid();
        await PostCommandAsync(server, StartSessionCommand.RequestName, new { sessionId, expectedSessionRevision = 1L });

        return new SessionSeed(schoolId, classroomId, sessionId);
    }

    /// <summary>Ingresso dal percorso dedicato: è ciò che emette il cookie del partecipante.</summary>
    private static async Task JoinAsync(CodiHttpServer server, SessionSeed seed, string displayName)
    {
        using var bootstrap = await server.SendAnonymousAsync(HttpMethod.Get, "/api/v1/public/bootstrap");
        server.ReplaceCookies(Cookies(bootstrap));

        using var response = await server.SendWithHeaderAsync(
            HttpMethod.Post,
            "/api/v1/public/join",
            new { schoolId = seed.SchoolId, classroomId = seed.ClassroomId, sessionId = seed.SessionId, displayName },
            IdempotencyHeader,
            "chiave-di-prova");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<JsonElement> PostCommandAsync(CodiHttpServer server, string requestName, object body)
    {
        using var response = await server.SendWithHeaderAsync(
            HttpMethod.Post, $"/api/v1/admin/execute/command/{requestName}", body, "Idempotency-Key", Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Cookie emessi da una risposta, come li conserverebbe un browser.</summary>
    private static KeyValuePair<string, string>[] Cookies(HttpResponseMessage response) =>
        [
            .. response.Headers.TryGetValues("Set-Cookie", out var cookies)
                ? cookies
                    .Select(cookie => cookie.Split(';')[0].Split('=', 2))
                    .Select(pair => new KeyValuePair<string, string>(pair[0], pair[1]))
                : [],
        ];

    /// <summary>Catena scelta dal client, creata dal percorso reale.</summary>
    private sealed record SessionSeed(Guid SchoolId, Guid ClassroomId, Guid SessionId);
}
