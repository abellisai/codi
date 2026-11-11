using Qwerty.Codi.Application.Dto;
using System.Text;
using System.Text.Json;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Sessions;

public sealed record ActivityStateArguments(int ProtocolVersion, Guid ServerInstanceId,
    long ConnectionEpoch, long EffectiveRevision, Guid? AttemptId, long ExecutionGeneration,
    long Sequence, string RuntimeStatus, string? Reason, Guid? RunId, string? WorkspaceHash, JsonElement? Scene);

/// <summary>Verifica identità, connessione e attività prima di aggiornare la cache in memoria.</summary>
public sealed class ParticipantObservationService(IUnitOfWorkContext factory,
    ISessionCoordinator coordinator, IParticipantConnectionRegistry connections,
    IParticipantObservationCache cache, IServerInstance server, TimeProvider time)
{
    private static readonly HashSet<string> RuntimeStates = ["Idle", "Ready", "Running", "Paused", "Completed", "Stopped", "Error"];

    public async Task<ParticipantObservationDto?> UpdateAsync(CodiRequestContext context,
        string connectionId, ActivityStateArguments update, CancellationToken cancellationToken)
    {
        if (context.ParticipantId is not { } participantId || !context.Allows(CodiPolicy.ActiveParticipantConnection))
            throw CodiRequestException.AuthenticationRequired("ActivityStateUpdated");
        var connection = connections.Get(participantId);
        if (connection is null || connection.ConnectionId != connectionId || !connection.SnapshotApplied ||
            connection.Epoch != update.ConnectionEpoch || connection.Presence == ParticipantPresence.NotOperational ||
            update.ProtocolVersion != ParticipantProtocol.CurrentVersion || update.ServerInstanceId != server.InstanceId)
            throw new CodiRequestException(CodiErrorCodes.SessionStateConflict, "La connessione non è operativa o è superata.");
        if (!RuntimeStates.Contains(update.RuntimeStatus) || update.Sequence < 0 || update.Reason?.Length > 128 ||
            update.WorkspaceHash?.Length > 128 || (update.Scene is { } scene && Encoding.UTF8.GetByteCount(scene.GetRawText()) > 8192))
            throw CodiRequestException.InvalidPayload("ActivityStateUpdated");

        await using var coordination = await coordinator.AcquireAsync(connection.SessionId, cancellationToken).ConfigureAwait(false);
        await using var work = await factory.CreateAsync(useTransaction: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        var participant = await work.Repo<ISessionParticipantRepository>().GetAsync(participantId.ToString("D"), cancellationToken).ConfigureAwait(false);
        var session = await work.Repo<ILessonSessionRepository>().GetAsync(connection.SessionId.ToString("D"), cancellationToken).ConfigureAwait(false);
        var state = await work.Repo<IParticipantStateRepository>().GetAsync(participantId.ToString("D"), cancellationToken).ConfigureAwait(false);
        if (participant?.AdmissionStatusText != nameof(AdmissionStatus.Admitted) ||
            participant.ConnectionEpoch != update.ConnectionEpoch || session?.Status == nameof(SessionStatus.Ended) || state is null ||
            state.AttemptId != update.AttemptId?.ToString("D") || state.EffectiveRevision != update.EffectiveRevision ||
            state.ExecutionGeneration != update.ExecutionGeneration || connections.Get(participantId)?.ConnectionId != connectionId)
            throw new CodiRequestException(CodiErrorCodes.SessionStateConflict, "La telemetria riferisce uno stato superato.");

        var observation = new ParticipantObservationDto(connection.SessionId, participantId, update.ConnectionEpoch,
            update.EffectiveRevision, update.AttemptId, update.ExecutionGeneration, update.Sequence,
            update.RuntimeStatus, update.Reason, update.RunId, update.WorkspaceHash, update.Scene?.Clone(), time.GetUtcNow());
        return cache.Update(observation) ? observation : null;
    }
}
