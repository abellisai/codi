using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;

namespace Qwerty.Codi.Application.Lessons;

/// <summary>Crea una lezione dentro un corso (§10.4). Una sola scrittura, quindi nessuna transazione.</summary>
public sealed record CreateLessonCommand(Guid CourseId, string Title, string? Description = null)
    : ICodiCommand<LessonDto>
{
    public static string RequestName => "CreateLesson";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>Modifica titolo e descrizione di una lezione (§10.4), con controllo di versione della riga.</summary>
public sealed record UpdateLessonCommand(
    Guid Id,
    string Title,
    string? Description = null,
    long? BaseRowVersion = null) : ICodiCommand<LessonDto>
{
    public static string RequestName => "UpdateLesson";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>Una casella della sequenza: l'esercizio che la lezione mostra in quella posizione.</summary>
public sealed record LessonExerciseInput(Guid ExerciseId);

/// <summary>
/// Sostituisce la sequenza ordinata degli esercizi di una lezione (§10.4). È l'unica richiesta che
/// riscrive righe correlate: dichiara la versione attesa della **lezione**, non delle singole
/// righe, perché la sequenza è un valore unico che si sostituisce in blocco.
/// </summary>
public sealed record ReplaceLessonExercisesCommand(
    Guid LessonId,
    IReadOnlyList<LessonExerciseInput> Exercises,
    long? BaseRowVersion = null) : ICodiCommand<IReadOnlyList<LessonExerciseDto>>
{
    public static string RequestName => "ReplaceLessonExercises";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>
/// Archivia una lezione (§10.4, §6.1). L'archiviazione non cancella: la lezione resta leggibile
/// con le sue attività, esce solo dal programma attivo del corso.
/// </summary>
public sealed record ArchiveLessonCommand(Guid Id, long? BaseRowVersion = null) : ICodiCommand<LessonDto>
{
    public static string RequestName => "ArchiveLesson";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}
