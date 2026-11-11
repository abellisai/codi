namespace Qwerty.Codi.Application.Dto;

/// <summary>Stato della sessione e ricevuta della distribuzione dopo il commit.</summary>
public sealed record SessionControlAcceptedDto(
    Guid Id, Guid LessonId, Guid ClassroomId, string Status, bool AdmissionsOpen, string? JoinCode,
    long SessionRevision, DateTimeOffset? StartedAtUtc, long RowVersion,
    Guid CommandId, Guid SessionId, DeliverySummaryDto DeliverySummary, CommandStatusReferenceDto StatusQuery)
    : SessionDto(Id, LessonId, ClassroomId, Status, AdmissionsOpen, JoinCode, SessionRevision, StartedAtUtc, RowVersion),
      Qwerty.Codi.Application.Dispatch.ICodiAcceptedOutcome
{
    public static SessionControlAcceptedDto Create(SessionDto session, CommandAcceptedDto accepted) =>
        new(session.Id, session.LessonId, session.ClassroomId, session.Status, session.AdmissionsOpen,
            session.JoinCode, session.SessionRevision, session.StartedAtUtc, session.RowVersion,
            accepted.CommandId, accepted.SessionId, accepted.DeliverySummary, accepted.StatusQuery);
}
