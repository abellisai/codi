using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Apertura delle letture di regia: la sessione della credenziale verificata, con la proprietÃ 
/// controllata come nei comandi (Â§6.4). Una lettura **non** dichiara la revisione attesa, perchÃ© non
/// muta nulla: chiedere una revisione per guardare la classe sarebbe un vincolo senza scopo, e la
/// griglia deve poter leggere lo stato anche mentre il docente sta comandando.
///
/// Un altro amministratore riceve una risorsa assente, non un rifiuto che riveli l'esistenza della
/// sessione; le sessioni terminate restano leggibili, perchÃ© la loro fotografia serve ancora.
/// </summary>
internal static class SessionGridGuard
{
    public static async Task<LessonSessionEntity> LoadOwnedSessionAsync(
        IUnitOfWork work,
        Guid sessionId,
        Guid controllerAdminId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        var stored = await work.Repo<ILessonSessionRepository>()
            .GetAsync(sessionId.ToString("D"), cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        if (!string.Equals(stored.ControllerAdminId, controllerAdminId.ToString("D"), StringComparison.Ordinal))
        {
            throw CodiRequestException.ResourceNotFound();
        }

        return stored;
    }
}

/// <summary>
/// Griglia dei partecipanti (Â§10.5). Le righe si leggono per sessione, non una per partecipante:
/// partecipazioni, stati e tentativi si raccolgono con tre letture e si compongono in memoria, e
/// dodici alunni non diventano trentasei query mentre il docente guarda la classe.
/// </summary>
public sealed class ListSessionParticipantsHandler(
    IUnitOfWorkContext unitOfWorkContext,
    IParticipantConnectionRegistry connectionRegistry,
    IParticipantObservationCache observations,
    IParticipantMessageStore messages) : IRequestHandler<ListSessionParticipantsQuery, IReadOnlyList<SessionParticipantOverviewDto>>
{
    public async ValueTask<IReadOnlyList<SessionParticipantOverviewDto>> Handle(
        ListSessionParticipantsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } controllerAdminId)
        {
            throw CodiRequestException.AuthenticationRequired(ListSessionParticipantsQuery.RequestName);
        }

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await SessionGridGuard
            .LoadOwnedSessionAsync(work, request.SessionId, controllerAdminId, cancellationToken)
            .ConfigureAwait(false);

        var rows = await SessionGridReader
            .ReadSessionAsync(work, request.SessionId, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Participants
                .OrderBy(row => row.DisplayName, StringComparer.Ordinal)
                .ThenBy(row => row.Id, StringComparer.Ordinal)
                .Select(row => SessionGridReader.ToOverview(row, rows, connectionRegistry, observations, messages)),
        ];
    }
}

/// <summary>
/// Dettaglio di un alunno (Â§10.5): la stessa riga della griglia piÃ¹ il materiale e il lavoro. Un
/// partecipante di un'altra sessione Ã¨ una risorsa assente, non un dettaglio da mostrare.
/// </summary>
public sealed class GetParticipantDetailHandler(
    IUnitOfWorkContext unitOfWorkContext,
    IParticipantConnectionRegistry connectionRegistry,
    IParticipantObservationCache observations,
    IParticipantMessageStore messages) : IRequestHandler<GetParticipantDetailQuery, SessionParticipantDetailDto>
{
    public async ValueTask<SessionParticipantDetailDto> Handle(
        GetParticipantDetailQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } controllerAdminId)
        {
            throw CodiRequestException.AuthenticationRequired(GetParticipantDetailQuery.RequestName);
        }

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await SessionGridGuard
            .LoadOwnedSessionAsync(work, request.SessionId, controllerAdminId, cancellationToken)
            .ConfigureAwait(false);

        var rows = await SessionGridReader
            .ReadSessionAsync(work, request.SessionId, cancellationToken)
            .ConfigureAwait(false);

        var key = request.ParticipantId.ToString("D");
        var participant = rows.Participants.FirstOrDefault(row => string.Equals(row.Id, key, StringComparison.Ordinal))
            ?? throw CodiRequestException.ResourceNotFound();

        var overview = SessionGridReader.ToOverview(participant, rows, connectionRegistry, observations, messages);
        var state = rows.States.GetValueOrDefault(key);

        if (state?.AssignmentId is null)
        {
            return new SessionParticipantDetailDto(overview, Assignment: null, Attempt: null, AttemptCount: 0);
        }

        var assignment = await work.Repo<IActivityAssignmentRepository>()
            .GetAsync(state.AssignmentId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var version = await work.Repo<IExerciseVersionRepository>()
            .GetAsync(assignment.ExerciseVersionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var exercise = await work.Repo<IExerciseRepository>()
            .GetAsync(version.ExerciseId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var history = await work.Repo<IExerciseAttemptRepository>()
            .QueryByAssignmentAndParticipantAsync(Guid.Parse(assignment.Id), request.ParticipantId, cancellationToken)
            .ConfigureAwait(false);

        return new SessionParticipantDetailDto(
            overview,
            new ParticipantAssignmentDto(
                Guid.Parse(assignment.Id),
                assignment.Scope,
                Guid.TryParse(assignment.LessonExerciseId, out var lessonExerciseId) ? lessonExerciseId : null,
                Guid.Parse(assignment.ExerciseVersionId),
                Guid.Parse(version.ExerciseId),
                exercise.Title,
                version.EngineVersion),
            SessionGridReader.ToAttempt(rows, state.AttemptId),
            history.Count);
    }
}

/// <summary>
/// Le tre letture di una sessione, prese insieme: le usano la griglia e il dettaglio, che devono
/// descrivere lo stesso istante. Il dettaglio non rilegge la riga del partecipante una seconda volta,
/// perchÃ© due letture separate possono cadere ai due lati di un comando.
/// </summary>
internal sealed class SessionGridRows(
    IReadOnlyList<SessionParticipantEntity> participants,
    IReadOnlyDictionary<string, ParticipantStateEntity> states,
    IReadOnlyDictionary<string, ExerciseAttemptEntity> attempts)
{
    public IReadOnlyList<SessionParticipantEntity> Participants { get; } = participants;

    public IReadOnlyDictionary<string, ParticipantStateEntity> States { get; } = states;

    public IReadOnlyDictionary<string, ExerciseAttemptEntity> Attempts { get; } = attempts;
}

internal static class SessionGridReader
{
    public static async Task<SessionGridRows> ReadSessionAsync(
        IUnitOfWork work,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var key = sessionId.ToString("D");

        var participants = await work.Repo<ISessionParticipantRepository>()
            .QueryAsync(row => row.SessionId == key, cancellationToken)
            .ConfigureAwait(false);

        var states = await work.Repo<IParticipantStateRepository>()
            .QueryAsync(row => row.SessionId == key, cancellationToken)
            .ConfigureAwait(false);

        var attempts = await work.Repo<IExerciseAttemptRepository>()
            .QueryAsync(row => row.SessionId == key, cancellationToken)
            .ConfigureAwait(false);

        return new SessionGridRows(
            participants,
            states.ToDictionary(row => row.Id, StringComparer.Ordinal),
            attempts.ToDictionary(row => row.Id, StringComparer.Ordinal));
    }

    /// <summary>
    /// Riga della griglia: presenza dal registro in memoria (Â§11.6) e stato desiderato dalla riga
    /// persistita. Una partecipazione senza stato â€” non dovrebbe capitare, l'ingresso lo crea nella
    /// stessa transazione â€” mostra solo l'identitÃ  invece di uno stato inventato.
    /// </summary>
    public static SessionParticipantOverviewDto ToOverview(
        SessionParticipantEntity participant,
        SessionGridRows rows,
        IParticipantConnectionRegistry connectionRegistry,
        IParticipantObservationCache? observations = null,
        IParticipantMessageStore? messages = null)
    {
        var connection = Guid.TryParse(participant.Id, out var participantId)
            ? connectionRegistry.Get(participantId)
            : null;

        var state = rows.States.GetValueOrDefault(participant.Id);
        var attempt = state?.AttemptId is null ? null : rows.Attempts.GetValueOrDefault(state.AttemptId);

        return new SessionParticipantOverviewDto(
            Guid.Parse(participant.Id),
            participant.DisplayName,
            Guid.TryParse(participant.StationId, out var stationId) ? stationId : null,
            participant.AdmissionStatusText,
            participant.ConnectionEpoch,
            DateTimeOffset.FromUnixTimeMilliseconds(participant.JoinedAtUtcMs),
            participant.ExcludedAtUtcMs is { } excluded ? DateTimeOffset.FromUnixTimeMilliseconds(excluded) : null,
            connection?.Presence ?? ParticipantPresence.Disconnected,
            connection?.LastHeartbeatUtc,
            state?.View,
            state is not null && state.InteractionEnabledFlag != 0,
            state is not null && state.TeacherPausedFlag != 0,
            state is not null && state.RecoveryBlockedFlag != 0,
            state?.ExecutionGeneration,
            // La direttiva si dichiara presente, non si interpreta qui: Ã¨ un documento dello stato
            // desiderato del client, e la griglia non deve dedurne lo stato di esecuzione.
            !string.IsNullOrWhiteSpace(state?.DirectiveJson),
            attempt is null ? null : Guid.Parse(attempt.Id),
            attempt?.AttemptNumber,
            attempt?.RowVersion,
            attempt?.Status,
            attempt?.ProjectRevision,
            attempt?.LastSavedAtUtcMs is { } saved ? DateTimeOffset.FromUnixTimeMilliseconds(saved) : null,
            messages?.UnreadByTeacher(participantId) ?? 0)
        {
            RowVersion = participant.RowVersion,
            ObservedState = CurrentObservation(observations?.Get(participantId), state, participant.ConnectionEpoch),
        };
    }

    private static ParticipantObservationDto? CurrentObservation(ParticipantObservationDto? observed,
        ParticipantStateEntity? state, long epoch) =>
        observed is not null && state is not null && observed.ConnectionEpoch == epoch &&
        observed.AttemptId?.ToString("D") == state.AttemptId && observed.EffectiveRevision == state.EffectiveRevision &&
        observed.ExecutionGeneration == state.ExecutionGeneration ? observed : null;

    public static ParticipantAttemptDto? ToAttempt(SessionGridRows rows, string? attemptId) =>
        attemptId is null || !rows.Attempts.TryGetValue(attemptId, out var attempt)
            ? null
            : new ParticipantAttemptDto(
                Guid.Parse(attempt.Id),
                attempt.AttemptNumber,
                attempt.Status,
                attempt.WorkspaceJson,
                attempt.WorkspaceHash,
                attempt.ProjectRevision,
                !string.IsNullOrWhiteSpace(attempt.CheckpointJson),
                attempt.CheckpointEngineVersion,
                attempt.LastSavedAtUtcMs is { } saved ? DateTimeOffset.FromUnixTimeMilliseconds(saved) : null,
                DateTimeOffset.FromUnixTimeMilliseconds(attempt.UpdatedAtUtcMs),
                attempt.RowVersion);
}

public sealed class GetSessionOverviewHandler(IUnitOfWorkContext factory,
    IParticipantConnectionRegistry connections, IParticipantObservationCache observations, IServerInstance server,
    IParticipantMessageStore messages)
    : IRequestHandler<GetSessionOverviewQuery, TeacherSnapshotDto>
{
    public async ValueTask<TeacherSnapshotDto> Handle(GetSessionOverviewQuery request, CancellationToken cancellationToken)
    {
        var adminId = request.Context.AdminUserId ?? throw CodiRequestException.AuthenticationRequired(GetSessionOverviewQuery.RequestName);
        await using var work = await factory.CreateAsync(useTransaction: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        var session = await SessionGridGuard.LoadOwnedSessionAsync(work, request.SessionId, adminId, cancellationToken).ConfigureAwait(false);
        var rows = await SessionGridReader.ReadSessionAsync(work, request.SessionId, cancellationToken).ConfigureAwait(false);
        var commands = await work.Repo<ISessionCommandRepository>().QueryAsync(row => row.SessionId == request.SessionId.ToString("D"), cancellationToken).ConfigureAwait(false);
        var statuses = new List<CommandStatusDto>();
        foreach (var command in commands.OrderByDescending(row => row.Revision).Take(8))
        {
            var deliveries = await work.Repo<ICommandDeliveryRepository>().QueryByCommandAsync(Guid.Parse(command.Id), cancellationToken).ConfigureAwait(false);
            statuses.Add(new CommandStatusDto(Guid.Parse(command.Id), request.SessionId, command.CommandName, command.Revision,
                GetCommandStatusHandler.Summarize(deliveries), deliveries.Select(GetCommandStatusHandler.ToDto).ToArray()));
        }
        return new TeacherSnapshotDto(ParticipantProtocol.CurrentVersion, server.InstanceId,
            SessionDto.FromDomain(session.ToDomain()), rows.Participants.OrderBy(row => row.DisplayName, StringComparer.Ordinal)
                .Select(row => SessionGridReader.ToOverview(row, rows, connections, observations, messages)).ToArray())
        { Commands = statuses };
    }
}
