using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;

namespace Qwerty.Codi.Application.Organization;

/// <summary>Crea una scuola (§10.4). Il nome è unico fra le scuole, normalizzato (§7.2).</summary>
public sealed record CreateSchoolCommand(string Name) : ICodiCommand<SchoolDto>
{
    public static string RequestName => "CreateSchool";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>
/// Modifica il nome di una scuola (§10.4). La versione attesa è facoltativa nel record perché la
/// sua assenza ha un esito proprio — <c>428 EntityVersionRequired</c> di §10.2 — e non un corpo
/// non deserializzabile.
/// </summary>
public sealed record UpdateSchoolCommand(Guid Id, string Name, long? BaseRowVersion = null)
    : ICodiCommand<SchoolDto>
{
    public static string RequestName => "UpdateSchool";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>
/// Archivia una scuola (§10.4, §6.1). L'archiviazione conserva lo storico: la scuola resta
/// leggibile e non viene eliminata.
/// </summary>
public sealed record ArchiveSchoolCommand(Guid Id, long? BaseRowVersion = null) : ICodiCommand<SchoolDto>
{
    public static string RequestName => "ArchiveSchool";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}
