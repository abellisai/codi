using Qwerty.Codi.Domain.Organization;
namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Scuola come la vede il client (§10.5): identificativo, nome, stato di archiviazione e versione
/// della riga, che il client deve rimandare per modificarla (§10.2). Non è l'entità memorizzata e
/// non porta date interne.
/// </summary>
public sealed record SchoolDto(Guid Id, string Name, bool IsArchived, long RowVersion)
{
    public static SchoolDto FromDomain(School school)
    {
        ArgumentNullException.ThrowIfNull(school);

        return new SchoolDto(school.Id, school.Name, school.IsArchived, school.RowVersion);
    }
}
