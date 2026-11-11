using Qwerty.Codi.Domain.Organization;
namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Classe accessibile nella scoperta pubblica (§10.3): identificativo, scuola di appartenenza,
/// nome e anno scolastico. Identificativo e scuola servono al join che segue; nessun dato interno
/// o contatore viene esposto a un client ancora anonimo.
/// </summary>
public sealed record AvailableClassroomDto(Guid Id, Guid SchoolId, string Name, string SchoolYear)
{
    public static AvailableClassroomDto FromDomain(Classroom classroom)
    {
        ArgumentNullException.ThrowIfNull(classroom);

        return new AvailableClassroomDto(classroom.Id, classroom.SchoolId, classroom.Name, classroom.SchoolYear);
    }
}
