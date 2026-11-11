using Qwerty.Codi.Domain.Lessons;
namespace Qwerty.Codi.Application.Dto;

/// <summary>Lezione come la vede il client (§10.5).</summary>
public sealed record LessonDto(
    Guid Id,
    Guid CourseId,
    string Title,
    string Description,
    bool IsArchived,
    long RowVersion)
{
    public static LessonDto FromDomain(Lesson lesson)
    {
        ArgumentNullException.ThrowIfNull(lesson);

        return new LessonDto(
            lesson.Id, lesson.CourseId, lesson.Title, lesson.Description, lesson.IsArchived, lesson.RowVersion);
    }
}
