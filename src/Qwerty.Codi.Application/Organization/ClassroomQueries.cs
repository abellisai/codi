using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;

namespace Qwerty.Codi.Application.Organization;

/// <summary>
/// Classi di una scuola (§10.5). Le archiviate restano fuori per impostazione predefinita: sono
/// ancora leggibili dal dettaglio, ma non fanno parte della configurazione attiva.
/// </summary>
public sealed record ListClassroomsQuery(Guid SchoolId, bool IncludeArchived = false)
    : ICodiQuery<IReadOnlyList<ClassroomDto>>
{
    public static string RequestName => "ListClassrooms";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>Dettaglio di una classe, comprese quelle archiviate (§10.5).</summary>
public sealed record GetClassroomQuery(Guid Id) : ICodiQuery<ClassroomDto>
{
    public static string RequestName => "GetClassroom";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}
