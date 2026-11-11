using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;

namespace Qwerty.Codi.Application.Organization;

/// <summary>Elenco dei corsi in ordine di titolo (§10.5).</summary>
public sealed record ListCoursesQuery(bool IncludeArchived = false) : ICodiQuery<IReadOnlyList<CourseDto>>
{
    public static string RequestName => "ListCourses";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>Dettaglio di un corso, compresi quelli archiviati (§10.5).</summary>
public sealed record GetCourseQuery(Guid Id) : ICodiQuery<CourseDto>
{
    public static string RequestName => "GetCourse";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}
