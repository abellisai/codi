using Qwerty.Codi.Domain.Organization;
namespace Qwerty.Codi.Application.Dto;

/// <summary>Corso come lo vede il client (§10.5).</summary>
public sealed record CourseDto(Guid Id, string Title, string Description, bool IsArchived, long RowVersion)
{
    public static CourseDto FromDomain(Course course)
    {
        ArgumentNullException.ThrowIfNull(course);

        return new CourseDto(course.Id, course.Title, course.Description, course.IsArchived, course.RowVersion);
    }
}
