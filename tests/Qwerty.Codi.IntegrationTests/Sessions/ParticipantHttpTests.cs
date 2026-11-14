using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Api.Authentication;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Lessons;
using Qwerty.Codi.Application.Organization;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Application.Sessions;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Sessions;

/// <summary>
/// Ingresso e contesto dell'alunno sul trasporto reale (§9.4, §10.3): bootstrap pubblico che emette
/// il ticket di ammissione, ingresso che emette il cookie del partecipante, profilo del contesto.
/// I test verificano anche il confine fra le due aree: la credenziale docente non autentica l'area
/// pubblica anche quando i due cookie convivono nello stesso browser (§9.3).
/// </summary>
public sealed class ParticipantHttpTests
{
    private const string IdempotencyHeader = "Idempotency-Key";

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Bootstrap_AreaPubblica_EmettteIlTicketDiAmmissione()
    {
        // Arrange
        await using var server = await CodiHttpServer.CreateAsync();

        // Act
        using var response = await server.SendAnonymousAsync(HttpMethod.Get, "/api/v1/public/bootstrap");

        // Assert: il ticket è un cookie protetto, non leggibile dal client, e c'è accanto al token
        // antiforgery: senza di esso un ingresso ripetuto non avrebbe uno scope (§9.4).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith($"{AdmissionCookie.CookieName}=", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Join_SenzaBootstrap_RifiutaConDatiNonValidi()
    {
        // Arrange: la catena dei dati nasce dal percorso amministrativo reale.
        await using var server = await CodiHttpServer.CreateAsync();
        var session = await SeedSessionAsync(server);

        // Act: nessun cookie di ammissione, quindi nessuno scope per l'ingresso.
        using var response = await server.SendWithHeaderAsync(
            HttpMethod.Post,
            "/api/v1/public/join",
            Body(session, "Luca"),
            IdempotencyHeader,
            "chiave-di-prova");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ValidationFailed", problem.GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Join_ConBootstrap_EmettteLaCredenzialeEUnProfiloMinimo()
    {
        // Arrange
        await using var server = await CodiHttpServer.CreateAsync();
        var session = await SeedSessionAsync(server);
        using var bootstrap = await server.SendAnonymousAsync(HttpMethod.Get, "/api/v1/public/bootstrap");
        server.ReplaceCookies(Cookies(bootstrap));

        // Act
        using var response = await server.SendWithHeaderAsync(
            HttpMethod.Post,
            "/api/v1/public/join",
            Body(session, "  Luca  "),
            IdempotencyHeader,
            "chiave-di-prova");

        // Assert: il cookie del partecipante è emesso, il ticket di ammissione è **svuotato** nella
        // stessa risposta (il trasporto scrive la cancellazione, il browser la applica) e la risposta
        // non contiene la marca di sicurezza: la risposta è un profilo minimo, registrabile.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith($"{ParticipantCookieAuthentication.CookieName}=", StringComparison.Ordinal));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Luca", body.GetProperty("displayName").GetString());
        Assert.Equal("Waiting", body.GetProperty("view").GetString());
        Assert.Equal(session.SessionId, body.GetProperty("sessionId").GetGuid());
        Assert.Equal(
            new[] { "displayName", "participantId", "sessionId", "view" },
            body.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));

        Assert.Equal(string.Empty, server.Cookies[AdmissionCookie.CookieName]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Join_RipetutoConLoStessoTicket_RestituisceLaStessaPartecipazione()
    {
        // Arrange: il doppio invio di «Entra» — due richieste partite prima che la prima risposta
        // sia applicata, quindi con lo **stesso** ticket di ammissione e senza credenziale di
        // partecipante. È il caso per cui esiste il registro di idempotenza (§9.4).
        await using var server = await CodiHttpServer.CreateAsync();
        var session = await SeedSessionAsync(server);
        using var bootstrap = await server.SendAnonymousAsync(HttpMethod.Get, "/api/v1/public/bootstrap");
        var ticket = Cookies(bootstrap);
        server.ReplaceCookies(ticket);

        using var first = await server.SendWithHeaderAsync(
            HttpMethod.Post, "/api/v1/public/join", Body(session, "Luca"), IdempotencyHeader, "chiave-di-prova");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();

        // Il secondo invio riparte dallo stesso ticket: la credenziale emessa dalla prima risposta
        // non è ancora di questo invio.
        server.ReplaceCookies(ticket);

        // Act
        using var second = await server.SendWithHeaderAsync(
            HttpMethod.Post, "/api/v1/public/join", Body(session, "Luca"), IdempotencyHeader, "chiave-di-prova");

        // Assert: stessa partecipazione, non una seconda; la credenziale viene riemessa perché
        // quella registrata è ancora utilizzabile, così il secondo invio entra davvero.
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains(
            second.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith($"{ParticipantCookieAuthentication.CookieName}=", StringComparison.Ordinal));

        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(firstBody.GetProperty("participantId").GetGuid(), secondBody.GetProperty("participantId").GetGuid());
        Assert.Equal("Luca", secondBody.GetProperty("displayName").GetString());
        Assert.Equal("Waiting", secondBody.GetProperty("view").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Join_ConCredenzialeDiPartecipante_RifiutaConPermessoMancante()
    {
        // Arrange: un browser già entrato. L'ingresso è ammesso solo a un contesto anonimo con un
        // ticket di ammissione (§9.3): chi ha la credenziale usa `GetParticipantContext`, non ripete
        // l'ingresso, e il rifiuto è dichiarato invece di creare una seconda partecipazione.
        await using var server = await CodiHttpServer.CreateAsync();
        var session = await SeedSessionAsync(server);
        await JoinAsync(server, session, "Luca");

        // Act
        using var response = await server.SendWithHeaderAsync(
            HttpMethod.Post, "/api/v1/public/join", Body(session, "Luca"), IdempotencyHeader, "chiave-di-prova");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SessionPermissionDenied", problem.GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Join_AncheDalDispatcherGenerico_EsegueLoStessoComando()
    {
        // Arrange: §10.1 ammette `JoinSession` nell'area pubblica, quindi la seconda porta non
        // aggiunge un ingresso diverso: esegue lo stesso caso d'uso con lo stesso contesto.
        await using var server = await CodiHttpServer.CreateAsync();
        var session = await SeedSessionAsync(server);
        using var bootstrap = await server.SendAnonymousAsync(HttpMethod.Get, "/api/v1/public/bootstrap");
        server.ReplaceCookies(Cookies(bootstrap));

        // Act
        using var response = await server.SendWithHeaderAsync(
            HttpMethod.Post,
            $"/api/v1/public/execute/command/{JoinSessionCommand.RequestName}",
            Body(session, "Luca"),
            IdempotencyHeader,
            "chiave-di-prova");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Luca", body.GetProperty("joined").GetProperty("displayName").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Join_ConIlCodiceDiIngresso_EmetteLaCredenzialeDelPartecipante()
    {
        // Arrange: il percorso ordinario dell'aula — bootstrap, codice detto a voce, nome — senza
        // che il client conosca scuola, classe o sessione.
        await using var server = await CodiHttpServer.CreateAsync();
        var session = await SeedSessionAsync(server);
        using var bootstrap = await server.SendAnonymousAsync(HttpMethod.Get, "/api/v1/public/bootstrap");
        server.ReplaceCookies(Cookies(bootstrap));

        // Act
        using var response = await server.SendWithHeaderAsync(
            HttpMethod.Post, "/api/v1/public/join", CodeBody(session, "  Luca  "), IdempotencyHeader, "chiave-di-prova");

        // Assert: la credenziale arriva dal cookie, la risposta resta un profilo minimo.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith($"{ParticipantCookieAuthentication.CookieName}=", StringComparison.Ordinal));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Luca", body.GetProperty("displayName").GetString());
        Assert.Equal(session.SessionId, body.GetProperty("sessionId").GetGuid());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Join_ConCodiceErrato_RifiutaSenzaRivelareSessioni()
    {
        // Arrange
        await using var server = await CodiHttpServer.CreateAsync();
        var session = await SeedSessionAsync(server);
        using var bootstrap = await server.SendAnonymousAsync(HttpMethod.Get, "/api/v1/public/bootstrap");
        server.ReplaceCookies(Cookies(bootstrap));

        // Act: un codice ammissibile ma inesistente.
        var wrong = session.JoinCode[..^1] + (session.JoinCode[^1] == '9' ? '8' : (char)(session.JoinCode[^1] + 1));
        using var response = await server.SendWithHeaderAsync(
            HttpMethod.Post, "/api/v1/public/join", new { joinCode = wrong, displayName = "Luca" }, IdempotencyHeader, "chiave-di-prova");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("InvalidJoinCode", problem.GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InPlay_SenzaCredenziale_DichiaraLaLezioneInCorso()
    {
        // Arrange: la lettura è anonima e non rivela quale lezione sia, né in quale classe.
        await using var server = await CodiHttpServer.CreateAsync();
        await SeedSessionAsync(server);

        // Act
        using var response = await server.SendAnonymousAsync(HttpMethod.Get, "/api/v1/public/in-play");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("inPlay").GetBoolean());
        Assert.False(body.TryGetProperty("sessionId", out _));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Profilo_SenzaCredenziale_RifiutaConAccessoRichiesto()
    {
        // Arrange
        await using var server = await CodiHttpServer.CreateAsync();

        // Act
        using var response = await server.SendAnonymousAsync(HttpMethod.Get, "/api/v1/public/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Profilo_ConCredenzialeDelPartecipante_RestituisceIlContesto()
    {
        // Arrange
        await using var server = await CodiHttpServer.CreateAsync();
        var session = await SeedSessionAsync(server);
        await JoinAsync(server, session, "Luca");

        // Act
        using var response = await server.SendAsync(HttpMethod.Get, "/api/v1/public/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Luca", body.GetProperty("displayName").GetString());
        Assert.Equal("Admitted", body.GetProperty("admissionStatus").GetString());
        Assert.Equal("Waiting", body.GetProperty("view").GetString());
        Assert.Equal("Active", body.GetProperty("sessionStatus").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Antiforgery_DocenteEAlunnoNelloStessoBrowser_ConservaTokenDistinti()
    {
        await using var server = await CodiHttpServer.CreateAsync();
        var session = await SeedSessionAsync(server);
        var adminToken = server.Cookies[CodiAntiforgery.CookieName];
        using var bootstrap = await server.SendAsync(HttpMethod.Get, "/api/v1/public/bootstrap");
        using var joined = await server.SendWithHeaderAsync(HttpMethod.Post,
            "/api/v1/public/join", Body(session, "Alunno T4"), IdempotencyHeader, "shared-browser");
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
        Assert.Equal(adminToken, server.Cookies[CodiAntiforgery.CookieName]);
        Assert.NotEqual(adminToken, server.Cookies[CodiAntiforgery.PublicCookieName]);
        using var admin = await server.SendAsync(HttpMethod.Post,
            "/api/v1/admin/execute/command/CreateSchool", new { name = "Dopo ingresso alunno" });
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        using var participant = await server.SendAsync(HttpMethod.Post,
            "/api/v1/public/execute/query/GetParticipantSnapshot", new { });
        Assert.Equal(HttpStatusCode.OK, participant.StatusCode);
        using var leave = await server.SendAsync(HttpMethod.Post,
            "/api/v1/public/participant/leave", new { });
        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
        Assert.Equal(adminToken, server.Cookies[CodiAntiforgery.CookieName]);
        using var afterLeave = await server.SendAsync(HttpMethod.Post,
            "/api/v1/admin/execute/query/ListSchools", new { });
        Assert.Equal(HttpStatusCode.OK, afterLeave.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Profilo_ConCredenzialeDocente_RifiutaConAccessoRichiesto()
    {
        // Arrange: nell'area pubblica la credenziale docente non autentica, e il cookie del
        // partecipante non autentica l'area docente: due schemi, nessuna fusione (§9.3).
        await using var server = await CodiHttpServer.CreateAsync();
        using var login = await server.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // Act
        using var response = await server.SendAsync(HttpMethod.Get, "/api/v1/public/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Profilo_ConSessioneTerminata_RifiutaConAccessoRichiesto()
    {
        // Arrange: la policy del partecipante vale per una sessione non terminata (§9.3), quindi la
        // chiusura toglie il contesto anche se il cookie resta nel browser. Regia e postazione sono
        // due browser distinti, con due credenziali: il PC dell’insegnante e quello dell'alunno.
        await using var server = await CodiHttpServer.CreateAsync();
        var session = await SeedSessionAsync(server);
        var regia = server.Cookies.ToArray();

        await JoinAsync(server, session, "Luca");
        var partecipante = server.Cookies.ToArray();

        server.ReplaceCookies(regia);
        using var end = await server.SendWithHeaderAsync(
            HttpMethod.Post,
            $"/api/v1/admin/execute/command/{EndSessionCommand.RequestName}",
            new { sessionId = session.SessionId, expectedSessionRevision = 2L }, "Idempotency-Key", "end-1");
        Assert.Equal(HttpStatusCode.Accepted, end.StatusCode);

        server.ReplaceCookies(partecipante);

        // Act
        using var response = await server.SendAsync(HttpMethod.Get, "/api/v1/public/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Uscita_ConCredenziale_RevocaLaCredenzialeECancellaIlCookie()
    {
        // Arrange
        await using var server = await CodiHttpServer.CreateAsync();
        var session = await SeedSessionAsync(server);
        await JoinAsync(server, session, "Luca");

        // Act
        using var leave = await server.SendAsync(HttpMethod.Post, "/api/v1/public/participant/leave");

        // Assert: la credenziale è revocata e il cookie rimosso dalla stessa risposta; la lettura
        // del contesto che segue non è più autenticata, quindi il browser torna alla pagina di
        // ingresso (§9.2).
        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
        Assert.Equal(string.Empty, server.Cookies[ParticipantCookieAuthentication.CookieName]);

        using var profile = await server.SendAsync(HttpMethod.Get, "/api/v1/public/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Uscita_SenzaCredenziale_RifiutaConAccessoRichiesto()
    {
        // Arrange
        await using var server = await CodiHttpServer.CreateAsync();

        // Act
        using var response = await server.SendAnonymousAsync(
            HttpMethod.Post, "/api/v1/public/participant/leave");

        // Assert: uscire è un'azione di chi è dentro; senza credenziale non c'è nessuna uscita da
        // registrare, e la policy `AdmittedParticipant` risponde 401 (§9.3, §10.3).
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Uscita_RipetutaConLaStessaCredenziale_RifiutaConAccessoRichiesto()
    {
        // Arrange
        await using var server = await CodiHttpServer.CreateAsync();
        var session = await SeedSessionAsync(server);
        await JoinAsync(server, session, "Luca");
        var credenziale = server.Cookies.ToArray();

        using var first = await server.SendAsync(HttpMethod.Post, "/api/v1/public/participant/leave");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        // Un secondo invio partito con la stessa credenziale non riapre la revoca: ripetere l'uscita
        // non è un recupero come la ripetizione dell'ingresso, perché la revoca è una decisione
        // (§9.2, §9.4).
        server.ReplaceCookies(credenziale);

        // Act
        using var second = await server.SendAsync(HttpMethod.Post, "/api/v1/public/participant/leave");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Uscita_NascondeLaPartecipazioneSenzaCancellarla()
    {
        // Arrange
        await using var server = await CodiHttpServer.CreateAsync();
        var session = await SeedSessionAsync(server);
        await JoinAsync(server, session, "Luca");

        using var profile = await server.SendAsync(HttpMethod.Get, "/api/v1/public/auth/me");
        var body = await profile.Content.ReadFromJsonAsync<JsonElement>();
        var participantId = body.GetProperty("participantId").GetGuid();

        // Act
        using var leave = await server.SendAsync(HttpMethod.Post, "/api/v1/public/participant/leave");
        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);

        // La riga resta nello storico, ma non concede più l'ammissione.
        await using var work = await server.Services
            .GetRequiredService<IUnitOfWorkContext>()
            .CreateAsync(cancellationToken: CancellationToken.None);
        var row = await work.Repo<ISessionParticipantRepository>()
            .GetAsync(participantId.ToString("D"), CancellationToken.None);

        Assert.NotNull(row);
        Assert.False(row!.ToDomain().IsAdmitted);
        Assert.NotNull(row.LeftAtUtcMs);
        Assert.Null(row.ExcludedAtUtcMs);
        using var login = await server.LoginAsync();
        using var list = await server.SendAsync(HttpMethod.Post, "/api/v1/admin/execute/query/ListSessionParticipants", new { sessionId = session.SessionId });
        var listed = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(0, listed.GetArrayLength());
        using var overview = await server.SendAsync(HttpMethod.Post, "/api/v1/admin/execute/query/GetSessionOverview", new { sessionId = session.SessionId });
        var state = await overview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, state.GetProperty("participants").GetArrayLength());
        await JoinAsync(server, session, "Luca");
        using var newProfile = await server.SendAsync(HttpMethod.Get, "/api/v1/public/auth/me");
        var next = await newProfile.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(participantId, next.GetProperty("participantId").GetGuid());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Uscita_AncheDalDispatcherGenerico_RevocaLaCredenziale()
    {
        // Arrange: §10.1 ammette i comandi dell'area pubblica anche dal dispatcher generico, che
        // però non tocca il cookie. La credenziale revocata è comunque inutilizzabile (§9.2), quindi
        // il contesto successivo non è autenticato anche se il browser conserva il ticket.
        await using var server = await CodiHttpServer.CreateAsync();
        var session = await SeedSessionAsync(server);
        await JoinAsync(server, session, "Luca");

        // Act
        using var response = await server.SendAsync(
            HttpMethod.Post, $"/api/v1/public/execute/command/{LeaveSessionCommand.RequestName}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await response.Content.ReadFromJsonAsync<bool>());

        using var profile = await server.SendAsync(HttpMethod.Get, "/api/v1/public/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
    }

    /// <summary>
    /// Catena reale dal percorso amministrativo: scuola, classe, corso, lezione, sessione avviata.
    /// La regia è autorizzata dalla credenziale del login, come nel servizio vero.
    /// </summary>
    private static async Task<SessionSeed> SeedSessionAsync(CodiHttpServer server)
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
        var started = await PostCommandAsync(server, StartSessionCommand.RequestName, new { sessionId, expectedSessionRevision = 1L });
        // Il codice di ingresso arriva con la ricevuta dell'avvio: è quello che il docente detta.
        var joinCode = started.GetProperty("joinCode").GetString();
        Assert.NotNull(joinCode);

        return new SessionSeed(schoolId, classroomId, sessionId, joinCode);
    }

    /// <summary>Ingresso dal percorso dedicato, con il bootstrap che precede: è il flusso reale.</summary>
    private static async Task JoinAsync(CodiHttpServer server, SessionSeed session, string displayName)
    {
        using var bootstrap = await server.SendAnonymousAsync(HttpMethod.Get, "/api/v1/public/bootstrap");
        server.ReplaceCookies(Cookies(bootstrap));

        using var response = await server.SendWithHeaderAsync(
            HttpMethod.Post, "/api/v1/public/join", Body(session, displayName), IdempotencyHeader, "chiave-di-prova");

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

    private static object Body(SessionSeed session, string displayName) => new
    {
        schoolId = session.SchoolId,
        classroomId = session.ClassroomId,
        sessionId = session.SessionId,
        displayName,
    };

    /// <summary>Corpo dell'ingresso con il codice: la forma ordinaria dell'aula (§9.4).</summary>
    private static object CodeBody(SessionSeed session, string displayName) => new
    {
        joinCode = session.JoinCode,
        displayName,
    };

    /// <summary>Catena scelta dal client, creata dal percorso reale, con il codice di ingresso.</summary>
    private sealed record SessionSeed(Guid SchoolId, Guid ClassroomId, Guid SessionId, string JoinCode);
}
