using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Qwerty.Codi.Application.Dispatch;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Organization;

/// <summary>
/// Percorso HTTP di classi, corsi e associazioni (§10.1, §10.2, §10.7): gli stessi casi d'uso
/// visti dal trasporto, con i loro stati e i loro codici.
/// </summary>
public sealed class OrganizationHttpTests : IAsyncLifetime
{
    private CodiHttpServer _server = null!;

    public async Task InitializeAsync()
    {
        _server = await CodiHttpServer.CreateAsync();

        using var login = await _server.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CicloCompleto_ScuolaClasseECorso_RispettaGliStati()
    {
        // Arrange
        var school = await CreateSchoolAsync("Scuola Manzoni");
        var schoolId = school.GetProperty("id").GetGuid();

        // Act: la classe nasce dentro la scuola, con l'anno scritto con il trattino.
        var classroom = await SendCommandAsync(
            "CreateClassroom",
            new { schoolId, name = "  2ª A ", schoolYear = "2026-2027" });

        // Assert
        Assert.Equal("2ª A", classroom.GetProperty("name").GetString());
        Assert.Equal("2026/2027", classroom.GetProperty("schoolYear").GetString());
        Assert.Equal("1", classroom.GetProperty("rowVersion").GetString());

        var classroomId = classroom.GetProperty("id").GetGuid();

        using var listed = await _server.SendAsync(
            HttpMethod.Post, "/api/v1/admin/execute/query/ListClassrooms", new { schoolId });
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var classrooms = await listed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(classroomId, Assert.Single(classrooms.EnumerateArray()).GetProperty("id").GetGuid());

        // Act / Assert: il corso segue lo stesso percorso della classe.
        var course = await SendCommandAsync("CreateCourse", new { title = "Informatica di base" });
        Assert.Equal("Informatica di base", course.GetProperty("title").GetString());
        Assert.Equal("1", course.GetProperty("rowVersion").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateClassroom_ConNomeEAnnoGiaUsati_RispondeConConflittoECodice()
    {
        // Arrange
        var school = await CreateSchoolAsync("Scuola Manzoni");
        var schoolId = school.GetProperty("id").GetGuid();
        await SendCommandAsync("CreateClassroom", new { schoolId, name = "2ª A", schoolYear = "2026/2027" });

        // Act
        using var duplicate = await _server.SendAsync(
            HttpMethod.Post,
            "/api/v1/admin/execute/command/CreateClassroom",
            new { schoolId, name = "  2ª  A ", schoolYear = "2026-2027" });

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var problem = await duplicate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(CodiErrorCodes.ClassroomAlreadyExists, problem.GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateClassroom_ConAnnoNonValido_RispondeConValidazioneFallita()
    {
        // Arrange
        var school = await CreateSchoolAsync("Scuola Manzoni");
        var schoolId = school.GetProperty("id").GetGuid();

        // Act
        using var response = await _server.SendAsync(
            HttpMethod.Post,
            "/api/v1/admin/execute/command/CreateClassroom",
            new { schoolId, name = "2ª A", schoolYear = "2026/2028" });

        // Assert: la forma dell'anno è una regola del caso d'uso, non un errore interno.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(CodiErrorCodes.ValidationFailed, problem.GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateClassroom_InUnaScuolaInesistente_RispondeRisorsaNonTrovata()
    {
        // Act
        using var response = await _server.SendAsync(
            HttpMethod.Post,
            "/api/v1/admin/execute/command/CreateClassroom",
            new { schoolId = Guid.NewGuid(), name = "2ª A", schoolYear = "2026/2027" });

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(CodiErrorCodes.ResourceNotFound, problem.GetProperty("code").GetString());
    }

    private Task<JsonElement> CreateSchoolAsync(string name) =>
        SendCommandAsync("CreateSchool", new { name });

    private async Task<JsonElement> SendCommandAsync(string requestName, object body)
    {
        using var response = await _server.SendAsync(
            HttpMethod.Post, $"/api/v1/admin/execute/command/{requestName}", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
