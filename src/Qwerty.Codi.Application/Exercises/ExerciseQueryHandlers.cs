using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;

namespace Qwerty.Codi.Application.Exercises;

/// <summary>Dettaglio di un esercizio (§10.5), archiviati compresi.</summary>
public sealed class GetExerciseHandler(IUnitOfWorkContext unitOfWorkContext)
    : IRequestHandler<GetExerciseQuery, ExerciseDto>
{
    public async ValueTask<ExerciseDto> Handle(GetExerciseQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var stored = await work.Repo<IExerciseRepository>()
            .GetAsync(request.Id.ToString("D"), cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var status = await ExercisePublicationStatus.ReadAsync(work, stored.Id, cancellationToken).ConfigureAwait(false);
        return ExerciseDto.FromDomain(stored.ToDomain()) with { PublicationStatus = status };
    }
}

/// <summary>Elenco degli esercizi (§10.5), in ordine di titolo senza distinzione di maiuscole.</summary>
public sealed class ListExercisesHandler(IUnitOfWorkContext unitOfWorkContext)
    : IRequestHandler<ListExercisesQuery, IReadOnlyList<ExerciseDto>>
{
    public async ValueTask<IReadOnlyList<ExerciseDto>> Handle(
        ListExercisesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var exercises = work.Repo<IExerciseRepository>();
        var rows = request.IncludeArchived
            ? await exercises.GetAllAsync(cancellationToken).ConfigureAwait(false)
            : await exercises.QueryAsync(entity => entity.IsArchivedFlag == 0, cancellationToken).ConfigureAwait(false);

        var statuses = await ExercisePublicationStatus.ReadAllAsync(work, cancellationToken).ConfigureAwait(false);

        return
        [
            .. rows
                .Select(row => ExerciseDto.FromDomain(row.ToDomain()) with { PublicationStatus = statuses.GetValueOrDefault(row.Id, "Draft") })
                .OrderBy(exercise => exercise.Title, StringComparer.OrdinalIgnoreCase),
        ];
    }
}

/// <summary>
/// Bozza di un esercizio per l'editor (§10.5). È una lettura pura: il documento memorizzato esce
/// così com'è stato salvato, perché la validazione appartiene alla scrittura e ripeterla qui non
/// aggiungerebbe nulla a un documento che è entrato solo passando da quella.
/// </summary>
public sealed class GetExerciseDraftHandler(IUnitOfWorkContext unitOfWorkContext)
    : IRequestHandler<GetExerciseDraftQuery, ExerciseDraftDto>
{
    public async ValueTask<ExerciseDraftDto> Handle(
        GetExerciseDraftQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var draft = await work.Repo<IExerciseDraftRepository>()
            .GetAsync(request.ExerciseId.ToString("D"), cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        return new ExerciseDraftDto(
            Guid.Parse(draft.Id),
            draft.DefinitionJson,
            draft.WorkspaceJson,
            draft.RowVersion,
            DateTimeOffset.FromUnixTimeMilliseconds(draft.UpdatedAtUtcMs));
    }
}

/// <summary>
/// Soluzione di riferimento della bozza (§10.5). È una lettura pura e separata dalla bozza: chi
/// carica l'esercizio per comporlo non riceve la risposta insieme al resto, e chi la vuole la
/// chiede, con la stessa policy.
/// </summary>
public sealed class GetReferenceSolutionHandler(IUnitOfWorkContext unitOfWorkContext)
    : IRequestHandler<GetReferenceSolutionQuery, ExerciseReferenceSolutionDto>
{
    public async ValueTask<ExerciseReferenceSolutionDto> Handle(
        GetReferenceSolutionQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var draft = await work.Repo<IExerciseDraftRepository>()
            .GetAsync(request.ExerciseId.ToString("D"), cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        return new ExerciseReferenceSolutionDto(Guid.Parse(draft.Id), draft.ReferenceSolutionJson);
    }
}

/// <summary>Versioni pubblicate di un esercizio (§10.5), dalla più recente alla prima.</summary>
public sealed class ListExerciseVersionsHandler(IUnitOfWorkContext unitOfWorkContext)
    : IRequestHandler<ListExerciseVersionsQuery, IReadOnlyList<ExerciseVersionDto>>
{
    public async ValueTask<IReadOnlyList<ExerciseVersionDto>> Handle(
        ListExerciseVersionsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var rows = await work.Repo<IExerciseVersionRepository>()
            .QueryAsync(row => row.ExerciseId == request.ExerciseId.ToString("D"), cancellationToken)
            .ConfigureAwait(false);

        // Le note dell'autore vivono nella riga compagna privata della versione, che la tabella
        // delle versioni non espone: si leggono per chiave primaria, una per versione pubblicata.
        var privateRows = work.Repo<IExerciseVersionPrivateRepository>();
        var versions = new List<ExerciseVersionDto>(rows.Count);

        foreach (var row in rows.OrderByDescending(row => row.VersionNumber))
        {
            var companion = await privateRows.GetAsync(row.Id, cancellationToken).ConfigureAwait(false);
            versions.Add(ExerciseVersionDto.FromEntity(row, companion?.AuthorNotes));
        }

        return versions;
    }
}

/// <summary>Dettaglio di una versione pubblicata (§10.5), indicata dal suo numero per esercizio.</summary>
public sealed class GetExerciseVersionHandler(IUnitOfWorkContext unitOfWorkContext)
    : IRequestHandler<GetExerciseVersionQuery, ExerciseVersionDto>
{
    public async ValueTask<ExerciseVersionDto> Handle(
        GetExerciseVersionQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var version = (await work.Repo<IExerciseVersionRepository>()
                .QueryAsync(
                    row => row.ExerciseId == request.ExerciseId.ToString("D")
                        && row.VersionNumber == request.VersionNumber,
                    cancellationToken)
                .ConfigureAwait(false))
            .SingleOrDefault()
            ?? throw CodiRequestException.ResourceNotFound();

        var companion = await work.Repo<IExerciseVersionPrivateRepository>()
            .GetAsync(version.Id, cancellationToken)
            .ConfigureAwait(false);

        return ExerciseVersionDto.FromEntity(version, companion?.AuthorNotes);
    }
}
