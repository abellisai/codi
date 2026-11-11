using Qwerty.Codi.Application.Dto;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Qwerty.Codi.Api.Dispatch;
using Qwerty.Codi.Api.Hubs;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Sessions;

namespace Qwerty.Codi.Api.BackgroundJobs;

/// <summary>Invia le variazioni agli osservatori dopo una nuova verifica della credenziale.</summary>
internal sealed class TeacherObservationService(TeacherObservationRegistry observations,
    IServiceScopeFactory scopes, IHubContext<TeacherHub> hub, TimeProvider time,
    ILogger<TeacherObservationService> logger) : BackgroundService
{
    private readonly Dictionary<string, string> _last = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            var current = observations.All();
            foreach (var key in _last.Keys.Except(current.Select(row => row.ConnectionId)).ToArray()) _last.Remove(key);
            foreach (var observer in current)
            {
                try { await PublishAsync(observer, stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (CodiRequestException)
                {
                    observations.Remove(observer.ConnectionId);
                    _last.Remove(observer.ConnectionId);
                    await hub.Clients.Client(observer.ConnectionId).SendAsync("ObservationRevoked", stoppingToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Aggiornamento docente non riuscito per la sessione {SessionId}", observer.SessionId);
                }
            }
        }
    }

    internal async Task PublishAsync(TeacherObservation observer, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var http = new DefaultHttpContext { User = observer.User };
        var context = await scope.ServiceProvider.GetRequiredService<ICodiRequestContextProvider>()
            .ResolveRequestContextAsync(http, cancellationToken).ConfigureAwait(false);
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(new { sessionId = observer.SessionId }, CodiJson.Payload));
        var snapshot = (TeacherSnapshotDto)(await scope.ServiceProvider.GetRequiredService<CodiRequestExecutor>()
            .ExecuteAsync(CodiRequestKind.Query, GetSessionOverviewQuery.RequestName, CodiRequestChannel.Hub,
                body.RootElement, context, cancellationToken).ConfigureAwait(false))!;
        var json = JsonSerializer.Serialize(snapshot, CodiJson.Payload);
        if (_last.GetValueOrDefault(observer.ConnectionId) == json) return;
        var client = hub.Clients.Client(observer.ConnectionId);
        var previous = _last.TryGetValue(observer.ConnectionId, out var priorJson) ? JsonSerializer.Deserialize<TeacherSnapshotDto>(priorJson, CodiJson.Payload) : null;
        var known = previous?.Participants.Select(row => row.ParticipantId).ToHashSet() ?? [];
        await client.SendAsync(TeacherRealtimeEvents.TeacherSnapshot, snapshot, cancellationToken).ConfigureAwait(false);
        foreach (var participant in snapshot.Participants.Where(row => !known.Contains(row.ParticipantId)))
            await client.SendAsync(TeacherRealtimeEvents.ParticipantJoined, participant, cancellationToken).ConfigureAwait(false);
        await client.SendAsync(TeacherRealtimeEvents.ParticipantPresenceChanged, snapshot.Participants, cancellationToken).ConfigureAwait(false);
        await client.SendAsync(TeacherRealtimeEvents.ParticipantStateUpdated, snapshot.Participants, cancellationToken).ConfigureAwait(false);
        await client.SendAsync(TeacherRealtimeEvents.CommandStatusChanged, snapshot.Commands, cancellationToken).ConfigureAwait(false);
        _last[observer.ConnectionId] = json;
    }
}
