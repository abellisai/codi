using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Domain.Exercises;

namespace Qwerty.Codi.Application.Persistence;

/// <summary>
/// Entità di persistenza di <c>exercise</c>. La descrizione si memorizza come stringa, vuota quando
/// il docente non l'ha scritta: la colonna non è annullabile e «nessuna descrizione» è un valore,
/// non un dato mancante. Il tipo di attività è una colonna propria, perché l'editor ne ha bisogno
/// prima che la bozza esista.
/// </summary>
public sealed class ExerciseEntity : IPersistedEntity<string>
{
    public string Id { get; set; } = string.Empty;
    public string ExerciseType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long IsArchivedFlag { get; set; }
    public long CreatedAtUtcMs { get; set; }
    public long UpdatedAtUtcMs { get; set; }
    public long RowVersion { get; set; }

    public static ExerciseEntity FromDomain(Exercise exercise)
    {
        ArgumentNullException.ThrowIfNull(exercise);

        return new ExerciseEntity
        {
            Id = exercise.Id.ToString("D"),
            ExerciseType = exercise.ExerciseType,
            Title = exercise.Title,
            Description = exercise.Description,
            IsArchivedFlag = exercise.IsArchived ? 1 : 0,
            CreatedAtUtcMs = exercise.CreatedAtUtc.ToUnixTimeMilliseconds(),
            UpdatedAtUtcMs = exercise.UpdatedAtUtc.ToUnixTimeMilliseconds(),
            RowVersion = exercise.RowVersion,
        };
    }

    /// <summary>Ricostruisce e valida anche dati scritti da strumenti esterni.</summary>
    public Exercise ToDomain() => Exercise.Restore(
        Guid.Parse(Id),
        ExerciseType,
        Title,
        Description,
        ToArchivedState(),
        DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtUtcMs),
        DateTimeOffset.FromUnixTimeMilliseconds(UpdatedAtUtcMs),
        RowVersion);

    private bool ToArchivedState() => IsArchivedFlag switch
    {
        0 => false,
        1 => true,
        _ => throw new ArgumentException(
            $"Stato di archiviazione non riconosciuto: '{IsArchivedFlag}'.", nameof(IsArchivedFlag)),
    };
}
