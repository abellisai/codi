using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Domain.Lessons;

namespace Qwerty.Codi.Application.Persistence;

/// <summary>
/// Entità di persistenza di <c>lesson_exercise</c>. I riferimenti sono stringhe e non <c>Guid</c>
/// (A-06): la riga appartiene a una lezione e punta a un esercizio, entrambi obbligatori.
/// </summary>
public sealed class LessonExerciseEntity : IPersistedEntity<string>
{
    public string Id { get; set; } = string.Empty;
    public string LessonId { get; set; } = string.Empty;
    public string ExerciseId { get; set; } = string.Empty;
    public long Position { get; set; }
    public long CreatedAtUtcMs { get; set; }
    public long UpdatedAtUtcMs { get; set; }
    public long RowVersion { get; set; }

    public static LessonExerciseEntity FromDomain(LessonExercise lessonExercise)
    {
        ArgumentNullException.ThrowIfNull(lessonExercise);

        return new LessonExerciseEntity
        {
            Id = lessonExercise.Id.ToString("D"),
            LessonId = lessonExercise.LessonId.ToString("D"),
            ExerciseId = lessonExercise.ExerciseId.ToString("D"),
            Position = lessonExercise.Position,
            CreatedAtUtcMs = lessonExercise.CreatedAtUtc.ToUnixTimeMilliseconds(),
            UpdatedAtUtcMs = lessonExercise.UpdatedAtUtc.ToUnixTimeMilliseconds(),
            RowVersion = lessonExercise.RowVersion,
        };
    }

    /// <summary>Ricostruisce e valida anche dati scritti da strumenti esterni.</summary>
    public LessonExercise ToDomain()
    {
        if (Position is < int.MinValue or > int.MaxValue)
        {
            throw new ArgumentException(
                $"La posizione dell'esercizio è fuori dall'intervallo ammesso: '{Position}'.", nameof(Position));
        }

        return LessonExercise.Restore(
            Guid.Parse(Id),
            Guid.Parse(LessonId),
            Guid.Parse(ExerciseId),
            (int)Position,
            DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtUtcMs),
            DateTimeOffset.FromUnixTimeMilliseconds(UpdatedAtUtcMs),
            RowVersion);
    }
}
