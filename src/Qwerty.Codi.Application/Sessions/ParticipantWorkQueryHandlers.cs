using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Exercises;
using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Materiale del lavoro di un alunno, letto in un punto solo: la riga di stato dice **quale**
/// assegnazione e quale tentativo sono correnti, e da lì si risale all'assegnazione, alla versione
/// fissata e all'identità dell'esercizio. Duplicare questa catena in due letture significherebbe
/// poterle far divergere, e l'alunno vedrebbe una definizione con il progetto di un'altra versione.
///
/// Una riga di stato assente o un tentativo assente non sono «nessuna attività»: sono la richiesta
/// di un oggetto che non esiste, e la risposta è una risorsa assente in entrambi i casi.
/// </summary>
internal static class ParticipantWorkLoader
{
    public static async Task<ParticipantWork> LoadAsync(
        IUnitOfWork work,
        Guid participantId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        var key = participantId.ToString("D");
        var state = await work.Repo<IParticipantStateRepository>()
            .GetAsync(key, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        if (state.AssignmentId is null || state.AttemptId is null)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var attempt = await work.Repo<IExerciseAttemptRepository>()
            .GetAsync(state.AttemptId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        if (!string.Equals(attempt.ParticipantId, key, StringComparison.Ordinal))
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var assignment = await work.Repo<IActivityAssignmentRepository>()
            .GetAsync(attempt.AssignmentId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var version = await work.Repo<IExerciseVersionRepository>()
            .GetAsync(attempt.ExerciseVersionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var exercise = await work.Repo<IExerciseRepository>()
            .GetAsync(version.ExerciseId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        return new ParticipantWork(state, attempt, assignment, version, exercise.Title);
    }
}

/// <summary>Le righe che descrivono il lavoro corrente di un alunno, lette nella stessa unità di lavoro.</summary>
internal sealed record ParticipantWork(
    ParticipantStateEntity State,
    ExerciseAttemptEntity Attempt,
    ActivityAssignmentEntity Assignment,
    ExerciseVersionEntity Version,
    string ExerciseTitle);

/// <summary>
/// Definizione pubblica dell'esercizio assegnato (§10.5). L'identità arriva dalla credenziale, e la
/// lettura non distingue «non esiste» da «non è tuo»: un altro alunno non può chiedere questo
/// materiale, quindi non c'è nulla da rivelargli con un codice più preciso.
/// </summary>
public sealed class GetAssignedExerciseHandler(
    IUnitOfWorkContext unitOfWorkContext) : IRequestHandler<GetAssignedExerciseQuery, AssignedExerciseDto>
{
    public async ValueTask<AssignedExerciseDto> Handle(
        GetAssignedExerciseQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.ParticipantId is not { } participantId)
        {
            throw CodiRequestException.AuthenticationRequired(GetAssignedExerciseQuery.RequestName);
        }

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var loaded = await ParticipantWorkLoader
            .LoadAsync(work, participantId, cancellationToken)
            .ConfigureAwait(false);

        var attempt = loaded.Attempt.ToDomain();

        return new AssignedExerciseDto(
            attempt.SessionId,
            attempt.AssignmentId,
            attempt.ExerciseVersionId,
            Guid.TryParse(loaded.Assignment.LessonExerciseId, out var lessonExerciseId) ? lessonExerciseId : null,
            loaded.Assignment.Scope,
            attempt.Id,
            attempt.AttemptNumber,
            Guid.Parse(loaded.Version.ExerciseId),
            loaded.ExerciseTitle,
            ExerciseDefinitionMetadata.ResolveExerciseType(loaded.Version.DefinitionJson) ?? string.Empty,
            loaded.Version.DefinitionJson,
            loaded.Version.EngineVersion,
            loaded.Version.SchemaVersion,
            loaded.Version.ContentHash);
    }
}

/// <summary>
/// Progetto e checkpoint del proprio tentativo (§10.5). Il tentativo è quello corrente dello stato
/// desiderato: dopo un azzeramento questa lettura porta il tentativo nuovo, e il vecchio resta nello
/// storico senza essere raggiungibile da qui.
/// </summary>
public sealed class GetAttemptHandler(
    IUnitOfWorkContext unitOfWorkContext) : IRequestHandler<GetAttemptQuery, AttemptDto>
{
    public async ValueTask<AttemptDto> Handle(GetAttemptQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.ParticipantId is not { } participantId)
        {
            throw CodiRequestException.AuthenticationRequired(GetAttemptQuery.RequestName);
        }

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var loaded = await ParticipantWorkLoader
            .LoadAsync(work, participantId, cancellationToken)
            .ConfigureAwait(false);

        var attempt = loaded.Attempt;

        return new AttemptDto(
            Guid.Parse(attempt.Id),
            Guid.Parse(attempt.AssignmentId),
            Guid.Parse(attempt.ExerciseVersionId),
            attempt.AttemptNumber,
            attempt.Status,
            attempt.WorkspaceJson,
            attempt.WorkspaceHash,
            attempt.ProjectRevision,
            attempt.CheckpointJson,
            attempt.CheckpointEngineVersion,
            attempt.LastSavedAtUtcMs is { } saved ? DateTimeOffset.FromUnixTimeMilliseconds(saved) : null,
            DateTimeOffset.FromUnixTimeMilliseconds(attempt.UpdatedAtUtcMs), attempt.RowVersion);
    }
}
