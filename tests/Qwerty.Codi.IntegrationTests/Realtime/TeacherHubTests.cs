using Qwerty.Codi.Application.Dto;
using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Qwerty.Codi.Api.Hubs;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Lessons;
using Qwerty.Codi.Application.Organization;
using Qwerty.Codi.Application.Sessions;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Realtime;

/// <summary>
/// Osservazione di una sessione dall'hub del docente (§11.1, §11.7): il metodo non contiene regole
/// applicative. Esegue la query <c>GetSession</c> attraverso il dispatcher — stessa area, stessa
/// policy, stessa proprietà della risorsa dell'HTTP — e solo se la lettura riesce associa la
/// connessione al gruppo dei docenti della sessione.
/// </summary>
public sealed class TeacherHubTests : IAsyncLifetime
{
    private const string ConnectionId = "connessione-docente";

    private CodiUseCaseHost _host = null!;

    private Guid _adminId;

    public async Task InitializeAsync()
    {
        _host = await CodiUseCaseHost.CreateAsync();
        _adminId = await InsertAdminUserAsync();
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Osservazione_ConSessioneDelDocente_AssociaIlGruppoDeiDocenti()
    {
        // Arrange
        var sessionId = await SeedSessionAsync();
        var groups = new FakeGroupManager();
        var hub = CreateHub(groups, ControllerContext());

        // Act
        await hub.ObserveSession(sessionId);

        // Assert
        Assert.Equal((ConnectionId, CodiHubGroups.Teachers(sessionId)), Assert.Single(groups.Added));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Osservazione_SuSessioneDiUnAltroAmministratore_RifiutaSenzaAssociareIlGruppo()
    {
        // Arrange: la sessione di un altro amministratore è indistinguibile da una inesistente, e il
        // rifiuto arriva dal caso d'uso — lo stesso che risponde all'HTTP — non da una regola dell'hub.
        var sessionId = await SeedSessionAsync();
        var groups = new FakeGroupManager();
        var hub = CreateHub(
            groups,
            new CodiRequestContext(
                isAuthenticated: true,
                [CodiPolicy.Admin, CodiPolicy.SessionController],
                adminUserId: Guid.NewGuid()));

        // Act
        var failure = await Assert.ThrowsAsync<CodiRequestException>(() => hub.ObserveSession(sessionId));

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
        Assert.Empty(groups.Added);
    }

    private TeacherHub CreateHub(FakeGroupManager groups, CodiRequestContext context) =>
        new(_host.Executor, new StubContextProvider(context))
        {
            Context = new FakeHubCallerContext(new DefaultHttpContext(), ConnectionId),
            Groups = groups,
        };

    /// <summary>Contesto di regia: la credenziale amministrativa concede anche SessionController (D3).</summary>
    private CodiRequestContext ControllerContext() =>
        new(isAuthenticated: true, [CodiPolicy.Admin, CodiPolicy.SessionController], adminUserId: _adminId);

    private async Task<Guid> SeedSessionAsync()
    {
        var school = await _host.CommandAsAsync<SchoolDto>(
            CreateSchoolCommand.RequestName, new { name = "Scuola Manzoni" }, ControllerContext());
        var classroom = await _host.CommandAsAsync<ClassroomDto>(
            CreateClassroomCommand.RequestName,
            new { schoolId = school.Id, name = "Terza A", schoolYear = "2026/2027" },
            ControllerContext());
        var course = await _host.CommandAsAsync<CourseDto>(
            CreateCourseCommand.RequestName, new { title = "Informatica di base" }, ControllerContext());
        var lesson = await _host.CommandAsAsync<LessonDto>(
            CreateLessonCommand.RequestName, new { courseId = course.Id, title = "Prima lezione" }, ControllerContext());

        var session = await _host.CommandAsAsync<SessionDto>(
            CreateSessionCommand.RequestName,
            new { lessonId = lesson.Id, classroomId = classroom.Id },
            ControllerContext());

        return session.Id;
    }

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

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
