using Qwerty.Codi.Application.Dto;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Lessons;
using Qwerty.Codi.Application.Organization;
using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.Domain.Sessions;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Sessions;

/// <summary>
/// Scoperta pubblica di §10.3: le scuole con ingressi disponibili, le classi accessibili di una
/// scuola e la sessione in play di una classe. Le tre letture non passano dal catalogo del
/// dispatcher — precedono il join, quindi non hanno un contesto autenticato a cui applicare una
/// policy — e si invocano per tipo, come fa l'endpoint dedicato; qui si verifica quindi il
/// percorso reale fra Mediator, handler, repository e database migrato, più il trasporto HTTP
/// anonimo.
///
/// La regola centrale è quella di §6.1: una sessione è <b>in play</b> se è <b>avviata</b>
/// (<c>Active</c> oppure <c>Paused</c>) <b>e</b> gli ingressi sono aperti. La pausa sospende
/// l'attività, non l'ingresso: un alunno che arriva durante una spiegazione vede ancora la lezione
/// e può entrare. Una sessione in <c>Lobby</c> o <c>Ended</c>, o con ingressi chiusi, non compare
/// nella scoperta. I test la verificano da entrambe le condizioni, così l'omissione di una delle
/// due li fa fallire.
/// </summary>
public sealed class SessionDiscoveryTests : IAsyncLifetime
{
    private CodiUseCaseHost _host = null!;
    private IMediator _mediator = null!;

    public async Task InitializeAsync()
    {
        _host = await CodiUseCaseHost.CreateAsync();
        _mediator = _host.Services.GetRequiredService<IMediator>();
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    /// <summary>
    /// Gli stati di sessione che non sono «in play»: non ancora avviata, conclusa, oppure con gli
    /// ingressi chiusi. La pausa non c'è: sospende l'attività, non l'ingresso.
    /// </summary>
    public static TheoryData<string> StatiNonInPlay() => new()
    {
        "Lobby",
        "Ended",
        "IngressiChiusi",
        "PausedIngressiChiusi",
    };

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAvailableSchools_ConSessioneInPlay_RestituisceLaScuola()
    {
        // Arrange
        var fixture = await PrepareAsync();
        await ArrangeSessionAsync(fixture, "Active");

        // Act
        var schools = await ListAvailableSchoolsAsync();

        // Assert: una sola scuola in play, con l'identificativo e il nome del contratto pubblico.
        var school = Assert.Single(schools);
        Assert.Equal(fixture.SchoolId, school.Id);
        Assert.Equal("Scuola Manzoni", school.Name);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAvailableSchools_ConSessioneInPausa_RestituisceLaScuola()
    {
        // Arrange: la pausa sospende l'attività, non l'ingresso. Chi arriva durante una
        // spiegazione deve poter ancora vedere la lezione e entrare (§6.1, §10.3).
        var fixture = await PrepareAsync();
        await ArrangeSessionAsync(fixture, "Paused");

        // Act
        var schools = await ListAvailableSchoolsAsync();
        var play = await GetClassroomPlaySessionAsync(fixture.ClassroomId);

        // Assert: la scuola resta disponibile e la sessione in pausa è quella in play.
        var school = Assert.Single(schools);
        Assert.Equal(fixture.SchoolId, school.Id);
        Assert.Equal(fixture.LessonId, play.LessonId);
    }

    [Theory]
    [MemberData(nameof(StatiNonInPlay))]
    [Trait("Category", "Integration")]
    public async Task ListAvailableSchools_ConSessioneNonInPlay_NonRestituisceLaScuola(string state)
    {
        // Arrange: la scuola esiste, la classe è attiva, ma la sessione non è in play.
        var fixture = await PrepareAsync();
        await ArrangeSessionAsync(fixture, state);

        // Act
        var schools = await ListAvailableSchoolsAsync();

        // Assert
        Assert.Empty(schools);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAvailableSchools_ConScuolaArchiviata_NonRestituisceLaScuola()
    {
        // Arrange: la sessione è in play, ma la scuola è archiviata: resta lo storico, non la scoperta.
        var fixture = await PrepareAsync();
        await ArrangeSessionAsync(fixture, "Active");
        await ArchiveSchoolAsync(fixture.AdminId, fixture.SchoolId);

        // Act
        var schools = await ListAvailableSchoolsAsync();

        // Assert
        Assert.Empty(schools);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAvailableSchools_SenzaSessioni_RestituisceElencoVuoto()
    {
        // Arrange: scuola e classe esistono, ma nessuna sessione è stata istanziata.
        await PrepareAsync();

        // Act
        var schools = await ListAvailableSchoolsAsync();

        // Assert
        Assert.Empty(schools);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAvailableSchools_ConDueScuoleInPlay_OrdinaPerNome()
    {
        // Arrange: due scuole distinte, ciascuna con la propria classe in play.
        var adminId = await InsertAdminUserAsync();
        var lesson = await CreateLessonAsync(adminId, "Informatica di base", "Prima lezione");
        var zeta = await CreateSchoolAsync(adminId, "Zeta");
        var alfa = await CreateSchoolAsync(adminId, "Alfa");
        var zetaClassroom = await CreateClassroomAsync(adminId, zeta.Id, "2ª A");
        var alfaClassroom = await CreateClassroomAsync(adminId, alfa.Id, "2ª B");
        await StartSessionInPlayAsync(adminId, lesson.Id, zetaClassroom.Id);
        await StartSessionInPlayAsync(adminId, lesson.Id, alfaClassroom.Id);

        // Act
        var schools = await ListAvailableSchoolsAsync();

        // Assert: l'ordine è per nome senza distinzione di maiuscole, stabile.
        Assert.Equal(new[] { "Alfa", "Zeta" }, schools.Select(school => school.Name));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAvailableClassrooms_ConSessioneInPlay_RestituisceSoloLaClasseAccessibile()
    {
        // Arrange: due classi della stessa scuola, ma solo una con una sessione in play.
        var fixture = await PrepareAsync();
        var otherClassroom = await CreateClassroomAsync(fixture.AdminId, fixture.SchoolId, "3ª B");
        await ArrangeSessionAsync(fixture, "Active");
        await ArrangeSessionAsync(
            new Fixture(fixture.AdminId, fixture.SchoolId, otherClassroom.Id, fixture.LessonId, fixture.CourseId),
            "Lobby");

        // Act
        var classrooms = await ListAvailableClassroomsAsync(fixture.SchoolId);

        // Assert
        var classroom = Assert.Single(classrooms);
        Assert.Equal(fixture.ClassroomId, classroom.Id);
        Assert.Equal(fixture.SchoolId, classroom.SchoolId);
        Assert.Equal("2ª A", classroom.Name);
        Assert.Equal("2026/2027", classroom.SchoolYear);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAvailableClassrooms_ConClasseArchiviata_NonRestituisceLaClasse()
    {
        // Arrange: la sessione è in play, ma la classe è archiviata.
        var fixture = await PrepareAsync();
        await ArrangeSessionAsync(fixture, "Active");
        await ArchiveClassroomAsync(fixture.AdminId, fixture.ClassroomId);

        // Act
        var classrooms = await ListAvailableClassroomsAsync(fixture.SchoolId);

        // Assert
        Assert.Empty(classrooms);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAvailableClassrooms_ConScuolaInesistente_RestituisceElencoVuoto()
    {
        // Arrange: esiste una classe in play, ma in un'altra scuola.
        var fixture = await PrepareAsync();
        await ArrangeSessionAsync(fixture, "Active");

        // Act: una scuola inesistente dà lo stesso esito di una scuola senza classi accessibili.
        var classrooms = await ListAvailableClassroomsAsync(Guid.NewGuid());

        // Assert
        Assert.Empty(classrooms);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAvailableClassrooms_ConClassiDiAltreScuole_RestituisceSoloQuelleDellaScuola()
    {
        // Arrange: due scuole, entrambe con una classe in play.
        var adminId = await InsertAdminUserAsync();
        var lesson = await CreateLessonAsync(adminId, "Informatica di base", "Prima lezione");
        var first = await CreateSchoolAsync(adminId, "Scuola Manzoni");
        var second = await CreateSchoolAsync(adminId, "Scuola Pascoli");
        var firstClassroom = await CreateClassroomAsync(adminId, first.Id, "2ª A");
        var secondClassroom = await CreateClassroomAsync(adminId, second.Id, "2ª B");
        await StartSessionInPlayAsync(adminId, lesson.Id, firstClassroom.Id);
        await StartSessionInPlayAsync(adminId, lesson.Id, secondClassroom.Id);

        // Act
        var classrooms = await ListAvailableClassroomsAsync(first.Id);

        // Assert
        var classroom = Assert.Single(classrooms);
        Assert.Equal(firstClassroom.Id, classroom.Id);
        Assert.Equal(first.Id, classroom.SchoolId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListAvailableClassrooms_ConAnniScolasticiDiversi_OrdinaPerAnnoDecrescente()
    {
        // Arrange: due classi in play, una dell'anno in corso e una del precedente.
        var fixture = await PrepareAsync();
        var previousYear = await CreateClassroomAsync(fixture.AdminId, fixture.SchoolId, "1ª A", "2025/2026");
        await ArrangeSessionAsync(fixture, "Active");
        await ArrangeSessionAsync(
            new Fixture(fixture.AdminId, fixture.SchoolId, previousYear.Id, fixture.LessonId, fixture.CourseId),
            "Active");

        // Act
        var classrooms = await ListAvailableClassroomsAsync(fixture.SchoolId);

        // Assert: prima l'anno più recente.
        Assert.Equal(
            new[] { "2026/2027", "2025/2026" },
            classrooms.Select(classroom => classroom.SchoolYear));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetClassroomPlaySession_ConSessioneInPlay_RestituisceCorsoLezioneEIdentificativi()
    {
        // Arrange
        var fixture = await PrepareAsync();
        var session = await ArrangeSessionAsync(fixture, "Active");

        // Act
        var play = await GetClassroomPlaySessionAsync(fixture.ClassroomId);

        // Assert: la catena scuola → classe → sessione con la lezione e il corso risolti dal dominio.
        Assert.Equal(fixture.SchoolId, play.SchoolId);
        Assert.Equal(fixture.ClassroomId, play.ClassroomId);
        Assert.Equal(session.Id, play.SessionId);
        Assert.Equal(fixture.LessonId, play.LessonId);
        Assert.Equal(fixture.CourseId, play.CourseId);
        Assert.Equal("Informatica di base", play.CourseTitle);
        Assert.Equal("Prima lezione", play.LessonTitle);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetClassroomPlaySession_ConSessioneInLobby_RestituisceRisorsaNonTrovata()
    {
        // Arrange: la classe esiste, ma non ha ancora una sessione in play.
        var fixture = await PrepareAsync();
        await ArrangeSessionAsync(fixture, "Lobby");

        // Act: la risorsa è singolare — «la» sessione in play — e la sua assenza è un 404.
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => GetClassroomPlaySessionRawAsync(fixture.ClassroomId));

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, exception.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetClassroomPlaySession_ConClasseInesistente_RestituisceRisorsaNonTrovata()
    {
        // Arrange
        var fixture = await PrepareAsync();
        await ArrangeSessionAsync(fixture, "Active");

        // Act: l'identificativo sbagliato è indistinguibile da una classe senza sessione in play.
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => GetClassroomPlaySessionRawAsync(Guid.NewGuid()));

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, exception.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetClassroomPlaySession_ConClasseArchiviata_RestituisceRisorsaNonTrovata()
    {
        // Arrange: la sessione è in play, ma la classe è archiviata e non è più accessibile.
        var fixture = await PrepareAsync();
        await ArrangeSessionAsync(fixture, "Active");
        await ArchiveClassroomAsync(fixture.AdminId, fixture.ClassroomId);

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => GetClassroomPlaySessionRawAsync(fixture.ClassroomId));

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, exception.Code);
    }

    [Theory]
    [InlineData("Active", true)]
    [InlineData("Paused", true)]
    [InlineData("Lobby", false)]
    [InlineData("Ended", false)]
    [Trait("Category", "Integration")]
    public async Task IsLessonInPlay_SegueLaRegolaDellaLezioneInPlay(string state, bool expected)
    {
        // Arrange: la stessa regola delle altre letture pubbliche — avviata, in corso o in pausa,
        // con gli ingressi aperti — ma la risposta è un solo booleano.
        var fixture = await PrepareAsync();
        await ArrangeSessionAsync(fixture, state);

        // Act
        var presence = await IsLessonInPlayAsync();

        // Assert
        Assert.Equal(expected, presence.InPlay);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task IsLessonInPlay_ConIngressiChiusi_RispondeFalso()
    {
        // Arrange: la sessione è avviata ma non accetta ingressi: non è «in play» (§6.1).
        var fixture = await PrepareAsync();
        var session = await ArrangeSessionAsync(fixture, "Active");
        await CloseAdmissionsAsync(session.Id);

        // Act
        var presence = await IsLessonInPlayAsync();

        // Assert
        Assert.False(presence.InPlay);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task IsLessonInPlay_SenzaSessioni_RispondeFalso()
    {
        // Act
        var presence = await IsLessonInPlayAsync();

        // Assert
        Assert.False(presence.InPlay);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ScopertaPubblica_RichiesteAnonime_RispondonoSenzaCredenzialeNeAntiforgery()
    {
        // Arrange: i dati nascono dal percorso HTTP amministrativo reale, le letture sono anonime.
        await using var server = await CodiHttpServer.CreateAsync();
        using var login = await server.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var schoolId = (await PostCommandAsync(server, CreateSchoolCommand.RequestName, new { name = "Scuola Manzoni" }))
            .GetProperty("id").GetGuid();
        var classroomId = (await PostCommandAsync(
            server, CreateClassroomCommand.RequestName, new { schoolId, name = "2ª A", schoolYear = "2026/2027" }))
            .GetProperty("id").GetGuid();
        var courseId = (await PostCommandAsync(server, CreateCourseCommand.RequestName, new { title = "Informatica di base" }))
            .GetProperty("id").GetGuid();
        var lessonId = (await PostCommandAsync(
            server, CreateLessonCommand.RequestName, new { courseId, title = "Prima lezione" }))
            .GetProperty("id").GetGuid();
        var sessionId = (await PostCommandAsync(server, CreateSessionCommand.RequestName, new { lessonId, classroomId }))
            .GetProperty("id").GetGuid();
        var started = await PostCommandAsync(
            server, StartSessionCommand.RequestName, new { sessionId, expectedSessionRevision = 1L });
        Assert.Equal(nameof(SessionStatus.Active), started.GetProperty("status").GetString());

        // Act: le tre letture senza cookie e senza token antiforgery. La scoperta precede il join,
        // quindi non esiste ancora una credenziale; un GET non richiede nemmeno antiforgery.
        using var schoolsResponse = await server.SendAnonymousAsync(HttpMethod.Get, "/api/v1/public/schools");
        using var classroomsResponse = await server.SendAnonymousAsync(
            HttpMethod.Get, $"/api/v1/public/schools/{schoolId}/classrooms");
        using var sessionResponse = await server.SendAnonymousAsync(
            HttpMethod.Get, $"/api/v1/public/classrooms/{classroomId}/sessions");

        // Assert: 200 su tutte e tre, senza autenticazione.
        Assert.Equal(HttpStatusCode.OK, schoolsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, classroomsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);

        var schools = await schoolsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, schools.GetArrayLength());
        Assert.Equal(schoolId, schools[0].GetProperty("id").GetGuid());
        Assert.Equal("Scuola Manzoni", schools[0].GetProperty("name").GetString());

        var classrooms = await classroomsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, classrooms.GetArrayLength());
        Assert.Equal(classroomId, classrooms[0].GetProperty("id").GetGuid());
        Assert.Equal("2ª A", classrooms[0].GetProperty("name").GetString());

        var play = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(sessionId, play.GetProperty("sessionId").GetGuid());
        Assert.Equal("Informatica di base", play.GetProperty("courseTitle").GetString());
        Assert.Equal("Prima lezione", play.GetProperty("lessonTitle").GetString());

        // Nessun dato di partecipante: l'insieme esatto delle proprietà è la prova più stretta.
        // La scoperta precede il join, quindi chi è collegato non deve essere rivelato; un campo
        // aggiunto al DTO — per esempio un elenco di partecipanti — fa fallire queste asserzioni.
        Assert.Equal(new[] { "id", "name" }, JsonPropertyNames(schools[0]));
        Assert.Equal(new[] { "id", "name", "schoolId", "schoolYear" }, JsonPropertyNames(classrooms[0]));
        Assert.Equal(
            new[]
            {
                "classroomId", "courseId", "courseTitle", "lessonId", "lessonTitle", "schoolId", "sessionId",
            },
            JsonPropertyNames(play));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Catalogo_NonContieneLeRichiesteDiScopertaPubblica()
    {
        // Arrange: il registro reale dell'assembly dell'applicazione, costruito come all'avvio.
        var registry = _host.Services.GetRequiredService<CodiRequestRegistry>();

        // Assert: il catalogo del dispatcher resta quello di §10.4–§10.5. Le richieste di scoperta
        // implementano soltanto IQuery<T> di Mediator, che non deriva da IRequest<T>: il registro
        // le ignora, quindi non sono invocabili per nome e non richiedono una policy.
        // I conteggi comprendono le richieste dell'autoraggio di T6 — `SaveExerciseDraft`,
        // `GetExerciseDraft`, `ValidateExerciseDraft`, `PublishExerciseVersion`,
        // `CopyExerciseVersionToDraft`, `GetReferenceSolution`, `ListExerciseVersions` e
        // `GetExerciseVersion` — che sono nomi di §10.4–§10.5 e non richieste di trasporto.
        // `JoinSession` e `GetParticipantContext` chiudono la prima parte di T3: l'elenco chiuso di
        // `AnonymousBootstrap` non ha più nomi senza tipo, quindi la scoperta è completa.
        // `LeaveSession` è l'uscita volontaria dell'alunno (§10.3), il primo comando della seconda
        // parte di T3 esposto dal catalogo; `OpenAdmissions`, `CloseAdmissions`, `UpdateParticipant`
        // ed `ExcludeParticipant` completano la regia dei partecipanti; `ShowWaiting` è il primo
        // comando che cambia lo stato desiderato della classe. `GetCommandStatus` legge lo stato
        // delle sue consegne. L'handshake del player aggiunge il comando
        // `InitializeParticipantConnection` (§10.4) e la query `GetParticipantSnapshot` (§10.5),
        // che rilegge lo stato corrente senza acquisire di nuovo l'operatività; la conferma
        // applicativa aggiunge `AcknowledgeCommand` (§10.4), il comando con cui il client dice che
        // cosa ha applicato di ciò che ha ricevuto. `AssignExercise` (§10.4) apre i comandi che
        // assegnano materiale e creano il tentativo dell'alunno; `EnableActivity` accende i
        // controlli dell'attività già assegnata senza avviarla, e `RunProgram`, `RestartExecution`
        // e `ResetWork` sono i tre comandi di esecuzione. L'anteprima dei blocchi (§15.5) aggiunge
        // `PublishWorkspacePreview`, che non salva nulla e aggiorna una cache in memoria, e la query
        // `GetParticipantPreview`, con cui il docente legge il contenuto dell'alunno che ha davanti.
        // Il primo tipo dichiarativo (§13.9.1) aggiunge la query `GetQuizOutcome`, con cui l'alunno
        // legge la correzione della propria risposta: la fa il server, che è l'unico a conoscere la
        // risposta attesa. Il tipo che conserva valori (§13.9) aggiunge `GetValueProgramOutcome`, con
        // cui l'alunno legge l'esito del proprio programma: anche lì la correzione è del server, che
        // **esegue** il progetto sui casi di prova invece di ricevere un esito dal browser. La chat fra
        // il docente e il singolo alunno (§8, §15.1) aggiunge i due comandi di scrittura e le due query
        // di lettura del thread: non sono richieste di trasporto, sono nomi del catalogo.
        // `DeleteSession` (§10.4) è l'eliminazione definitiva di una sessione conclusa o mai avviata:
        // un comando di regia in più, senza query nuove. `DeleteExerciseVersion` (§10.4) elimina una
        // versione pubblicata che non è più la più recente: un comando dell'autoraggio in più.
        Assert.Equal(51, registry.Descriptors.Count(descriptor => descriptor.Kind == CodiRequestKind.Command));
        Assert.Equal(32, registry.Descriptors.Count(descriptor => descriptor.Kind == CodiRequestKind.Query));
        Assert.Empty(registry.MissingAnonymousBootstrapRequests);

        Assert.DoesNotContain(
            registry.Descriptors, descriptor => descriptor.RequestType == typeof(ListAvailableSchoolsQuery));
        Assert.DoesNotContain(
            registry.Descriptors, descriptor => descriptor.RequestType == typeof(ListAvailableClassroomsQuery));
        Assert.DoesNotContain(
            registry.Descriptors, descriptor => descriptor.RequestType == typeof(GetClassroomPlaySessionQuery));
        Assert.DoesNotContain(
            registry.Descriptors, descriptor => descriptor.RequestType == typeof(IsLessonInPlayQuery));
        Assert.Null(registry.Find(CodiRequestKind.Query, "ListAvailableSchools"));
    }

    /// <summary>Scuola, classe e lezione create dal percorso reale, pronte per una sessione.</summary>
    private sealed record Fixture(Guid AdminId, Guid SchoolId, Guid ClassroomId, Guid LessonId, Guid CourseId);

    private async Task<Fixture> PrepareAsync()
    {
        var adminId = await InsertAdminUserAsync();
        var school = await CreateSchoolAsync(adminId, "Scuola Manzoni");
        var classroom = await CreateClassroomAsync(adminId, school.Id, "2ª A");
        var lesson = await CreateLessonAsync(adminId, "Informatica di base", "Prima lezione");

        return new Fixture(adminId, school.Id, classroom.Id, lesson.Id, lesson.CourseId);
    }

    /// <summary>
    /// Crea una sessione nella classe e la porta nello stato richiesto dal caso di prova. Lo stato
    /// è quello del prodotto, con una sola eccezione dichiarata: nessun caso d'uso produce
    /// <c>Active</c> con ingressi chiusi, ma la regola di §6.1 richiede <b>entrambe</b> le
    /// condizioni, quindi il test deve poterle separare.
    /// </summary>
    private async Task<SessionDto> ArrangeSessionAsync(Fixture fixture, string state)
    {
        var session = await CreateSessionAsync(fixture.AdminId, fixture.LessonId, fixture.ClassroomId);
        Assert.Equal(nameof(SessionStatus.Lobby), session.Status);
        Assert.False(session.AdmissionsOpen);

        switch (state)
        {
            case "Lobby":
                break;

            case "Active":
                session = await StartSessionAsync(fixture.AdminId, session);
                Assert.Equal(nameof(SessionStatus.Active), session.Status);
                Assert.True(session.AdmissionsOpen);
                break;

            case "Paused":
                session = await PauseSessionAsync(fixture.AdminId, await StartSessionAsync(fixture.AdminId, session));
                Assert.Equal(nameof(SessionStatus.Paused), session.Status);
                break;

            case "Ended":
                session = await EndSessionAsync(fixture.AdminId, await StartSessionAsync(fixture.AdminId, session));
                Assert.Equal(nameof(SessionStatus.Ended), session.Status);
                Assert.False(session.AdmissionsOpen);
                break;

            case "PausedIngressiChiusi":
                session = await PauseSessionAsync(fixture.AdminId, await StartSessionAsync(fixture.AdminId, session));
                await CloseAdmissionsAsync(session.Id);
                break;

            case "IngressiChiusi":
                session = await StartSessionAsync(fixture.AdminId, session);
                await CloseAdmissionsAsync(session.Id);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, "Stato non previsto dal test.");
        }

        return session;
    }

    private async Task StartSessionInPlayAsync(Guid adminId, Guid lessonId, Guid classroomId)
    {
        var session = await CreateSessionAsync(adminId, lessonId, classroomId);
        var started = await StartSessionAsync(adminId, session);
        Assert.Equal(nameof(SessionStatus.Active), started.Status);
        Assert.True(started.AdmissionsOpen);
    }

    private async Task<IReadOnlyList<AvailableSchoolDto>> ListAvailableSchoolsAsync() =>
        Assert.IsAssignableFrom<IReadOnlyList<AvailableSchoolDto>>(
            await _mediator.Send(new ListAvailableSchoolsQuery(), CancellationToken.None));

    private async Task<IReadOnlyList<AvailableClassroomDto>> ListAvailableClassroomsAsync(Guid schoolId) =>
        Assert.IsAssignableFrom<IReadOnlyList<AvailableClassroomDto>>(
            await _mediator.Send(new ListAvailableClassroomsQuery(schoolId), CancellationToken.None));

    private async Task<PlaySessionDto> GetClassroomPlaySessionAsync(Guid classroomId) =>
        Assert.IsType<PlaySessionDto>(
            await _mediator.Send(new GetClassroomPlaySessionQuery(classroomId), CancellationToken.None));

    private Task GetClassroomPlaySessionRawAsync(Guid classroomId) =>
        _mediator.Send(new GetClassroomPlaySessionQuery(classroomId), CancellationToken.None).AsTask();

    private async Task<LessonInPlayDto> IsLessonInPlayAsync() =>
        Assert.IsType<LessonInPlayDto>(
            await _mediator.Send(new IsLessonInPlayQuery(), CancellationToken.None));

    private Task<SchoolDto> CreateSchoolAsync(Guid adminId, string name) =>
        CommandAsControllerAsync<SchoolDto>(adminId, CreateSchoolCommand.RequestName, new { name });

    private Task<ClassroomDto> CreateClassroomAsync(
        Guid adminId, Guid schoolId, string name, string schoolYear = "2026/2027") =>
        CommandAsControllerAsync<ClassroomDto>(
            adminId, CreateClassroomCommand.RequestName, new { schoolId, name, schoolYear });

    private async Task<LessonDto> CreateLessonAsync(Guid adminId, string courseTitle, string lessonTitle)
    {
        var course = await CommandAsControllerAsync<CourseDto>(
            adminId, CreateCourseCommand.RequestName, new { title = courseTitle });

        return await CommandAsControllerAsync<LessonDto>(
            adminId, CreateLessonCommand.RequestName, new { courseId = course.Id, title = lessonTitle });
    }

    private Task<SessionDto> CreateSessionAsync(Guid adminId, Guid lessonId, Guid classroomId) =>
        CommandAsControllerAsync<SessionDto>(adminId, CreateSessionCommand.RequestName, new { lessonId, classroomId });

    private Task<SessionDto> StartSessionAsync(Guid adminId, SessionDto session) =>
        CommandAsControllerAsync<SessionDto>(
            adminId,
            StartSessionCommand.RequestName,
            new { sessionId = session.Id, expectedSessionRevision = session.SessionRevision });

    private Task<SessionDto> PauseSessionAsync(Guid adminId, SessionDto session) =>
        CommandAsControllerAsync<SessionDto>(
            adminId,
            PauseCommand.RequestName,
            new { sessionId = session.Id, expectedSessionRevision = session.SessionRevision });

    private Task<SessionDto> EndSessionAsync(Guid adminId, SessionDto session) =>
        CommandAsControllerAsync<SessionDto>(
            adminId,
            EndSessionCommand.RequestName,
            new { sessionId = session.Id, expectedSessionRevision = session.SessionRevision });

    private Task<SchoolDto> ArchiveSchoolAsync(Guid adminId, Guid schoolId) =>
        CommandAsControllerAsync<SchoolDto>(
            adminId, ArchiveSchoolCommand.RequestName, new { id = schoolId, baseRowVersion = 1L });

    private Task<ClassroomDto> ArchiveClassroomAsync(Guid adminId, Guid classroomId) =>
        CommandAsControllerAsync<ClassroomDto>(
            adminId, ArchiveClassroomCommand.RequestName, new { id = classroomId, baseRowVersion = 1L });

    /// <summary>
    /// Chiude gli ingressi di una sessione attiva agendo sulla riga: è l'unico modo di separare le
    /// due condizioni di §6.1, perché nessun caso d'uso del prodotto produce <c>Active</c> con
    /// ingressi chiusi. L'aggiornamento è verificato dal numero di righe toccate.
    /// </summary>
    private async Task CloseAdmissionsAsync(Guid sessionId)
    {
        await using var connection = await _host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE lesson_session SET admissions_open = 0 WHERE id = $id;";
        AddParameter(command, "$id", sessionId.ToString("D"));

        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    /// <summary>
    /// Comandi eseguiti con il contesto di regia: la credenziale amministrativa concede anche
    /// <c>SessionController</c>, che i comandi di sessione richiedono (§9.3).
    /// </summary>
    private async Task<T> CommandAsControllerAsync<T>(Guid adminId, string name, object body)
    {
        using var payload = CodiUseCaseHost.Payload(body);

        var result = await _host.Executor.ExecuteAsync(
            CodiRequestKind.Command,
            name,
            CodiRequestChannel.Http,
            payload.RootElement,
            ControllerContext(adminId),
            CancellationToken.None);

        return Assert.IsAssignableFrom<T>(result);
    }

    /// <summary>Contesto di regia: la credenziale amministrativa concede anche SessionController (D3).</summary>
    private static CodiRequestContext ControllerContext(Guid adminId) =>
        new(isAuthenticated: true, [CodiPolicy.Admin, CodiPolicy.SessionController], adminUserId: adminId);

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

    private static async Task<JsonElement> PostCommandAsync(CodiHttpServer server, string requestName, object body)
    {
        using var response = await server.SendWithHeaderAsync(
            HttpMethod.Post, $"/api/v1/admin/execute/command/{requestName}", body, "Idempotency-Key", Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Nomi delle proprietà di un oggetto JSON, ordinati: il contratto pubblico, per intero.</summary>
    private static string[] JsonPropertyNames(JsonElement element) =>
        [.. element.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal)];

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
