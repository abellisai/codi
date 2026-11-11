using System.Text.Json;
using System.Text.Json.Nodes;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>Ripristino persistito prima dell'ascolto: nessun vecchio avvio resta distribuibile.</summary>
public sealed class SessionStartupRecovery(IUnitOfWorkContext factory, ISessionCoordinator coordinator,
    IParticipantConnectionRegistry connections, TimeProvider time)
{
    public async Task<int> RecoverAsync(CancellationToken cancellationToken = default)
    {
        string[] ids;
        await using (var read = await factory.CreateAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            ids = (await read.Repo<ILessonSessionRepository>().QueryAsync(row => row.Status != "Ended", cancellationToken)
                .ConfigureAwait(false)).Select(row => row.Id).ToArray();
        foreach (var id in ids)
        {
            await using var coordination = await coordinator.AcquireAsync(Guid.Parse(id), cancellationToken).ConfigureAwait(false);
            await using var work = await factory.CreateAsync(useTransaction: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            var sessions = work.Repo<ILessonSessionRepository>();
            var session = await sessions.GetAsync(id, cancellationToken).ConfigureAwait(false)
                ?? throw CodiRequestException.ResourceNotFound();
            var now = time.GetUtcNow().ToUnixTimeMilliseconds();
            if (session.Status == "Ended") continue;
            if (session.Status == "Active") session.Status = "Paused";
            session.SessionRevision = checked(session.SessionRevision + 1);
            session.UpdatedAtUtcMs = now;
            var prior = ClassActivityState.Read(session.ClassDefaultJson);
            var suspended = ClassActivityState.Suspended(session.ClassDefaultJson) ?? prior;
            var document = (JsonObject)JsonSerializer.SerializeToNode(prior with
            { InteractionEnabled = false, TeacherPaused = session.Status == "Paused", RecoveryBlocked = true, ExecutionDirective = null }, CodiJson.Payload)!;
            document["resumeContext"] = JsonSerializer.SerializeToNode(suspended, CodiJson.Payload);
            session.ClassDefaultJson = document.ToJsonString(CodiJson.Payload);
            if (!await sessions.UpdateAsync(session, cancellationToken).ConfigureAwait(false)) throw CodiRequestException.RowVersionMismatch();
            var states = await work.Repo<IParticipantStateRepository>().QueryAsync(row => row.SessionId == id, cancellationToken).ConfigureAwait(false);
            foreach (var state in states)
            {
                state.ResumeContextJson ??= JsonSerializer.Serialize(DesiredStateProjection.Create(state), CodiJson.Payload);
                state.RecoveryBlockedFlag = 1;
                state.InteractionEnabledFlag = 0;
                state.DirectiveJson = null;
                state.EffectiveRevision = session.SessionRevision;
                state.UpdatedAtUtcMs = now;
                await SessionCommandPreamble.UpdateStateAsync(work, state, cancellationToken).ConfigureAwait(false);
                var participants = work.Repo<ISessionParticipantRepository>();
                var participant = await participants.GetAsync(state.Id, cancellationToken).ConfigureAwait(false)
                    ?? throw CodiRequestException.ResourceNotFound();
                participant.ConnectionEpoch = checked(participant.ConnectionEpoch + 1);
                participant.UpdatedAtUtcMs = now;
                if (!await participants.UpdateAsync(participant, cancellationToken).ConfigureAwait(false)) throw CodiRequestException.RowVersionMismatch();
            }
            var commands = await work.Repo<ISessionCommandRepository>().QueryAsync(row => row.SessionId == id, cancellationToken).ConfigureAwait(false);
            var deliveries = work.Repo<ICommandDeliveryRepository>();
            foreach (var command in commands)
                foreach (var delivery in await deliveries.QueryByCommandAsync(Guid.Parse(command.Id), cancellationToken).ConfigureAwait(false))
                    if (delivery.Status is "Pending" or "Sent" or "Unconfirmed")
                        await deliveries.MarkSupersededAsync(Guid.Parse(command.Id), Guid.Parse(delivery.ParticipantId), delivery.Status, cancellationToken).ConfigureAwait(false);
            await work.Repo<IOutboxMessageRepository>().SupersedeSessionAsync(id, now, cancellationToken).ConfigureAwait(false);
            await work.CommitAsync(cancellationToken).ConfigureAwait(false);
            foreach (var state in states)
                if (connections.Get(Guid.Parse(state.Id)) is { } connection)
                    connections.Release(connection.ParticipantId, connection.ConnectionId, connection.Epoch);
        }
        return ids.Length;
    }
}
