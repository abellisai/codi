using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Api.Authentication;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Exercises;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Sessions;

public sealed class AttemptHttpTests : IAsyncLifetime
{
    private CodiHttpServer server = null!;
    private Guid sessionId, attemptId, participantId;
    private long epoch;
    public async Task InitializeAsync()
    {
        server = await CodiHttpServer.CreateAsync();
        using var login = await server.LoginAsync();
        var school = (await Command("CreateSchool", new { name = "Test" })).GetProperty("id").GetGuid();
        var classroom = (await Command("CreateClassroom", new { schoolId = school, name = "1A", schoolYear = "2026/2027" })).GetProperty("id").GetGuid();
        var course = (await Command("CreateCourse", new { title = "Corso" })).GetProperty("id").GetGuid();
        var lesson = (await Command("CreateLesson", new { courseId = course, title = "Lezione" })).GetProperty("id").GetGuid();
        sessionId = (await Command("CreateSession", new { lessonId = lesson, classroomId = classroom })).GetProperty("id").GetGuid();
        await Command("StartSession", new { sessionId, expectedSessionRevision = 1 });
        using var bootstrap = await server.SendAsync(HttpMethod.Get, "/api/v1/public/bootstrap");
        using var join = await server.SendWithHeaderAsync(HttpMethod.Post, "/api/v1/public/join",
            new { schoolId = school, classroomId = classroom, sessionId, displayName = "Luca" }, "Idempotency-Key", "join");
        Assert.Equal(HttpStatusCode.OK, join.StatusCode);
        participantId = (await join.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("participantId").GetGuid();
        var exercise = (await Command("CreateExercise", new { exerciseType = "GridPath", title = "Stella" })).GetProperty("id").GetGuid();
        var draft = await Command("SaveExerciseDraft", new { exerciseId = exercise, definitionJson = AttemptSaveTests.Definition, workspaceJson = "{}" });
        var version = (await Command("PublishExerciseVersion", new { exerciseId = exercise, baseRowVersion = draft.GetProperty("rowVersion").GetString() })).GetProperty("id").GetGuid();
        await Command("AssignExercise", new { sessionId, expectedSessionRevision = 2, exerciseVersionId = version });
        using var initialized = await server.SendAsync(HttpMethod.Post, "/api/v1/public/execute/command/InitializeParticipantConnection",
            new { protocolVersion = 1, clientInstanceId = Guid.NewGuid(), appVersion = "test", engineVersion = "regia-demo-1" });
        Assert.Equal(HttpStatusCode.OK, initialized.StatusCode);
        var snapshot = await initialized.Content.ReadFromJsonAsync<JsonElement>();
        epoch = long.Parse(snapshot.GetProperty("connectionEpoch").GetString()!);
        attemptId = snapshot.GetProperty("desiredState").GetProperty("attemptId").GetGuid();
        var registry = server.Services.GetRequiredService<IParticipantConnectionRegistry>();
        registry.Acquire(new(participantId, sessionId, Guid.NewGuid(), "http-test", epoch, 3), DateTimeOffset.UtcNow);
        registry.MarkSnapshotApplied(new(participantId, "http-test", epoch, 3), DateTimeOffset.UtcNow);
    }
    public async Task DisposeAsync() => await server.DisposeAsync();
    private async Task<JsonElement> Command(string name, object body)
    {
        using var response = await server.SendWithHeaderAsync(HttpMethod.Post, $"/api/v1/admin/execute/command/{name}", body, "Idempotency-Key", Guid.NewGuid().ToString("N"));
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Accepted }); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private object Body => new { saveId = Guid.NewGuid(), connectionEpoch = epoch.ToString(), baseRowVersion = "1", clientProjectRevision = "1", workspaceJson = "{ }", workspaceHash = ExerciseContentHash.ComputeWorkspace("{ }") };
    [Fact]
    public async Task Workspace_Ricevuta_EGetAttempt()
    {
        using var response = await server.SendAsync(HttpMethod.Put, $"/api/v1/public/participant/attempts/{attemptId:D}/workspace", Body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("1", receipt.GetProperty("projectRevision").GetString());
        using var read = await server.SendAsync(HttpMethod.Post, "/api/v1/public/execute/query/GetAttempt", new { });
        Assert.Equal("{ }", (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("workspaceJson").GetString());
    }
    [Theory]
    [InlineData("workspace")]
    [InlineData("checkpoint")]
    [InlineData("finalization")]
    public async Task Mutation_SenzaAntiforgery_Rifiuta(string kind)
    {
        using var response = await server.SendAsync(kind == "finalization" ? HttpMethod.Post : HttpMethod.Put,
            $"/api/v1/public/participant/attempts/{attemptId:D}/{kind}", Body, useCookieToken: false);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Theory]
    [InlineData("wrongId")]
    [InlineData("number")]
    [InlineData("array")]
    [InlineData("broken")]
    public async Task CorpoNonCoerente_Rifiuta(string defect)
    {
        var text = defect switch { "wrongId" => $"{{\"attemptId\":\"{Guid.NewGuid():D}\"}}", "number" => "{\"attemptId\":1}", "array" => "[]", _ => "{" };
        using var response = await server.SendContentAsync(HttpMethod.Put, $"/api/v1/public/participant/attempts/{attemptId:D}/workspace", new StringContent(text, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact]
    public async Task CorpoOltreLimite_RifiutaCon413()
    {
        using var response = await server.SendContentAsync(HttpMethod.Put, $"/api/v1/public/participant/attempts/{attemptId:D}/workspace", new StringContent(new string('a', 2 * 1024 * 1024 + 1), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }
    [Fact]
    public async Task FineSessione_Grant_Idempotenza_ESalvataggiOrdinariChiusi()
    {
        await Command("EndSession", new { sessionId, expectedSessionRevision = 3 });
        using var grants = await server.SendAsync(HttpMethod.Get, "/api/v1/public/participant/finalizations");
        Assert.Equal(HttpStatusCode.OK, grants.StatusCode);
        var grant = (await grants.Content.ReadFromJsonAsync<JsonElement>())[0];
        using var rejected = await server.SendAsync(HttpMethod.Put, $"/api/v1/public/participant/attempts/{attemptId:D}/workspace", Body);
        Assert.Contains(rejected.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.Conflict });
        var body = new
        {
            grantId = grant.GetProperty("grantId").GetGuid(),
            saveId = Guid.NewGuid(),
            connectionEpoch = epoch.ToString(),
            baseRowVersion = "1",
            clientProjectRevision = "1",
            workspaceJson = "{ }",
            workspaceHash = ExerciseContentHash.ComputeWorkspace("{ }")
        };
        using var final = await server.SendAsync(HttpMethod.Post, $"/api/v1/public/participant/attempts/{attemptId:D}/finalization", body);
        Assert.Equal(HttpStatusCode.OK, final.StatusCode);
        using var retry = await server.SendAsync(HttpMethod.Post, $"/api/v1/public/participant/attempts/{attemptId:D}/finalization", body);
        Assert.Equal(await final.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
    }
}
