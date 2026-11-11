using Qwerty.Codi.Domain.Sessions;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Sessions;

/// <summary>
/// Cambiamento dello stato desiderato che non cambia il default di classe (§6.5). L'assegnazione
/// destina materiale a chi è già presente — la classe o un singolo alunno — e il default di classe
/// resta quello di partenza per chi entra dopo; la revisione avanza comunque, perché è cambiato lo
/// stato desiderato della sessione.
/// </summary>
public sealed class SessionDesiredStateChangeTests
{
    private static readonly Guid LessonId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClassroomId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AdminId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RegistraIlCambiamento_AvanzаLaRevisioneELasciaIlDefault()
    {
        var session = StartedSession();
        var revision = session.SessionRevision;

        session.RegisterDesiredStateChange(revision, Now);

        Assert.Equal(revision + 1, session.SessionRevision);
        Assert.Equal(LessonSession.WaitingClassJson, session.ClassDefaultJson);
        Assert.Equal(Now, session.UpdatedAtUtc);
    }

    [Fact]
    public void ConRevisioneDiversa_RifiutaIlCambiamento()
    {
        var session = StartedSession();
        var revision = session.SessionRevision;

        var failure = Assert.Throws<InvalidOperationException>(
            () => session.RegisterDesiredStateChange(revision - 1, Now));

        Assert.Contains("SessionRevisionConflict", failure.Message, StringComparison.Ordinal);
        Assert.Equal(revision, session.SessionRevision);
    }

    [Fact]
    public void SuSessioneTerminata_RifiutaIlCambiamento()
    {
        var session = StartedSession();
        session.End(session.SessionRevision, Now);

        var failure = Assert.Throws<InvalidOperationException>(
            () => session.RegisterDesiredStateChange(session.SessionRevision, Now));

        Assert.Contains("SessionStateConflict", failure.Message, StringComparison.Ordinal);
    }

    private static LessonSession StartedSession()
    {
        var session = LessonSession.Create(LessonId, ClassroomId, AdminId, Now);
        session.Start(session.SessionRevision, Now, SessionJoinCode.Restore("12345678"));

        return session;
    }
}
