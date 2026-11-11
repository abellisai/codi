using Qwerty.Codi.Domain.Sessions;
namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Sessione come la mostra la pagina admin delle sessioni (§10.5): stato, ingressi e revisione
/// della sessione, con i titoli della catena lezione → corso → classe → scuola per riconoscerla
/// nell'elenco. È distinta da <see cref="SessionDto"/>, che è l'esito dei comandi di regia e porta
/// la versione di riga: qui contano i dati da leggere, non la concorrenza ottimistica.
///
/// Gli istanti sono ISO 8601 UTC e <c>sessionRevision</c> viaggia come stringa decimale (§10.2),
/// come gli altri contatori a 64 bit del contratto.
/// </summary>
public sealed record SessionSummaryDto(
    Guid Id,
    string Status,
    bool AdmissionsOpen,
    string? JoinCode,
    long SessionRevision,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    Guid LessonId,
    string LessonTitle,
    string CourseTitle,
    string ClassroomName,
    string SchoolName)
{
    public static SessionSummaryDto FromDomain(
        LessonSession session,
        string lessonTitle,
        string courseTitle,
        string classroomName,
        string schoolName)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new SessionSummaryDto(
            session.Id,
            session.Status.ToString(),
            session.AdmissionsOpen,
            session.JoinCode?.Value,
            session.SessionRevision,
            session.StartedAtUtc,
            session.EndedAtUtc,
            session.LessonId,
            lessonTitle,
            courseTitle,
            classroomName,
            schoolName);
    }
}
