using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;

namespace Qwerty.Codi.Application.Sessions;

public sealed class GetAttemptFinalizationsHandler(IUnitOfWorkContext factory, TimeProvider time)
    : IRequestHandler<GetAttemptFinalizationsQuery, IReadOnlyList<AttemptFinalizationGrantDto>>
{
    public async ValueTask<IReadOnlyList<AttemptFinalizationGrantDto>> Handle(GetAttemptFinalizationsQuery request, CancellationToken cancellationToken)
    {
        var credentialId = request.Context.CredentialSessionId ?? throw CodiRequestException.AuthenticationRequired(GetAttemptFinalizationsQuery.RequestName);
        await using var work = await factory.CreateAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return await AttemptFinalization.ReadAsync(work, credentialId.ToString("D"), time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
    }
}

public sealed class ListParticipantAttemptsHandler(IUnitOfWorkContext factory)
    : IRequestHandler<ListParticipantAttemptsQuery, IReadOnlyList<AttemptDto>>
{
    public async ValueTask<IReadOnlyList<AttemptDto>> Handle(ListParticipantAttemptsQuery request, CancellationToken cancellationToken)
    {
        var adminId = request.Context.AdminUserId ?? throw CodiRequestException.AuthenticationRequired(ListParticipantAttemptsQuery.RequestName);
        await using var work = await factory.CreateAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await SessionGridGuard.LoadOwnedSessionAsync(work, request.SessionId, adminId, cancellationToken).ConfigureAwait(false);
        var participantId = request.ParticipantId.ToString("D");
        var sessionId = request.SessionId.ToString("D");
        var participant = await work.Repo<ISessionParticipantRepository>().GetAsync(participantId, cancellationToken).ConfigureAwait(false);
        if (participant?.SessionId != sessionId) throw CodiRequestException.ResourceNotFound();
        var attempts = await work.Repo<IExerciseAttemptRepository>().QueryAsync(
            row => row.ParticipantId == participantId && row.SessionId == sessionId, cancellationToken).ConfigureAwait(false);
        return attempts.OrderByDescending(row => row.CreatedAtUtcMs).ThenByDescending(row => row.AttemptNumber).Select(row =>
            new AttemptDto(Guid.Parse(row.Id), Guid.Parse(row.AssignmentId), Guid.Parse(row.ExerciseVersionId),
                row.AttemptNumber, row.Status, row.WorkspaceJson, row.WorkspaceHash, row.ProjectRevision,
                row.CheckpointJson, row.CheckpointEngineVersion,
                row.LastSavedAtUtcMs is { } saved ? DateTimeOffset.FromUnixTimeMilliseconds(saved) : null,
                DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtcMs), row.RowVersion)).ToArray();
    }
}
