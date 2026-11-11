using Qwerty.Codi.Domain.Exercises;
namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Esercizio come lo vede il client (§10.5). Il <see cref="ExerciseType"/> è il tipo di attività
/// scelto alla creazione: l'editor lo usa per mostrare i campi giusti anche prima che esista una
/// bozza.
/// </summary>
public sealed record ExerciseDto(
    Guid Id,
    string ExerciseType,
    string Title,
    string Description,
    bool IsArchived,
    long RowVersion,
    string PublicationStatus = "Draft")
{
    public static ExerciseDto FromDomain(Exercise exercise)
    {
        ArgumentNullException.ThrowIfNull(exercise);

        return new ExerciseDto(
            exercise.Id, exercise.ExerciseType, exercise.Title, exercise.Description,
            exercise.IsArchived, exercise.RowVersion);
    }
}
