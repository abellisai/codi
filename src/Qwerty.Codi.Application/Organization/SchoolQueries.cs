using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;

namespace Qwerty.Codi.Application.Organization;

/// <summary>
/// Elenco delle scuole, in ordine di nome (§10.5). Le archiviate restano fuori per impostazione
/// predefinita: sono ancora leggibili, ma non fanno parte della configurazione operativa.
/// </summary>
public sealed record ListSchoolsQuery(bool IncludeArchived = false) : ICodiQuery<IReadOnlyList<SchoolDto>>
{
    public static string RequestName => "ListSchools";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>Dettaglio di una scuola, comprese quelle archiviate (§10.5).</summary>
public sealed record GetSchoolQuery(Guid Id) : ICodiQuery<SchoolDto>
{
    public static string RequestName => "GetSchool";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}
