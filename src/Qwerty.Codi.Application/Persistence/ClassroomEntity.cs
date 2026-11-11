using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Domain.Organization;

namespace Qwerty.Codi.Application.Persistence;

/// <summary>
/// Entità di persistenza di <c>classroom</c>. Il mapping SQL vive in Infrastructure, le invarianti
/// restano nel dominio. UUID canonici, flag interi e date epoch millisecondi seguono §7.1.
/// </summary>
public sealed class ClassroomEntity : IPersistedEntity<string>
{
    public string Id { get; set; } = string.Empty;
    public string SchoolId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string SchoolYear { get; set; } = string.Empty;
    public long IsArchivedFlag { get; set; }
    public long CreatedAtUtcMs { get; set; }
    public long UpdatedAtUtcMs { get; set; }
    public long RowVersion { get; set; }

    public static ClassroomEntity FromDomain(Classroom classroom)
    {
        ArgumentNullException.ThrowIfNull(classroom);

        return new ClassroomEntity
        {
            Id = classroom.Id.ToString("D"),
            SchoolId = classroom.SchoolId.ToString("D"),
            Name = classroom.Name,
            NormalizedName = classroom.NormalizedName,
            SchoolYear = classroom.SchoolYear,
            IsArchivedFlag = classroom.IsArchived ? 1 : 0,
            CreatedAtUtcMs = classroom.CreatedAtUtc.ToUnixTimeMilliseconds(),
            UpdatedAtUtcMs = classroom.UpdatedAtUtc.ToUnixTimeMilliseconds(),
            RowVersion = classroom.RowVersion,
        };
    }

    /// <summary>Ricostruisce e valida anche dati scritti da strumenti esterni.</summary>
    public Classroom ToDomain() => Classroom.Restore(
        Guid.Parse(Id),
        Guid.Parse(SchoolId),
        Name,
        NormalizedName,
        SchoolYear,
        ToArchivedState(),
        DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtUtcMs),
        DateTimeOffset.FromUnixTimeMilliseconds(UpdatedAtUtcMs),
        RowVersion);

    /// <summary>Lo stato di archiviazione è un flag intero: un altro valore non è uno stato.</summary>
    private bool ToArchivedState() => IsArchivedFlag switch
    {
        0 => false,
        1 => true,
        _ => throw new ArgumentException(
            $"Stato di archiviazione non riconosciuto: '{IsArchivedFlag}'.", nameof(IsArchivedFlag)),
    };
}
