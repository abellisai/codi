using Qwerty.Codi.Domain.Lessons;
namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Una riga della sequenza di una lezione (§7.3, §10.5): l'associazione, la lezione, l'esercizio
/// mostrato e la sua posizione. La posizione è quella definitiva, <c>1..N</c>: le posizioni
/// temporanee negative di §7.3 non sopravvivono alla transazione che le usa.
/// </summary>
public sealed record LessonExerciseDto(
    Guid Id,
    Guid LessonId,
    Guid ExerciseId,
    int Position,
    long RowVersion)
{
    /// <summary>
    /// Copia dalla riga di dominio. La versione è quella della riga, non della lezione: la sequenza
    /// è un valore unico che si sostituisce in blocco, ma ogni casella conserva la propria identità.
    /// </summary>
    public static LessonExerciseDto FromDomain(LessonExercise lessonExercise) => new(
        lessonExercise.Id,
        lessonExercise.LessonId,
        lessonExercise.ExerciseId,
        lessonExercise.Position,
        lessonExercise.RowVersion);
}
