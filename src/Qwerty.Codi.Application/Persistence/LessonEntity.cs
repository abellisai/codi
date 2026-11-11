using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Domain.Lessons;

namespace Qwerty.Codi.Application.Persistence;

/// <summary>
/// Entità di persistenza di <c>lesson</c>. La descrizione si memorizza come stringa, vuota quando
/// il docente non l'ha scritta: la colonna non è annullabile e «nessuna descrizione» è un valore,
/// non un dato mancante.
/// </summary>
public sealed class LessonEntity : IPersistedEntity<string>
{
    public string Id { get; set; } = string.Empty;
    public string CourseId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long IsArchivedFlag { get; set; }
    public long CreatedAtUtcMs { get; set; }
    public long UpdatedAtUtcMs { get; set; }
    public long RowVersion { get; set; }

    public static LessonEntity FromDomain(Lesson lesson)
    {
        ArgumentNullException.ThrowIfNull(lesson);

        return new LessonEntity
        {
            Id = lesson.Id.ToString("D"),
            CourseId = lesson.CourseId.ToString("D"),
            Title = lesson.Title,
            Description = lesson.Description,
            IsArchivedFlag = lesson.IsArchived ? 1 : 0,
            CreatedAtUtcMs = lesson.CreatedAtUtc.ToUnixTimeMilliseconds(),
            UpdatedAtUtcMs = lesson.UpdatedAtUtc.ToUnixTimeMilliseconds(),
            RowVersion = lesson.RowVersion,
        };
    }

    /// <summary>Ricostruisce e valida anche dati scritti da strumenti esterni.</summary>
    public Lesson ToDomain() => Lesson.Restore(
        Guid.Parse(Id),
        Guid.Parse(CourseId),
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
