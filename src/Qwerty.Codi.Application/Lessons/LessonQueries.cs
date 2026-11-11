using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;

namespace Qwerty.Codi.Application.Lessons;

/// <summary>Lezioni di un corso in ordine di titolo (§10.5).</summary>
public sealed record ListLessonsQuery(Guid CourseId, bool IncludeArchived = false)
    : ICodiQuery<IReadOnlyList<LessonDto>>
{
    public static string RequestName => "ListLessons";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>Dettaglio di una lezione (§10.5).</summary>
public sealed record GetLessonQuery(Guid Id) : ICodiQuery<LessonDto>
{
    public static string RequestName => "GetLesson";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>
/// Sequenza ordinata degli esercizi di una lezione (§10.5). Una lezione senza esercizi produce un
/// elenco vuoto e non un errore: è una lettura, e una sequenza vuota è uno stato ammesso.
/// </summary>
public sealed record ListLessonExercisesQuery(Guid LessonId) : ICodiQuery<IReadOnlyList<LessonExerciseDto>>
{
    public static string RequestName => "ListLessonExercises";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}
