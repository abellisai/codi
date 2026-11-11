using Qwerty.Codi.Application.Dto;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Qwerty.Codi.Api.BackgroundJobs;
using Qwerty.Codi.Api.Dispatch;
using Qwerty.Codi.Api.Hubs;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.Application.Organization;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Realtime;

[Trait("Category", "Integration")]
public sealed class TeacherObservationTests
{
    [Fact]
    public void Osservatori_LimiteSostituzioneERimozione_ConservaSoloConnessioniAttive()
    {
        var registry = new TeacherObservationRegistry();
        for (var index = 0; index < 64; index++) registry.Observe(new(index.ToString(), Guid.NewGuid(), new()));
        Assert.Throws<HubException>(() => registry.Observe(new("overflow", Guid.NewGuid(), new())));
        var session = Guid.NewGuid(); registry.Observe(new("0", session, new()));
        Assert.Equal(session, registry.All().Single(row => row.ConnectionId == "0").SessionId);
        registry.Remove("0"); Assert.Equal(63, registry.All().Count);
    }

    [Fact]
    public async Task Osservazione_SnapshotVariazioniERevoca_VerificaLaCredenzialeOgniVolta()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        var registry = new TeacherObservationRegistry();
        var context = new MutableContext(scenario.RegiaContext());
        var clients = new FakeHubClients();
        var services = new ServiceCollection();
        services.AddSingleton(scenario.Host.Executor);
        services.AddSingleton<ICodiRequestContextProvider>(context);
        await using var provider = services.BuildServiceProvider();
        using var service = new TeacherObservationService(registry, provider.GetRequiredService<IServiceScopeFactory>(),
            new ObservationHubContext(clients), TimeProvider.System, NullLogger<TeacherObservationService>.Instance);
        var observer = new TeacherObservation("teacher", seed.SessionId, new ClaimsPrincipal());
        registry.Observe(observer);
        await service.PublishAsync(observer, CancellationToken.None);
        Assert.Contains(clients.Sent, row => row.Method == "TeacherSnapshot");
        var before = clients.Sent.Count;
        await service.PublishAsync(observer, CancellationToken.None);
        Assert.Equal(before, clients.Sent.Count);
        await scenario.JoinAsync(seed, "Luca");
        await service.PublishAsync(observer, CancellationToken.None);
        Assert.True(clients.Sent.Count > before);
        var snapshot = (TeacherSnapshotDto)clients.Sent.Last(row => row.Method == "TeacherSnapshot").Arguments[0]!;
        Assert.Equal("Luca", Assert.Single(snapshot.Participants).DisplayName);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(snapshot, CodiJson.Payload));
        Assert.Equal("Disconnected", json.RootElement.GetProperty("participants")[0].GetProperty("presence").GetString());
        context.Current = CodiRequestContext.Anonymous;
        await service.StartAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (registry.All().Count != 0) await Task.Delay(50, timeout.Token);
        await service.StopAsync(CancellationToken.None);
        Assert.Contains(clients.Sent, row => row.Method == "ObservationRevoked");
    }

    [Fact]
    public async Task HubDocente_CambioSessioneRiconnessioneERimozione_InviaLoSnapshot()
    {
        await using var scenario = await OutboxScenario.CreateAsync();
        var first = await scenario.StartedSessionAsync();
        var classroom = await scenario.Host.CommandAsAsync<ClassroomDto>("CreateClassroom", new { schoolId = first.SchoolId, name = "Terza B", schoolYear = "2026/2027" }, scenario.RegiaContext());
        var lessonId = Guid.Parse(await scenario.ScalarAsync<string>("SELECT lesson_id FROM lesson_session LIMIT 1;"));
        var second = await scenario.Host.CommandAsAsync<SessionDto>("CreateSession", new { lessonId, classroomId = classroom.Id }, scenario.RegiaContext());
        var registry = new TeacherObservationRegistry(); var clients = new FakeHubClients(); var groups = new FakeGroupManager();
        var hub = new TeacherHub(scenario.Host.Executor, new StubContextProvider(scenario.RegiaContext()), registry)
        { Context = new FakeHubCallerContext(new DefaultHttpContext(), "teacher"), Groups = groups, Clients = clients };
        await hub.ObserveSession(first.SessionId);
        await hub.ObserveSession(second.Id);
        Assert.Equal(second.Id, Assert.Single(registry.All()).SessionId);
        Assert.Contains(("teacher", CodiHubGroups.Teachers(first.SessionId)), groups.Removed);
        Assert.Equal(2, clients.Sent.Count(row => row.Method == "TeacherSnapshot"));
        await hub.OnDisconnectedAsync(null); Assert.Empty(registry.All());
    }

    private sealed class MutableContext(CodiRequestContext current) : ICodiRequestContextProvider
    {
        public CodiRequestContext Current { get; set; } = current;
        public Task<CodiRequestContext> ResolveRequestContextAsync(HttpContext httpContext, CancellationToken cancellationToken) => Task.FromResult(Current);
    }
    private sealed class ObservationHubContext(FakeHubClients clients) : IHubContext<TeacherHub>
    {
        public IHubClients Clients => clients;
        public IGroupManager Groups { get; } = new FakeGroupManager();
    }
}
