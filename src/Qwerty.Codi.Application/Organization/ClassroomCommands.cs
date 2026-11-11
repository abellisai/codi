using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;

namespace Qwerty.Codi.Application.Organization;

/// <summary>
/// Crea una classe in una scuola (§10.4). Nome e anno insieme sono unici nella scuola, e la
/// scuola deve esistere: il vincolo di chiave esterna non è il modo in cui si risponde a un
/// identificativo sbagliato.
/// </summary>
public sealed record CreateClassroomCommand(Guid SchoolId, string Name, string SchoolYear)
    : ICodiCommand<ClassroomDto>
{
    public static string RequestName => "CreateClassroom";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>
/// Modifica nome e anno scolastico di una classe (§10.4). La scuola non cambia: una classe
/// appartiene a una sola scuola, quindi spostarla sarebbe un'altra operazione (§5.2).
/// </summary>
public sealed record UpdateClassroomCommand(
    Guid Id,
    string Name,
    string SchoolYear,
    long? BaseRowVersion = null) : ICodiCommand<ClassroomDto>
{
    public static string RequestName => "UpdateClassroom";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>Archivia una classe (§10.4, §6.1): lo storico resta, la classe esce dalla configurazione attiva.</summary>
public sealed record ArchiveClassroomCommand(Guid Id, long? BaseRowVersion = null)
    : ICodiCommand<ClassroomDto>
{
    public static string RequestName => "ArchiveClassroom";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}
