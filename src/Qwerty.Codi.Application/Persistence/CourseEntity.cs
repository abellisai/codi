using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Domain.Organization;

namespace Qwerty.Codi.Application.Persistence;

/// <summary>
/// Entità di persistenza di <c>course</c>. La descrizione si memorizza come stringa, vuota quando
/// il docente non l'ha scritta: la colonna non è annullabile e «nessuna descrizione» è un valore,
/// non un dato mancante.
/// </summary>
public sealed class CourseEntity : IPersistedEntity<string>
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long IsArchivedFlag { get; set; }
    public long CreatedAtUtcMs { get; set; }
    public long UpdatedAtUtcMs { get; set; }
    public long RowVersion { get; set; }

    public static CourseEntity FromDomain(Course course)
    {
        ArgumentNullException.ThrowIfNull(course);

        return new CourseEntity
        {
            Id = course.Id.ToString("D"),
            Title = course.Title,
            Description = course.Description,
            IsArchivedFlag = course.IsArchived ? 1 : 0,
            CreatedAtUtcMs = course.CreatedAtUtc.ToUnixTimeMilliseconds(),
            UpdatedAtUtcMs = course.UpdatedAtUtc.ToUnixTimeMilliseconds(),
            RowVersion = course.RowVersion,
        };
    }

    /// <summary>Ricostruisce e valida anche dati scritti da strumenti esterni.</summary>
    public Course ToDomain() => Course.Restore(
        Guid.Parse(Id),
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
