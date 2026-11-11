using Qwerty.Codi.Domain.Organization;
namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Classe come la vede il client (§10.5). La scuola di appartenenza fa parte del contratto: una
/// classe si legge sempre dentro la sua scuola, e i due identificativi servono alle associazioni.
/// </summary>
public sealed record ClassroomDto(
    Guid Id,
    Guid SchoolId,
    string Name,
    string SchoolYear,
    bool IsArchived,
    long RowVersion)
{
    public static ClassroomDto FromDomain(Classroom classroom)
    {
        ArgumentNullException.ThrowIfNull(classroom);

        return new ClassroomDto(
            classroom.Id,
            classroom.SchoolId,
            classroom.Name,
            classroom.SchoolYear,
            classroom.IsArchived,
            classroom.RowVersion);
    }
}
