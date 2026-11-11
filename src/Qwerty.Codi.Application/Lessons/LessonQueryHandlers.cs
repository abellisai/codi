using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Lessons;

/// <summary>
/// Lezioni di un corso (§10.5), in ordine di titolo senza distinzione di maiuscole. Un corso
/// inesistente produce un elenco vuoto, non un errore: il filtro è una lettura, e chiedere le
/// lezioni di un identificativo sbagliato non è un'operazione pericolosa.
/// </summary>
public sealed class ListLessonsHandler(IUnitOfWorkContext unitOfWorkContext)
    : IRequestHandler<ListLessonsQuery, IReadOnlyList<LessonDto>>
{
    public async ValueTask<IReadOnlyList<LessonDto>> Handle(
        ListLessonsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var courseId = request.CourseId.ToString("D");
        var lessons = work.Repo<ILessonRepository>();

        var rows = request.IncludeArchived
            ? await lessons.QueryAsync(entity => entity.CourseId == courseId, cancellationToken).ConfigureAwait(false)
            : await lessons.QueryAsync(
                entity => entity.CourseId == courseId && entity.IsArchivedFlag == 0, cancellationToken)
                .ConfigureAwait(false);

        return
        [
            .. rows
                .Select(row => LessonDto.FromDomain(row.ToDomain()))
                .OrderBy(lesson => lesson.Title, StringComparer.OrdinalIgnoreCase),
        ];
    }
}

/// <summary>Dettaglio di una lezione (§10.5).</summary>
public sealed class GetLessonHandler(IUnitOfWorkContext unitOfWorkContext)
    : IRequestHandler<GetLessonQuery, LessonDto>
{
    public async ValueTask<LessonDto> Handle(GetLessonQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var stored = await work.Repo<ILessonRepository>()
            .GetAsync(request.Id.ToString("D"), cancellationToken)
            .ConfigureAwait(false) ?? throw CodiRequestException.ResourceNotFound();

        return LessonDto.FromDomain(stored.ToDomain());
    }
}

/// <summary>
/// Sequenza ordinata degli esercizi di una lezione (§10.5). L'ordine è quello delle posizioni
/// definitive: una posizione temporanea non sopravvive alla transazione che la usa.
/// </summary>
public sealed class ListLessonExercisesHandler(IUnitOfWorkContext unitOfWorkContext)
    : IRequestHandler<ListLessonExercisesQuery, IReadOnlyList<LessonExerciseDto>>
{
    public async ValueTask<IReadOnlyList<LessonExerciseDto>> Handle(
        ListLessonExercisesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var lessonId = request.LessonId.ToString("D");
        var rows = await work.Repo<ILessonExerciseRepository>()
            .QueryAsync(entity => entity.LessonId == lessonId, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows
                .Select(row => LessonExerciseDto.FromDomain(row.ToDomain()))
                .OrderBy(row => row.Position),
        ];
    }
}
