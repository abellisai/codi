using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Exercises;
using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Pubblica l'anteprima dei blocchi (§15.5). Non scrive nulla nel database: verifica che l'anteprima
/// appartenga al lavoro **corrente** — partecipazione ammessa, connessione operativa, tentativo e
/// generazione correnti, sessione non terminata — poi la valida contro la definizione dell'esercizio
/// e la mette in cache. Un'anteprima che non corrisponde al lavoro corrente viene rifiutata, non
/// conservata: mostrarla al docente descriverebbe un altro lavoro.
///
/// La validazione usa le stesse regole del salvataggio (§13.3): un progetto formalmente JSON ma con
/// blocchi sconosciuti o fuori dai limiti non entra nella cache.
/// </summary>
public sealed class PublishWorkspacePreviewHandler(
    IUnitOfWorkContext unitOfWorkContext,
    IParticipantConnectionRegistry connections,
    IParticipantPreviewCache previews,
    TimeProvider time) : IRequestHandler<PublishWorkspacePreviewCommand, WorkspacePreviewDto>
{
    public async ValueTask<WorkspacePreviewDto> Handle(
        PublishWorkspacePreviewCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = PublishWorkspacePreviewCommand.RequestName;
        var participantId = request.Context.ParticipantId
            ?? throw CodiRequestException.AuthenticationRequired(name);

        var connection = connections.Get(participantId)
            ?? throw CodiRequestException.ResourceNotFound();

        if (connection.Epoch != request.ConnectionEpoch || !connection.SnapshotApplied ||
            connection.Presence == ParticipantPresence.NotOperational)
        {
            throw new CodiRequestException(CodiErrorCodes.SessionStateConflict,
                "La connessione dell'anteprima non è operativa o è superata.");
        }

        if (string.IsNullOrWhiteSpace(request.WorkspaceHash) || request.WorkspaceHash.Length > 128)
        {
            throw CodiRequestException.InvalidPayload(name);
        }

        var participantKey = participantId.ToString("D");
        var attemptKey = request.AttemptId.ToString("D");

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var participant = await work.Repo<ISessionParticipantRepository>()
            .GetAsync(participantKey, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var session = await work.Repo<ILessonSessionRepository>()
            .GetAsync(connection.SessionId.ToString("D"), cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var state = await work.Repo<IParticipantStateRepository>()
            .GetAsync(participantKey, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        if (participant.AdmissionStatusText != "Admitted" || participant.ConnectionEpoch != request.ConnectionEpoch ||
            session.Status == "Ended" || state.AttemptId != attemptKey ||
            state.ExecutionGeneration != request.ExecutionGeneration)
        {
            throw new CodiRequestException(CodiErrorCodes.SessionStateConflict,
                "L'anteprima riferisce uno stato superato.");
        }

        var attempt = await work.Repo<IExerciseAttemptRepository>()
            .GetAsync(attemptKey, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        if (attempt.ParticipantId != participantKey)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var version = await work.Repo<IExerciseVersionRepository>()
            .GetAsync(attempt.ExerciseVersionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        _ = ExerciseWorkspaceJson.Validate(
            request.WorkspaceJson,
            ExerciseDefinitionJson.Parse(version.DefinitionJson, name),
            name,
            "l'anteprima dei blocchi",
            requireEntryPoint: false);

        var nowUtc = time.GetUtcNow();
        previews.Update(new ParticipantPreviewDto(connection.SessionId, participantId, request.AttemptId,
            request.ConnectionEpoch, request.ExecutionGeneration, request.ProjectRevision,
            request.WorkspaceHash, request.WorkspaceJson, nowUtc));

        return new WorkspacePreviewDto(participantId, connection.SessionId, request.AttemptId,
            request.ProjectRevision, request.WorkspaceHash, nowUtc);
    }
}

/// <summary>
/// Legge l'ultima anteprima disponibile per un alunno della sessione osservata (§10.5, §15.5).
/// Restituisce <c>null</c> quando non ce n'è una, o quando è scaduta: il docente vede l'assenza e,
/// se vuole il contenuto, aspetta che il client ne mandi una nuova.
/// </summary>
public sealed class GetParticipantPreviewHandler(
    IUnitOfWorkContext unitOfWorkContext,
    IParticipantPreviewCache previews) : IRequestHandler<GetParticipantPreviewQuery, ParticipantPreviewViewDto?>
{
    public async ValueTask<ParticipantPreviewViewDto?> Handle(
        GetParticipantPreviewQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var controllerAdminId = request.Context.AdminUserId
            ?? throw CodiRequestException.AuthenticationRequired(GetParticipantPreviewQuery.RequestName);

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await SessionGridGuard
            .LoadOwnedSessionAsync(work, request.SessionId, controllerAdminId, cancellationToken)
            .ConfigureAwait(false);

        var preview = previews.Get(request.ParticipantId);
        if (preview is null || preview.SessionId != request.SessionId)
        {
            return null;
        }

        var attempt = await work.Repo<IExerciseAttemptRepository>()
            .GetAsync(preview.AttemptId.ToString("D"), cancellationToken)
            .ConfigureAwait(false);

        return new ParticipantPreviewViewDto(preview.ParticipantId, preview.AttemptId, preview.ProjectRevision,
            preview.WorkspaceHash, preview.WorkspaceJson, preview.ReceivedAtUtc, attempt?.WorkspaceHash);
    }
}
