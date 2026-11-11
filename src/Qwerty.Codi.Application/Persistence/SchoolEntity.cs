using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Domain.Organization;

namespace Qwerty.Codi.Application.Persistence;

/// <summary>
/// Entità di persistenza di <c>school</c>. Il mapping SQL vive in Infrastructure, le invarianti
/// restano nel dominio. UUID canonici, flag interi e date epoch millisecondi seguono §7.1.
/// </summary>
public sealed class SchoolEntity : IPersistedEntity<string>
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public long IsArchivedFlag { get; set; }
    public long CreatedAtUtcMs { get; set; }
    public long UpdatedAtUtcMs { get; set; }
    public long RowVersion { get; set; }

    public static SchoolEntity FromDomain(School school)
    {
        ArgumentNullException.ThrowIfNull(school);

        return new SchoolEntity
        {
            Id = school.Id.ToString("D"),
            Name = school.Name,
            NormalizedName = school.NormalizedName,
            IsArchivedFlag = school.IsArchived ? 1 : 0,
            CreatedAtUtcMs = school.CreatedAtUtc.ToUnixTimeMilliseconds(),
            UpdatedAtUtcMs = school.UpdatedAtUtc.ToUnixTimeMilliseconds(),
            RowVersion = school.RowVersion,
        };
    }

    /// <summary>Ricostruisce e valida anche dati scritti da strumenti esterni.</summary>
    public School ToDomain() => School.Restore(
        Guid.Parse(Id),
        Name,
        NormalizedName,
        ToArchivedState(),
        DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtUtcMs),
        DateTimeOffset.FromUnixTimeMilliseconds(UpdatedAtUtcMs),
        RowVersion);

    /// <summary>
    /// Lo stato di archiviazione si legge come flag intero (0 o 1): un altro valore non è uno
    /// stato, e leggerlo come «non archiviata» riaprirebbe in silenzio una scuola archiviata.
    /// </summary>
    private bool ToArchivedState() => IsArchivedFlag switch
    {
        0 => false,
        1 => true,
        _ => throw new ArgumentException(
            $"Stato di archiviazione non riconosciuto: '{IsArchivedFlag}'.", nameof(IsArchivedFlag)),
    };
}
