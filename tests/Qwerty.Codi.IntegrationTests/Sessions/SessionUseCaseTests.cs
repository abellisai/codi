using Qwerty.Codi.Application.Dto;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Lessons;
using Qwerty.Codi.Application.Organization;
using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.Domain.Sessions;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Sessions;

/// <summary>
/// Casi d'uso della sessione di lezione (§6.1, §6.4, §10.4, §10.5) attraverso il percorso reale:
/// registro, canale, policy, deserializzazione, mediator, handler, repository e database migrato.
/// Il proprietario della regia è quello del contesto verificato dal trasporto, mai un campo del
/// payload.
/// </summary>
public sealed class SessionUseCaseTests : IAsyncLifetime
{
    private CodiUseCaseHost _host = null!;

    public async Task InitializeAsync() => _host = await CodiUseCaseHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateSession_CreaLaSessioneInLobbyConIlRegistaDelContesto()
    {
        // Arrange
        var fixture = await PrepareAsync();

        // Act
        var session = await CreateSessionAsync(fixture);

        // Assert
        Assert.Equal(fixture.LessonId, session.LessonId);
        Assert.Equal(fixture.ClassroomId, session.ClassroomId);
        Assert.Equal(nameof(SessionStatus.Lobby), session.Status);
        Assert.False(session.AdmissionsOpen);
        Assert.Equal(1, session.SessionRevision);
        Assert.Null(session.StartedAtUtc);
        Assert.Equal(1, session.RowVersion);

        // Il regista è quello della credenziale: non un identificativo dichiarato nel payload.
        Assert.Equal(fixture.AdminId.ToString("D"), await ReadControllerAdminIdAsync(session.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateSession_SuUnaClasseConSessioneNonTerminata_RifiutaSenzaDuplicare()
    {
        // Arrange: una sessione avviata non è terminata, quindi la classe è già occupata.
        var fixture = await PrepareAsync();
        var first = await CreateSessionAsync(fixture);
        await StartSessionAsync(fixture, first, expectedRevision: 1);

        // Act
        using var payload = CodiUseCaseHost.Payload(new { lessonId = fixture.LessonId, classroomId = fixture.ClassroomId });
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => ExecuteAsControllerAsync(
            fixture.AdminId,
            CodiRequestKind.Command,
            CreateSessionCommand.RequestName,
            payload.RootElement).AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.SessionAlreadyOpen, exception.Code);
        Assert.Equal(1L, await CountSessionsAsync(fixture.ClassroomId));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateSession_ConLezioneOClasseInesistente_RestituisceRisorsaNonTrovata()
    {
        // Arrange
        var fixture = await PrepareAsync();

        // Act: la chiave esterna non è il modo in cui si risponde a un identificativo sbagliato.
        using var unknownLesson = CodiUseCaseHost.Payload(new { lessonId = Guid.NewGuid(), classroomId = fixture.ClassroomId });
        var missingLesson = await Assert.ThrowsAsync<CodiRequestException>(() => ExecuteAsControllerAsync(
            fixture.AdminId, CodiRequestKind.Command, CreateSessionCommand.RequestName, unknownLesson.RootElement).AsTask());

        using var unknownClassroom = CodiUseCaseHost.Payload(new { lessonId = fixture.LessonId, classroomId = Guid.NewGuid() });
        var missingClassroom = await Assert.ThrowsAsync<CodiRequestException>(() => ExecuteAsControllerAsync(
            fixture.AdminId, CodiRequestKind.Command, CreateSessionCommand.RequestName, unknownClassroom.RootElement).AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, missingLesson.Code);
        Assert.Equal(CodiErrorCodes.ResourceNotFound, missingClassroom.Code);
        Assert.Equal(0L, await CountSessionsAsync(fixture.ClassroomId));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StartSession_DaLobby_PortaInActiveEIncrementaLaRevisione()
    {
        // Arrange
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);

        // Act
        var started = await StartSessionAsync(fixture, created, expectedRevision: created.SessionRevision);

        // Assert
        Assert.Equal(nameof(SessionStatus.Active), started.Status);
        Assert.True(started.AdmissionsOpen);
        Assert.Equal(2, started.SessionRevision);
        Assert.Equal(CodiUseCaseHost.Now, started.StartedAtUtc);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StartSession_SuSessioneGiaAttiva_RifiutaConConflittoDiStato()
    {
        // Arrange
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);
        var started = await StartSessionAsync(fixture, created, expectedRevision: 1);

        // Act: la revisione attesa è corretta, ma lo stato non ammette la transizione.
        using var payload = CodiUseCaseHost.Payload(
            new { sessionId = created.Id, expectedSessionRevision = started.SessionRevision });
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => ExecuteAsControllerAsync(
            fixture.AdminId, CodiRequestKind.Command, StartSessionCommand.RequestName, payload.RootElement).AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.SessionStateConflict, exception.Code);
        Assert.Equal(2, (await ReadSessionAsync(created.Id)).SessionRevision);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StartSession_SenzaRevisioneAttesa_RifiutaSenzaAvviare()
    {
        // Arrange
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);

        // Act: il corpo non dichiara la revisione attesa.
        using var payload = CodiUseCaseHost.Payload(new { sessionId = created.Id });
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => ExecuteAsControllerAsync(
            fixture.AdminId, CodiRequestKind.Command, StartSessionCommand.RequestName, payload.RootElement).AsTask());

        // Assert: la revisione è facoltativa nel record, quindi il corpo è deserializzabile e la
        // sua assenza ha un esito proprio — 428 EntityVersionRequired di §10.2 — come i comandi
        // del CRUD che modificano una riga. La sessione resta in attesa: nessuna transizione a metà.
        Assert.Equal(CodiErrorCodes.EntityVersionRequired, exception.Code);
        var row = await ReadSessionAsync(created.Id);
        Assert.Equal(nameof(SessionStatus.Lobby), row.Status);
        Assert.Equal(1, row.SessionRevision);
        Assert.Null(row.StartedAtUtcMs);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StartSession_ConRevisioneSuperata_RifiutaConConflittoDiRevisione()
    {
        // Arrange
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);

        // Act
        using var payload = CodiUseCaseHost.Payload(new { sessionId = created.Id, expectedSessionRevision = 99L });
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => ExecuteAsControllerAsync(
            fixture.AdminId, CodiRequestKind.Command, StartSessionCommand.RequestName, payload.RootElement).AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.SessionRevisionConflict, exception.Code);
        var row = await ReadSessionAsync(created.Id);
        Assert.Equal(nameof(SessionStatus.Lobby), row.Status);
        Assert.Equal(1, row.SessionRevision);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StartSession_DiUnAltroAmministratore_RestituisceRisorsaNonTrovataEInvariaLoStato()
    {
        // Arrange
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);

        // Act: la sessione di un altro amministratore è indistinguibile da una inesistente (404).
        using var payload = CodiUseCaseHost.Payload(
            new { sessionId = created.Id, expectedSessionRevision = created.SessionRevision });
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => ExecuteAsControllerAsync(
            Guid.NewGuid(), CodiRequestKind.Command, StartSessionCommand.RequestName, payload.RootElement).AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, exception.Code);
        var row = await ReadSessionAsync(created.Id);
        Assert.Equal(nameof(SessionStatus.Lobby), row.Status);
        Assert.Equal(1, row.SessionRevision);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StartSession_SenzaIdentita_RifiutaConAccessoRichiesto()
    {
        // Arrange
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);

        // Act: il contesto anonimo non concede la policy di regia; il rifiuto precede l'handler.
        using var payload = CodiUseCaseHost.Payload(
            new { sessionId = created.Id, expectedSessionRevision = created.SessionRevision });
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => _host.Executor.ExecuteAsync(
            CodiRequestKind.Command,
            StartSessionCommand.RequestName,
            CodiRequestChannel.Http,
            payload.RootElement,
            CodiRequestContext.Anonymous,
            CancellationToken.None).AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.AuthenticationRequired, exception.Code);
        Assert.Equal(1, (await ReadSessionAsync(created.Id)).SessionRevision);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateSession_EseguitoDueVolteConsecutivamente_CreaUnaSolaSessione()
    {
        // Arrange: è il doppio clic: la seconda esecuzione non deve produrre una seconda sessione.
        var fixture = await PrepareAsync();
        var first = await CreateSessionAsync(fixture);

        // Act
        using var payload = CodiUseCaseHost.Payload(new { lessonId = fixture.LessonId, classroomId = fixture.ClassroomId });
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => ExecuteAsControllerAsync(
            fixture.AdminId, CodiRequestKind.Command, CreateSessionCommand.RequestName, payload.RootElement).AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.SessionAlreadyOpen, exception.Code);
        Assert.Equal(1L, await CountSessionsAsync(fixture.ClassroomId));
        Assert.Equal(nameof(SessionStatus.Lobby), (await ReadSessionAsync(first.Id)).Status);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StartSession_SullAreaPublica_NonEsisteESullAreaAdmin_VieneEseguito()
    {
        // Arrange: composizione HTTP reale, credenziale amministrativa del bootstrap.
        await using var server = await CodiHttpServer.CreateAsync();
        using var login = await server.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var schoolId = (await PostCommandAsync(server, "CreateSchool", new { name = "Scuola Manzoni" })).GetProperty("id").GetGuid();
        var classroomId = (await PostCommandAsync(server, "CreateClassroom", new { schoolId, name = "2ª A", schoolYear = "2026/2027" })).GetProperty("id").GetGuid();
        var courseId = (await PostCommandAsync(server, "CreateCourse", new { title = "Informatica di base" })).GetProperty("id").GetGuid();
        var lessonId = (await PostCommandAsync(server, "CreateLesson", new { courseId, title = "Prima lezione" })).GetProperty("id").GetGuid();
        var sessionId = (await PostCommandAsync(server, "CreateSession", new { lessonId, classroomId })).GetProperty("id").GetGuid();

        // Act: lo stesso comando di regia, sulle due aree.
        using var publicAttempt = await server.SendAsync(
            HttpMethod.Post,
            "/api/v1/public/execute/command/StartSession",
            new { sessionId, expectedSessionRevision = 1L });
        using var missingKey = await server.SendAsync(
            HttpMethod.Post,
            "/api/v1/admin/execute/command/StartSession",
            new { sessionId, expectedSessionRevision = 1L });
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
        using var adminAttempt = await server.SendWithHeaderAsync(
            HttpMethod.Post,
            "/api/v1/admin/execute/command/StartSession",
            new { sessionId, expectedSessionRevision = 1L }, "Idempotency-Key", "start-1");

        // Assert: fuori area il nome non esiste, anche con la credenziale valida; in area il
        // comando raggiunge l'handler e produce la transizione.
        Assert.Equal(HttpStatusCode.NotFound, publicAttempt.StatusCode);
        var problem = await publicAttempt.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(CodiErrorCodes.CommandNotFound, problem.GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.OK, adminAttempt.StatusCode);
        var started = await adminAttempt.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(nameof(SessionStatus.Active), started.GetProperty("status").GetString());
        Assert.True(started.GetProperty("admissionsOpen").GetBoolean());
        Assert.Equal("2", started.GetProperty("sessionRevision").GetString());
        using var repeated = await server.SendWithHeaderAsync(
            HttpMethod.Post,
            "/api/v1/admin/execute/command/StartSession",
            new { sessionId, expectedSessionRevision = 1L }, "Idempotency-Key", "start-1");
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(await adminAttempt.Content.ReadAsStringAsync(), await repeated.Content.ReadAsStringAsync());
    }

    /// <summary>Comandi di regia con lo stesso confine di policy, canale e revisione attesa.</summary>
    public static TheoryData<string> RegiaCommands() => new()
    {
        PauseCommand.RequestName,
        ResumeCommand.RequestName,
        EndSessionCommand.RequestName,
        OpenAdmissionsCommand.RequestName,
        CloseAdmissionsCommand.RequestName,
        ShowWaitingCommand.RequestName,
    };

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PauseSession_DaActive_PortaInPausedEIncrementaLaRevisione()
    {
        // Arrange: la pausa di classe è ammessa solo da una sessione in play.
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);
        var started = await StartSessionAsync(fixture, created, expectedRevision: created.SessionRevision);

        // Act
        var paused = await PauseSessionAsync(fixture, started, expectedRevision: started.SessionRevision);

        // Assert: stato e revisione cambiano, gli ingressi restano aperti e l'avvio è invariato.
        Assert.Equal(nameof(SessionStatus.Paused), paused.Status);
        Assert.True(paused.AdmissionsOpen);
        Assert.Equal(3, paused.SessionRevision);

        var row = await ReadSessionAsync(created.Id);
        Assert.Equal(nameof(SessionStatus.Paused), row.Status);
        Assert.Equal(1, row.AdmissionsOpen);
        Assert.Equal(3, row.SessionRevision);
        Assert.NotNull(row.StartedAtUtcMs);
        Assert.Null(row.EndedAtUtcMs);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ResumeSession_DaPaused_PortaInActiveEIncrementaLaRevisione()
    {
        // Arrange
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);
        var started = await StartSessionAsync(fixture, created, expectedRevision: created.SessionRevision);
        var paused = await PauseSessionAsync(fixture, started, expectedRevision: started.SessionRevision);

        // Act
        var resumed = await ResumeSessionAsync(fixture, paused, expectedRevision: paused.SessionRevision);

        // Assert
        Assert.Equal(nameof(SessionStatus.Active), resumed.Status);
        Assert.True(resumed.AdmissionsOpen);
        Assert.Equal(4, resumed.SessionRevision);

        var row = await ReadSessionAsync(created.Id);
        Assert.Equal(nameof(SessionStatus.Active), row.Status);
        Assert.Equal(1, row.AdmissionsOpen);
        Assert.NotNull(row.StartedAtUtcMs);
        Assert.Null(row.EndedAtUtcMs);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EndSession_DaActive_PortaInEndedEChiudeGliIngressi()
    {
        // Arrange
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);
        var started = await StartSessionAsync(fixture, created, expectedRevision: created.SessionRevision);

        // Act
        var ended = await EndSessionAsync(fixture, started, expectedRevision: started.SessionRevision);

        // Assert: la conclusione è terminale, chiude gli ingressi e registra l'istante di fine.
        Assert.Equal(nameof(SessionStatus.Ended), ended.Status);
        Assert.False(ended.AdmissionsOpen);
        Assert.Equal(3, ended.SessionRevision);

        var row = await ReadSessionAsync(created.Id);
        Assert.Equal(nameof(SessionStatus.Ended), row.Status);
        Assert.Equal(0, row.AdmissionsOpen);
        Assert.Equal(3, row.SessionRevision);
        Assert.NotNull(row.StartedAtUtcMs);
        Assert.Equal(CodiUseCaseHost.Now.ToUnixTimeMilliseconds(), row.EndedAtUtcMs!.Value);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EndSession_DaSessioneInLobby_ConcludeEStartSessionDaQuelMomentoRifiuta()
    {
        // Arrange: lo Stop è ammesso anche da Lobby, quindi senza avvio registrato (§6.1).
        // Nota: il dominio ammette una sessione Ended senza istante di avvio, ma il CHECK della
        // migrazione 0002 `(status = 'Lobby') = (started_at_utc_ms IS NULL)` pretende un avvio per
        // ogni stato diverso da Lobby. Questo test documenta il contrasto: fallisce con
        // SqliteException finché il vincolo o la regola di dominio non vengono riconciliati.
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);

        // Act
        var ended = await EndSessionAsync(fixture, created, expectedRevision: created.SessionRevision);

        // Assert
        Assert.Equal(nameof(SessionStatus.Ended), ended.Status);
        Assert.False(ended.AdmissionsOpen);
        Assert.Equal(2, ended.SessionRevision);

        var row = await ReadSessionAsync(created.Id);
        Assert.Equal(nameof(SessionStatus.Ended), row.Status);
        Assert.Equal(0, row.AdmissionsOpen);
        Assert.Null(row.StartedAtUtcMs);
        Assert.NotNull(row.EndedAtUtcMs);

        // Dopo la conclusione l'avvio, con la revisione corretta, rifiuta per stato incompatibile.
        var exception = await ExpectControllerFailureAsync(
            fixture.AdminId,
            StartSessionCommand.RequestName,
            new { sessionId = created.Id, expectedSessionRevision = ended.SessionRevision });

        Assert.Equal(CodiErrorCodes.SessionStateConflict, exception.Code);
    }

    [Theory]
    [MemberData(nameof(RegiaCommands))]
    [Trait("Category", "Integration")]
    public async Task Regia_SenzaRevisioneAttesa_RifiutaSenzaTransizione(string requestName)
    {
        // Arrange
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);

        // Act: il corpo non dichiara la revisione attesa.
        var exception = await ExpectControllerFailureAsync(
            fixture.AdminId, requestName, new { sessionId = created.Id });

        // Assert: la revisione è facoltativa nel record, quindi il corpo è deserializzabile e la
        // sua assenza ha l'esito 428 di §10.2; la sessione resta in attesa.
        Assert.Equal(CodiErrorCodes.EntityVersionRequired, exception.Code);

        var row = await ReadSessionAsync(created.Id);
        Assert.Equal(nameof(SessionStatus.Lobby), row.Status);
        Assert.Equal(1, row.SessionRevision);
        Assert.Null(row.EndedAtUtcMs);
    }

    [Theory]
    [MemberData(nameof(RegiaCommands))]
    [Trait("Category", "Integration")]
    public async Task Regia_ConRevisioneSuperata_RifiutaConConflittoDiRevisione(string requestName)
    {
        // Arrange
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);

        // Act
        var exception = await ExpectControllerFailureAsync(
            fixture.AdminId, requestName, new { sessionId = created.Id, expectedSessionRevision = 99L });

        // Assert
        Assert.Equal(CodiErrorCodes.SessionRevisionConflict, exception.Code);

        var row = await ReadSessionAsync(created.Id);
        Assert.Equal(nameof(SessionStatus.Lobby), row.Status);
        Assert.Equal(1, row.SessionRevision);
    }

    [Theory]
    [MemberData(nameof(RegiaCommands))]
    [Trait("Category", "Integration")]
    public async Task Regia_SuSessioneDiUnAltroAmministratore_RestituisceRisorsaNonTrovataEInvariaLoStato(
        string requestName)
    {
        // Arrange
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);

        // Act: la sessione di un altro amministratore è indistinguibile da una inesistente (404).
        var exception = await ExpectControllerFailureAsync(
            Guid.NewGuid(), requestName, new { sessionId = created.Id, expectedSessionRevision = 1L });

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, exception.Code);

        var row = await ReadSessionAsync(created.Id);
        Assert.Equal(nameof(SessionStatus.Lobby), row.Status);
        Assert.Equal(1, row.SessionRevision);
    }

    [Theory]
    [MemberData(nameof(RegiaCommands))]
    [Trait("Category", "Integration")]
    public async Task Regia_SenzaIdentita_RifiutaConAccessoRichiesto(string requestName)
    {
        // Arrange
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);

        // Act: il contesto anonimo non concede la policy di regia; il rifiuto precede l'handler.
        using var payload = CodiUseCaseHost.Payload(
            new { sessionId = created.Id, expectedSessionRevision = created.SessionRevision });
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => _host.Executor.ExecuteAsync(
            CodiRequestKind.Command,
            requestName,
            CodiRequestChannel.Http,
            payload.RootElement,
            CodiRequestContext.Anonymous,
            CancellationToken.None).AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.AuthenticationRequired, exception.Code);
        Assert.Equal(1, (await ReadSessionAsync(created.Id)).SessionRevision);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CloseAdmissions_DaInPlay_ChiudeGliIngressiSenzaToccareLoStato()
    {
        // Arrange: una sessione avviata ha gli ingressi aperti; chiuderli non cambia lo stato.
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);
        var started = await StartSessionAsync(fixture, created, created.SessionRevision);
        Assert.True(started.AdmissionsOpen);

        // Act
        var closed = await CloseAdmissionsAsync(fixture, started, started.SessionRevision);

        // Assert: gli ingressi si chiudono, lo stato resta Active e la revisione avanza di uno.
        Assert.False(closed.AdmissionsOpen);
        Assert.Equal(nameof(SessionStatus.Active), closed.Status);
        Assert.Equal(started.SessionRevision + 1, closed.SessionRevision);

        var row = await ReadSessionAsync(created.Id);
        Assert.Equal(0, row.AdmissionsOpen);
        Assert.Equal(nameof(SessionStatus.Active), row.Status);
        Assert.NotNull(row.StartedAtUtcMs);
        Assert.Null(row.EndedAtUtcMs);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task OpenAdmissions_DopoLaChiusura_RiapreGliIngressi()
    {
        // Arrange: gli ingressi si possono riaprire dopo l'avvio (§6.1).
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);
        var started = await StartSessionAsync(fixture, created, created.SessionRevision);
        var closed = await CloseAdmissionsAsync(fixture, started, started.SessionRevision);

        // Act
        var reopened = await OpenAdmissionsAsync(fixture, closed, closed.SessionRevision);

        // Assert
        Assert.True(reopened.AdmissionsOpen);
        Assert.Equal(closed.SessionRevision + 1, reopened.SessionRevision);
        Assert.Equal(nameof(SessionStatus.Active), reopened.Status);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CloseAdmissions_SuIngressiGiaChiusi_NonIncrementaLaRevisione()
    {
        // Arrange: in Lobby gli ingressi sono già chiusi; ripetere la chiusura non cambia nulla.
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);

        // Act
        var closed = await CloseAdmissionsAsync(fixture, created, created.SessionRevision);

        // Assert: nessuna scrittura, revisione invariata (idempotenza come l'archiviazione ripetuta).
        Assert.False(closed.AdmissionsOpen);
        Assert.Equal(1, closed.SessionRevision);
        Assert.Equal(1, (await ReadSessionAsync(created.Id)).SessionRevision);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task OpenAdmissions_SuIngressiGiaAperti_NonIncrementaLaRevisione()
    {
        // Arrange: dopo l'avvio gli ingressi sono aperti; ripetere l'apertura non cambia nulla.
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);
        var started = await StartSessionAsync(fixture, created, created.SessionRevision);

        // Act
        var opened = await OpenAdmissionsAsync(fixture, started, started.SessionRevision);

        // Assert
        Assert.True(opened.AdmissionsOpen);
        Assert.Equal(2, opened.SessionRevision);
        Assert.Equal(2, (await ReadSessionAsync(created.Id)).SessionRevision);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Adesioni_SuSessioneTerminata_RifiutaConConflittoDiStato()
    {
        // Arrange: in Ended gli ingressi restano chiusi e non si riaprono (§6.1).
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);
        var started = await StartSessionAsync(fixture, created, created.SessionRevision);
        var ended = await EndSessionAsync(fixture, started, started.SessionRevision);

        // Act
        var openFailure = await ExpectControllerFailureAsync(
            fixture.AdminId,
            OpenAdmissionsCommand.RequestName,
            new { sessionId = ended.Id, expectedSessionRevision = ended.SessionRevision });
        var closeFailure = await ExpectControllerFailureAsync(
            fixture.AdminId,
            CloseAdmissionsCommand.RequestName,
            new { sessionId = ended.Id, expectedSessionRevision = ended.SessionRevision });

        // Assert: il rifiuto è di stato, non di revisione, e la sessione resta conclusa.
        Assert.Equal(CodiErrorCodes.SessionStateConflict, openFailure.Code);
        Assert.Equal(CodiErrorCodes.SessionStateConflict, closeFailure.Code);
        Assert.Equal(nameof(SessionStatus.Ended), (await ReadSessionAsync(created.Id)).Status);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PauseSession_SuSessioneInLobby_RifiutaConConflittoDiStato()
    {
        // Arrange: la transizione Lobby → Paused non esiste, la revisione attesa è corretta.
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);

        // Act
        var exception = await ExpectControllerFailureAsync(
            fixture.AdminId,
            PauseCommand.RequestName,
            new { sessionId = created.Id, expectedSessionRevision = created.SessionRevision });

        // Assert
        Assert.Equal(CodiErrorCodes.SessionStateConflict, exception.Code);

        var row = await ReadSessionAsync(created.Id);
        Assert.Equal(nameof(SessionStatus.Lobby), row.Status);
        Assert.Equal(1, row.SessionRevision);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ResumeSession_SuSessioneActive_RifiutaConConflittoDiStato()
    {
        // Arrange: una sessione in play non è in pausa, quindi non si riprende.
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);
        var started = await StartSessionAsync(fixture, created, expectedRevision: created.SessionRevision);

        // Act
        var exception = await ExpectControllerFailureAsync(
            fixture.AdminId,
            ResumeCommand.RequestName,
            new { sessionId = started.Id, expectedSessionRevision = started.SessionRevision });

        // Assert
        Assert.Equal(CodiErrorCodes.SessionStateConflict, exception.Code);
        Assert.Equal(nameof(SessionStatus.Active), (await ReadSessionAsync(created.Id)).Status);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EndSession_SuSessioneGiaTerminata_RifiutaConConflittoDiStato()
    {
        // Arrange: Ended è terminale, una seconda conclusione non è ammessa.
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);
        var started = await StartSessionAsync(fixture, created, expectedRevision: created.SessionRevision);
        var ended = await EndSessionAsync(fixture, started, expectedRevision: started.SessionRevision);

        // Act
        var exception = await ExpectControllerFailureAsync(
            fixture.AdminId,
            EndSessionCommand.RequestName,
            new { sessionId = ended.Id, expectedSessionRevision = ended.SessionRevision });

        // Assert
        Assert.Equal(CodiErrorCodes.SessionStateConflict, exception.Code);

        var row = await ReadSessionAsync(created.Id);
        Assert.Equal(nameof(SessionStatus.Ended), row.Status);
        Assert.Equal(3, row.SessionRevision);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListSessions_SenzaSessioni_RestituisceElencoVuoto()
    {
        // Arrange: nessuna sessione istanziata.
        await PrepareAsync();

        // Act
        var sessions = await _host.QueryAsync<SessionSummaryDto>(ListSessionsQuery.RequestName, new { });

        // Assert
        Assert.Empty(sessions);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListSessions_ConPiuSessioni_OrdinaDallaPiuRecenteEComponeIlContenuto()
    {
        // Arrange: due classi, così due sessioni non terminate possono coesistere (§7.6).
        var fixture = await PrepareAsync();
        var otherClassroom = await _host.CommandAsync<ClassroomDto>(
            CreateClassroomCommand.RequestName,
            new { schoolId = fixture.SchoolId, name = "2ª B", schoolYear = "2026/2027" });

        var older = await CreateSessionAsync(fixture.AdminId, fixture.LessonId, fixture.ClassroomId);
        await SetCreatedAtAsync(older.Id, CodiUseCaseHost.Now.AddMinutes(-30).ToUnixTimeMilliseconds());
        var newer = await CreateSessionAsync(fixture.AdminId, fixture.LessonId, otherClassroom.Id);

        // Act
        var sessions = await _host.QueryAsync<SessionSummaryDto>(ListSessionsQuery.RequestName, new { });

        // Assert: prima la sessione creata più di recente, con la catena di titoli risolta.
        Assert.Equal(2, sessions.Count);
        Assert.Equal(newer.Id, sessions[0].Id);
        Assert.Equal(older.Id, sessions[1].Id);

        var first = sessions[0];
        Assert.Equal(nameof(SessionStatus.Lobby), first.Status);
        Assert.False(first.AdmissionsOpen);
        Assert.Equal(1, first.SessionRevision);
        Assert.Null(first.StartedAtUtc);
        Assert.Null(first.EndedAtUtc);
        Assert.Equal("Prima lezione", first.LessonTitle);
        Assert.Equal("Informatica di base", first.CourseTitle);
        Assert.Equal("2ª B", first.ClassroomName);
        Assert.Equal("Scuola Manzoni", first.SchoolName);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListSessions_ConPolicyAdmin_MostraLeSessioniDiTuttiGliAmministratori()
    {
        // Arrange: due amministratori del contesto hanno una sessione ciascuno in classi diverse.
        var fixture = await PrepareAsync();
        var otherAdminId = await InsertAdminUserAsync();
        var otherClassroom = await _host.CommandAsync<ClassroomDto>(
            CreateClassroomCommand.RequestName,
            new { schoolId = fixture.SchoolId, name = "2ª B", schoolYear = "2026/2027" });

        var mine = await CreateSessionAsync(fixture.AdminId, fixture.LessonId, fixture.ClassroomId);
        var theirs = await CreateSessionAsync(otherAdminId, fixture.LessonId, otherClassroom.Id);

        // Act: la query ha policy Admin e non filtra per proprietario.
        var sessions = await _host.QueryAsync<SessionSummaryDto>(ListSessionsQuery.RequestName, new { });

        // Assert
        Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, session => session.Id == mine.Id);
        Assert.Contains(sessions, session => session.Id == theirs.Id);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StartSession_GeneraIlCodiceDiIngressoEIlRegistaLoRilegge()
    {
        // Arrange: in attesa non c'è codice, perché il codice nasce con l'avvio.
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);
        Assert.Null(created.JoinCode);
        Assert.Null(await ReadJoinCodeAsync(created.Id));

        // Act
        var started = await StartSessionAsync(fixture, created, expectedRevision: created.SessionRevision);

        // Assert: otto cifre, prima cifra diversa da zero, presenti nella riga e nella lettura della regia.
        var code = started.JoinCode;
        Assert.NotNull(code);
        Assert.True(SessionJoinCode.IsWellFormed(code));
        Assert.Equal(code, await ReadJoinCodeAsync(started.Id));

        var detail = await QueryAsControllerAsync<SessionSummaryDto>(
            fixture.AdminId, GetSessionQuery.RequestName, new { sessionId = started.Id });
        Assert.Equal(code, detail.JoinCode);

        // Pausa e ripresa non cambiano il codice: non invalidano ciò che il docente ha già detto a voce.
        var paused = await PauseSessionAsync(fixture, started, started.SessionRevision);
        Assert.Equal(code, paused.JoinCode);

        var resumed = await ResumeSessionAsync(fixture, paused, paused.SessionRevision);
        Assert.Equal(code, resumed.JoinCode);

        // Dopo lo Stop il codice resta nella riga — è la memoria della lezione — ma la sessione non è
        // più in play, quindi non è più spendibile.
        var ended = await EndSessionAsync(fixture, resumed, resumed.SessionRevision);
        Assert.Equal(code, ended.JoinCode);
        Assert.Equal(nameof(SessionStatus.Ended), ended.Status);
        Assert.False(ended.AdmissionsOpen);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListSessions_MostraIlCodiceDelleSessioniAvviate()
    {
        // Arrange
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);

        // Act: prima dell'avvio il codice non c'è, dopo l'avvio è quello della sessione.
        var before = await _host.QueryAsync<SessionSummaryDto>(ListSessionsQuery.RequestName, new { });
        Assert.Null(before.Single(session => session.Id == created.Id).JoinCode);

        var started = await StartSessionAsync(fixture, created, created.SessionRevision);
        var after = await _host.QueryAsync<SessionSummaryDto>(ListSessionsQuery.RequestName, new { });

        // Assert
        Assert.Equal(started.JoinCode, after.Single(session => session.Id == started.Id).JoinCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetSession_RestituisceIlDettaglioDellaSessione()
    {
        // Arrange
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);

        // Act
        var detail = await QueryAsControllerAsync<SessionSummaryDto>(
            fixture.AdminId, GetSessionQuery.RequestName, new { sessionId = created.Id });

        // Assert
        Assert.Equal(created.Id, detail.Id);
        Assert.Equal(nameof(SessionStatus.Lobby), detail.Status);
        Assert.False(detail.AdmissionsOpen);
        Assert.Equal(1, detail.SessionRevision);
        Assert.Equal("Prima lezione", detail.LessonTitle);
        Assert.Equal("Informatica di base", detail.CourseTitle);
        Assert.Equal("2ª A", detail.ClassroomName);
        Assert.Equal("Scuola Manzoni", detail.SchoolName);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetSession_SuIdentificativoInesistente_RestituisceRisorsaNonTrovata()
    {
        // Arrange
        var fixture = await PrepareAsync();

        // Act
        var exception = await ExpectControllerQueryFailureAsync(
            fixture.AdminId, GetSessionQuery.RequestName, new { sessionId = Guid.NewGuid() });

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, exception.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetSession_DiUnAltroAmministratore_RestituisceRisorsaNonTrovata()
    {
        // Arrange: il dettaglio di una sessione altrui non è leggibile: stesso esito di inesistente.
        var fixture = await PrepareAsync();
        var created = await CreateSessionAsync(fixture);

        // Act
        var exception = await ExpectControllerQueryFailureAsync(
            Guid.NewGuid(), GetSessionQuery.RequestName, new { sessionId = created.Id });

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, exception.Code);
    }

    [Theory]
    [MemberData(nameof(RegiaCommands))]
    [Trait("Category", "Integration")]
    public async Task Regia_SuSessioneInesistente_RestituisceRisorsaNonTrovata(string requestName)
    {
        // Arrange
        var fixture = await PrepareAsync();

        // Act: un identificativo di sessione inesistente è una risorsa assente, non un conflitto.
        var exception = await ExpectControllerFailureAsync(
            fixture.AdminId, requestName, new { sessionId = Guid.NewGuid(), expectedSessionRevision = 1L });

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, exception.Code);
    }

    [Theory]
    [MemberData(nameof(RegiaCommands))]
    [Trait("Category", "Integration")]
    public async Task Regia_SullAreaPublica_NonEsiste(string requestName)
    {
        // Arrange: composizione HTTP reale, credenziale amministrativa del bootstrap.
        await using var server = await CodiHttpServer.CreateAsync();
        using var login = await server.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // Act: lo stesso nome, sull'area pubblica.
        using var response = await server.SendAsync(
            HttpMethod.Post,
            $"/api/v1/public/execute/command/{requestName}",
            new { sessionId = Guid.NewGuid(), expectedSessionRevision = 1L });

        // Assert: fuori area il nome non esiste, anche con la credenziale valida.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(CodiErrorCodes.CommandNotFound, problem.GetProperty("code").GetString());
    }

    /// <summary>
    /// Fixture condivisa: un amministratore con riga reale in <c>admin_user</c> (la chiave esterna
    /// della sessione lo richiede) e una lezione dentro una classe, creati dal percorso reale.
    /// </summary>
    private sealed record Fixture(Guid AdminId, Guid SchoolId, Guid LessonId, Guid ClassroomId);

    private async Task<Fixture> PrepareAsync()
    {
        var adminId = await InsertAdminUserAsync();
        var school = await _host.CommandAsync<SchoolDto>(CreateSchoolCommand.RequestName, new { name = "Scuola Manzoni" });
        var classroom = await _host.CommandAsync<ClassroomDto>(
            CreateClassroomCommand.RequestName, new { schoolId = school.Id, name = "2ª A", schoolYear = "2026/2027" });
        var course = await _host.CommandAsync<CourseDto>(CreateCourseCommand.RequestName, new { title = "Informatica di base" });
        var lesson = await _host.CommandAsync<LessonDto>(
            CreateLessonCommand.RequestName, new { courseId = course.Id, title = "Prima lezione" });

        return new Fixture(adminId, school.Id, lesson.Id, classroom.Id);
    }

    private async Task<SessionDto> CreateSessionAsync(Fixture fixture) =>
        await CreateSessionAsync(fixture.AdminId, fixture.LessonId, fixture.ClassroomId);

    private async Task<SessionDto> CreateSessionAsync(Guid adminId, Guid lessonId, Guid classroomId)
    {
        using var payload = CodiUseCaseHost.Payload(new { lessonId, classroomId });

        return await StartCommandAsync<SessionDto>(adminId, CreateSessionCommand.RequestName, payload.RootElement);
    }

    private async Task<SessionDto> StartSessionAsync(Fixture fixture, SessionDto session, long expectedRevision)
    {
        using var payload = CodiUseCaseHost.Payload(
            new { sessionId = session.Id, expectedSessionRevision = expectedRevision });

        return await StartCommandAsync<SessionDto>(fixture.AdminId, StartSessionCommand.RequestName, payload.RootElement);
    }

    private Task<SessionDto> PauseSessionAsync(Fixture fixture, SessionDto session, long expectedRevision) =>
        RegiaCommandAsync(fixture, PauseCommand.RequestName, session.Id, expectedRevision);

    private Task<SessionDto> ResumeSessionAsync(Fixture fixture, SessionDto session, long expectedRevision) =>
        RegiaCommandAsync(fixture, ResumeCommand.RequestName, session.Id, expectedRevision);

    private Task<SessionDto> EndSessionAsync(Fixture fixture, SessionDto session, long expectedRevision) =>
        RegiaCommandAsync(fixture, EndSessionCommand.RequestName, session.Id, expectedRevision);

    private Task<SessionDto> OpenAdmissionsAsync(Fixture fixture, SessionDto session, long expectedRevision) =>
        RegiaCommandAsync(fixture, OpenAdmissionsCommand.RequestName, session.Id, expectedRevision);

    private Task<SessionDto> CloseAdmissionsAsync(Fixture fixture, SessionDto session, long expectedRevision) =>
        RegiaCommandAsync(fixture, CloseAdmissionsCommand.RequestName, session.Id, expectedRevision);

    private async Task<SessionDto> RegiaCommandAsync(Fixture fixture, string name, Guid sessionId, long expectedRevision)
    {
        using var payload = CodiUseCaseHost.Payload(new { sessionId, expectedSessionRevision = expectedRevision });

        return await StartCommandAsync<SessionDto>(fixture.AdminId, name, payload.RootElement);
    }

    /// <summary>Esegue un comando di regia che deve essere rifiutato e restituisce l'errore applicativo.</summary>
    private async Task<CodiRequestException> ExpectControllerFailureAsync(Guid adminId, string name, object body)
    {
        using var payload = CodiUseCaseHost.Payload(body);

        return await Assert.ThrowsAsync<CodiRequestException>(
            () => ExecuteAsControllerAsync(adminId, CodiRequestKind.Command, name, payload.RootElement).AsTask());
    }

    private async Task<CodiRequestException> ExpectControllerQueryFailureAsync(Guid adminId, string name, object body)
    {
        using var payload = CodiUseCaseHost.Payload(body);

        return await Assert.ThrowsAsync<CodiRequestException>(
            () => ExecuteAsControllerAsync(adminId, CodiRequestKind.Query, name, payload.RootElement).AsTask());
    }

    private async Task<T> QueryAsControllerAsync<T>(Guid adminId, string name, object body)
    {
        using var payload = CodiUseCaseHost.Payload(body);

        return Assert.IsType<T>(await ExecuteAsControllerAsync(adminId, CodiRequestKind.Query, name, payload.RootElement));
    }

    private async Task<T> StartCommandAsync<T>(Guid adminId, string name, JsonElement payload) =>
        Assert.IsAssignableFrom<T>(await ExecuteAsControllerAsync(adminId, CodiRequestKind.Command, name, payload));

    /// <summary>Contesto di regia: la credenziale amministrativa concede anche SessionController (D3).</summary>
    private static CodiRequestContext ControllerContext(Guid adminId) =>
        new(isAuthenticated: true, [CodiPolicy.Admin, CodiPolicy.SessionController], adminUserId: adminId);

    private ValueTask<object?> ExecuteAsControllerAsync(
        Guid adminId,
        CodiRequestKind kind,
        string name,
        JsonElement payload) =>
        _host.Executor.ExecuteAsync(
            kind, name, CodiRequestChannel.Http, payload, ControllerContext(adminId), CancellationToken.None);

    /// <summary>
    /// Inserisce una riga in <c>admin_user</c>: la composizione dei casi d'uso non registra
    /// l'impronta delle password, ma la chiave esterna della sessione richiede l'amministratore.
    /// </summary>
    private async Task<Guid> InsertAdminUserAsync()
    {
        var id = Guid.NewGuid();

        await using var connection = await _host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO admin_user (
                id, username, normalized_username, password_hash, security_stamp,
                is_active, failed_login_count, created_at_utc_ms, updated_at_utc_ms)
            VALUES ($id, 'Maestra', 'MAESTRA-' || $id, 'hash-di-prova', 'stamp-di-prova', 1, 0, 1000, 1000);
            """;
        AddParameter(command, "$id", id.ToString("D"));

        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<string> ReadControllerAdminIdAsync(Guid sessionId)
    {
        await using var connection = await _host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT controller_admin_id FROM lesson_session WHERE id = $id;";
        AddParameter(command, "$id", sessionId.ToString("D"));

        return (string)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Codice di ingresso come è scritto nella riga della sessione, oppure null.</summary>
    private async Task<string?> ReadJoinCodeAsync(Guid sessionId)
    {
        await using var connection = await _host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT join_code FROM lesson_session WHERE id = $id;";
        AddParameter(command, "$id", sessionId.ToString("D"));

        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : (string)value;
    }

    private async Task<SessionRow> ReadSessionAsync(Guid sessionId)
    {
        await using var connection = await _host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT status, admissions_open, session_revision, started_at_utc_ms, ended_at_utc_ms
            FROM lesson_session WHERE id = $id;
            """;
        AddParameter(command, "$id", sessionId.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new SessionRow(
            reader.GetString(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetInt64(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4));
    }

    /// <summary>Sposta nel passato l'istante di creazione, per osservare l'ordinamento dell'elenco.</summary>
    private async Task SetCreatedAtAsync(Guid sessionId, long createdAtUtcMs)
    {
        await using var connection = await _host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE lesson_session SET created_at_utc_ms = $createdAt WHERE id = $id;";
        AddParameter(command, "$createdAt", createdAtUtcMs);
        AddParameter(command, "$id", sessionId.ToString("D"));

        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private async Task<long> CountSessionsAsync(Guid classroomId)
    {
        await using var connection = await _host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM lesson_session WHERE classroom_id = $classroomId;";
        AddParameter(command, "$classroomId", classroomId.ToString("D"));

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<JsonElement> PostCommandAsync(CodiHttpServer server, string requestName, object body)
    {
        using var response = await server.SendAsync(
            HttpMethod.Post, $"/api/v1/admin/execute/command/{requestName}", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed record SessionRow(
        string Status, long AdmissionsOpen, long SessionRevision, long? StartedAtUtcMs, long? EndedAtUtcMs);
}
