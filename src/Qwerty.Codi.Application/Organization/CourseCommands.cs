using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;

namespace Qwerty.Codi.Application.Organization;

/// <summary>Crea un corso (§10.4). Il titolo non è una chiave: due corsi possono avere lo stesso titolo.</summary>
public sealed record CreateCourseCommand(string Title, string? Description = null)
    : ICodiCommand<CourseDto>
{
    public static string RequestName => "CreateCourse";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>Modifica titolo e descrizione di un corso (§10.4).</summary>
public sealed record UpdateCourseCommand(Guid Id, string Title, string? Description = null, long? BaseRowVersion = null)
    : ICodiCommand<CourseDto>
{
    public static string RequestName => "UpdateCourse";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>Archivia un corso (§10.4, §6.1): lo storico resta, il corso esce dalla configurazione attiva.</summary>
public sealed record ArchiveCourseCommand(Guid Id, long? BaseRowVersion = null) : ICodiCommand<CourseDto>
{
    public static string RequestName => "ArchiveCourse";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}
