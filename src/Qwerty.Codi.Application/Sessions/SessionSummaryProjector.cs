using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Compone il dettaglio di lettura delle sessioni condiviso da <c>ListSessions</c> e
/// <c>GetSession</c>: risolve la catena lezione → corso e classe → scuola usando i repository
/// esistenti, senza aggiungere metodi alle loro interfacce. Non è una richiesta del catalogo e non
/// muta nulla: legge e proietta le righe già lette.
///
/// La lettura è per identificativo e memorizza il risultato in una mappa per sessione, così una
/// sessione che condivide lezione, corso, classe o scuola con un'altra non ripete la lettura. Una
/// riga della catena assente o incoerente è uno stato corrotto: lo stesso esito delle altre
/// letture per identificativo è <c>404 ResourceNotFound</c>, non un titolo vuoto.
/// </summary>
internal static class SessionSummaryProjector
{
    public static async Task<IReadOnlyList<SessionSummaryDto>> ProjectAsync(
        IUnitOfWork work,
        IReadOnlyList<LessonSessionEntity> sessions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(sessions);

        var lessons = work.Repo<ILessonRepository>();
        var courses = work.Repo<ICourseRepository>();
        var classrooms = work.Repo<IClassroomRepository>();
        var schools = work.Repo<ISchoolRepository>();

        var lessonBySession = new Dictionary<string, LessonEntity>(StringComparer.Ordinal);
        var courseById = new Dictionary<string, CourseEntity>(StringComparer.Ordinal);
        var classroomById = new Dictionary<string, ClassroomEntity>(StringComparer.Ordinal);
        var schoolById = new Dictionary<string, SchoolEntity>(StringComparer.Ordinal);

        var projected = new List<SessionSummaryDto>(sessions.Count);

        foreach (var stored in sessions)
        {
            if (!lessonBySession.TryGetValue(stored.LessonId, out var lesson))
            {
                lesson = await lessons
                    .GetAsync(stored.LessonId, cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw CodiRequestException.ResourceNotFound();
                lessonBySession[stored.LessonId] = lesson;
            }

            if (!courseById.TryGetValue(lesson.CourseId, out var course))
            {
                course = await courses
                    .GetAsync(lesson.CourseId, cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw CodiRequestException.ResourceNotFound();
                courseById[lesson.CourseId] = course;
            }

            if (!classroomById.TryGetValue(stored.ClassroomId, out var classroom))
            {
                classroom = await classrooms
                    .GetAsync(stored.ClassroomId, cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw CodiRequestException.ResourceNotFound();
                classroomById[stored.ClassroomId] = classroom;
            }

            if (!schoolById.TryGetValue(classroom.SchoolId, out var school))
            {
                school = await schools
                    .GetAsync(classroom.SchoolId, cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw CodiRequestException.ResourceNotFound();
                schoolById[classroom.SchoolId] = school;
            }

            projected.Add(SessionSummaryDto.FromDomain(
                stored.ToDomain(), lesson.Title, course.Title, classroom.Name, school.Name));
        }

        return projected;
    }
}
