using Qwerty.Codi.Domain.Sessions;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Sessions;

/// <summary>
/// Invarianti della sessione di lezione (§6.1, §6.4, §7.4): nasce in attesa, le transizioni di
/// regia sono ammesse solo dagli stati previsti e con revisione controllata — <c>Lobby → Active</c>,
/// <c>Active ⇄ Paused</c> e <c>* → Ended</c> da ogni stato non terminale — e la ricostruzione dai
/// dati persistiti rifiuta stati e istanti incoerenti. Il rifiuto previsto del comando è del caso
/// d'uso; qui si verifica che il dominio non lasci passare un uso scorretto.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LessonSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid LessonId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid ClassroomId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid AdminId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>Codice di ingresso dei casi: la generazione casuale si verifica a parte.</summary>
    private static readonly SessionJoinCode SampleCode = SessionJoinCode.Restore("12345678");

    [Fact]
    public void Create_ProduceLobbyConIngressiChiusiERevisioneUno()
    {
        var session = LessonSession.Create(LessonId, ClassroomId, AdminId, Now);

        Assert.NotEqual(Guid.Empty, session.Id);
        Assert.Equal(LessonId, session.LessonId);
        Assert.Equal(ClassroomId, session.ClassroomId);
        Assert.Equal(AdminId, session.ControllerAdminId);
        Assert.Equal(SessionStatus.Lobby, session.Status);
        Assert.False(session.AdmissionsOpen);
        Assert.Equal(1, session.SessionRevision);
        Assert.Equal(LessonSession.WaitingClassJson, session.ClassDefaultJson);
        Assert.Null(session.StartedAtUtc);
        Assert.Null(session.EndedAtUtc);
        Assert.Equal(Now, session.CreatedAtUtc);
        Assert.Equal(Now, session.UpdatedAtUtc);
        Assert.Equal(1, session.RowVersion);
    }

    [Fact]
    public void Create_ConIstanteNonUtc_Rifiuta()
    {
        var local = new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.FromHours(2));

        var exception = Assert.Throws<ArgumentException>(
            () => LessonSession.Create(LessonId, ClassroomId, AdminId, local));

        Assert.Equal("nowUtc", exception.ParamName);
    }

    [Fact]
    public void Start_DaLobby_PortaInActive_ApreGliIngressiERegistraAvvio()
    {
        var session = LessonSession.Create(LessonId, ClassroomId, AdminId, Now);
        var startedAt = Now.AddMinutes(5);

        session.Start(expectedSessionRevision: 1, startedAt, SampleCode);

        Assert.Equal(SessionStatus.Active, session.Status);
        Assert.True(session.AdmissionsOpen);
        Assert.Equal(2, session.SessionRevision);
        Assert.Equal(startedAt, session.StartedAtUtc);
        Assert.Null(session.EndedAtUtc);
        Assert.Equal(startedAt, session.UpdatedAtUtc);
        Assert.Equal(SampleCode, session.JoinCode);
    }

    [Fact]
    public void Start_ConCodiceVuoto_RifiutaEInvariaLoStato()
    {
        var session = LessonSession.Create(LessonId, ClassroomId, AdminId, Now);

        var exception = Assert.Throws<ArgumentException>(
            () => session.Start(expectedSessionRevision: 1, Now.AddMinutes(5), default));

        Assert.Equal("joinCode", exception.ParamName);
        Assert.Equal(SessionStatus.Lobby, session.Status);
        Assert.Null(session.JoinCode);
    }

    [Fact]
    public void PauseEResume_ConservanIlCodiceDiIngresso()
    {
        var session = LessonSession.Create(LessonId, ClassroomId, AdminId, Now);
        session.Start(expectedSessionRevision: 1, Now.AddMinutes(1), SampleCode);

        session.Pause(expectedSessionRevision: 2, Now.AddMinutes(2));
        Assert.Equal(SampleCode, session.JoinCode);

        session.Resume(expectedSessionRevision: 3, Now.AddMinutes(3));
        Assert.Equal(SampleCode, session.JoinCode);
    }

    [Fact]
    public void End_ConservaIlCodiceMaChiudeGliIngressi()
    {
        var session = LessonSession.Create(LessonId, ClassroomId, AdminId, Now);
        session.Start(expectedSessionRevision: 1, Now.AddMinutes(1), SampleCode);

        session.End(expectedSessionRevision: 2, Now.AddMinutes(10));

        Assert.Equal(SampleCode, session.JoinCode);
        Assert.False(session.AdmissionsOpen);
        Assert.Equal(SessionStatus.Ended, session.Status);
    }

    [Fact]
    public void Start_ConRevisioneAttesaDiversa_RifiutaEInvariaLoStato()
    {
        var session = LessonSession.Create(LessonId, ClassroomId, AdminId, Now);

        var exception = Assert.Throws<InvalidOperationException>(
            () => session.Start(expectedSessionRevision: 2, Now.AddMinutes(5), SampleCode));

        Assert.Contains("SessionRevisionConflict", exception.Message, StringComparison.Ordinal);
        Assert.Equal(SessionStatus.Lobby, session.Status);
        Assert.False(session.AdmissionsOpen);
        Assert.Equal(1, session.SessionRevision);
        Assert.Null(session.StartedAtUtc);
        Assert.Equal(Now, session.UpdatedAtUtc);
    }

    [Fact]
    public void Start_DaStatoDiversoDaLobby_RifiutaEInvariaLoStato()
    {
        var active = Restore(SessionStatus.Active, startedAt: Now, sessionRevision: 2);

        var exception = Assert.Throws<InvalidOperationException>(
            () => active.Start(expectedSessionRevision: 2, Now.AddMinutes(5), SampleCode));

        Assert.Contains("SessionStateConflict", exception.Message, StringComparison.Ordinal);
        Assert.Equal(SessionStatus.Active, active.Status);
        Assert.Equal(2, active.SessionRevision);
        Assert.Equal(Now, active.StartedAtUtc);
    }

    [Fact]
    public void Start_ConIstanteNonUtc_Rifiuta()
    {
        var session = LessonSession.Create(LessonId, ClassroomId, AdminId, Now);
        var local = new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.FromHours(2));

        var exception = Assert.Throws<ArgumentException>(
            () => session.Start(expectedSessionRevision: 1, local, SampleCode));

        Assert.Equal("nowUtc", exception.ParamName);
        Assert.Equal(SessionStatus.Lobby, session.Status);
    }

    [Fact]
    public void Restore_ConDatiCoerenti_RicostruisceLaSessione()
    {
        var id = Guid.NewGuid();
        var startedAt = Now.AddMinutes(2);
        var endedAt = Now.AddMinutes(30);

        var session = LessonSession.Restore(
            id, LessonId, ClassroomId, AdminId, SessionStatus.Ended, admissionsOpen: false,
            sessionRevision: 3, LessonSession.WaitingClassJson, SampleCode,
            startedAt, endedAt, Now, endedAt, rowVersion: 4);

        Assert.Equal(id, session.Id);
        Assert.Equal(SessionStatus.Ended, session.Status);
        Assert.Equal(3, session.SessionRevision);
        Assert.Equal(startedAt, session.StartedAtUtc);
        Assert.Equal(endedAt, session.EndedAtUtc);
        Assert.Equal(4, session.RowVersion);
        Assert.Equal(SampleCode, session.JoinCode);
    }

    [Fact]
    public void Restore_ConCodiceSuSessioneInAttesa_RifiutaLaRicostruzione()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => LessonSession.Restore(
                Guid.NewGuid(), LessonId, ClassroomId, AdminId, SessionStatus.Lobby, admissionsOpen: false,
                sessionRevision: 1, LessonSession.WaitingClassJson, SampleCode,
                null, null, Now, Now, rowVersion: 1));

        Assert.Equal("joinCode", exception.ParamName);
    }

    [Fact]
    public void Restore_ConLobbyEAvvioPresente_RifiutaLaRicostruzione()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Restore(SessionStatus.Lobby, startedAt: Now));

        Assert.Equal("startedAtUtc", exception.ParamName);
    }

    [Theory]
    [InlineData(SessionStatus.Active)]
    [InlineData(SessionStatus.Paused)]
    public void Restore_ConSessioneAvviataSenzaAvvio_RifiutaLaRicostruzione(SessionStatus status)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Restore(status, startedAt: null, sessionRevision: 2));

        Assert.Equal("startedAtUtc", exception.ParamName);
    }

    [Fact]
    public void Restore_ConSessioneTerminataSenzaTermine_RifiutaLaRicostruzione()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Restore(SessionStatus.Ended, startedAt: Now, endedAt: null, sessionRevision: 2));

        Assert.Equal("endedAtUtc", exception.ParamName);
    }

    [Fact]
    public void Restore_ConRevisioneNonPositiva_RifiutaLaRicostruzione()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Restore(SessionStatus.Lobby, sessionRevision: 0));

        Assert.Equal("sessionRevision", exception.ParamName);
    }

    [Fact]
    public void Restore_ConIdentificativoVuoto_RifiutaLaRicostruzione()
    {
        var exception = Assert.Throws<ArgumentException>(() => LessonSession.Restore(
            Guid.Empty, LessonId, ClassroomId, AdminId, SessionStatus.Lobby, admissionsOpen: false,
            sessionRevision: 1, LessonSession.WaitingClassJson, null, null, null, Now, Now, rowVersion: 1));

        Assert.Equal("id", exception.ParamName);
    }

    [Fact]
    public void Restore_ConVersioneDiRigaNonPositiva_RifiutaLaRicostruzione()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Restore(SessionStatus.Lobby, rowVersion: 0));

        Assert.Equal("rowVersion", exception.ParamName);
    }

    [Fact]
    public void Pause_DaActive_PortaInPaused_IncrementaLaRevisioneEMantieneGliIngressi()
    {
        var session = ActiveSession();
        var pausedAt = Now.AddMinutes(5);

        session.Pause(expectedSessionRevision: 2, pausedAt);

        Assert.Equal(SessionStatus.Paused, session.Status);
        Assert.True(session.AdmissionsOpen);
        Assert.Equal(3, session.SessionRevision);
        Assert.Equal(Now.AddMinutes(1), session.StartedAtUtc);
        Assert.Null(session.EndedAtUtc);
        Assert.Equal(pausedAt, session.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(SessionStatus.Lobby)]
    [InlineData(SessionStatus.Ended)]
    public void Pause_DaStatoNonActive_RifiutaEInvariaLoStato(SessionStatus status)
    {
        var session = status == SessionStatus.Lobby ? Restore(SessionStatus.Lobby) : EndedSession();
        var revisionBefore = session.SessionRevision;
        var updatedBefore = session.UpdatedAtUtc;

        var exception = Assert.Throws<InvalidOperationException>(
            () => session.Pause(revisionBefore, Now.AddMinutes(5)));

        Assert.Contains("SessionStateConflict", exception.Message, StringComparison.Ordinal);
        Assert.Equal(status, session.Status);
        Assert.Equal(revisionBefore, session.SessionRevision);
        Assert.Equal(updatedBefore, session.UpdatedAtUtc);
    }

    [Fact]
    public void Resume_DaPaused_PortaInActive_IncrementaLaRevisioneEMantieneGliIngressi()
    {
        var session = Restore(
            SessionStatus.Paused, startedAt: Now.AddMinutes(1), sessionRevision: 3, admissionsOpen: true);
        var resumedAt = Now.AddMinutes(6);

        session.Resume(expectedSessionRevision: 3, resumedAt);

        Assert.Equal(SessionStatus.Active, session.Status);
        Assert.True(session.AdmissionsOpen);
        Assert.Equal(4, session.SessionRevision);
        Assert.Equal(Now.AddMinutes(1), session.StartedAtUtc);
        Assert.Null(session.EndedAtUtc);
        Assert.Equal(resumedAt, session.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(SessionStatus.Lobby)]
    [InlineData(SessionStatus.Active)]
    [InlineData(SessionStatus.Ended)]
    public void Resume_DaStatoNonPaused_RifiutaEInvariaLoStato(SessionStatus status)
    {
        var session = status switch
        {
            SessionStatus.Lobby => Restore(SessionStatus.Lobby),
            SessionStatus.Active => ActiveSession(),
            _ => EndedSession(),
        };
        var revisionBefore = session.SessionRevision;

        var exception = Assert.Throws<InvalidOperationException>(
            () => session.Resume(revisionBefore, Now.AddMinutes(5)));

        Assert.Contains("SessionStateConflict", exception.Message, StringComparison.Ordinal);
        Assert.Equal(status, session.Status);
        Assert.Equal(revisionBefore, session.SessionRevision);
    }

    [Theory]
    [InlineData(SessionStatus.Lobby)]
    [InlineData(SessionStatus.Active)]
    [InlineData(SessionStatus.Paused)]
    public void End_DaStatoNonTerminale_PortaInEnded_ChiudeGliIngressiERegistraIlTermine(SessionStatus status)
    {
        var session = status switch
        {
            SessionStatus.Lobby => Restore(SessionStatus.Lobby),
            SessionStatus.Active => ActiveSession(),
            _ => Restore(SessionStatus.Paused, startedAt: Now.AddMinutes(1), sessionRevision: 3, admissionsOpen: true),
        };
        var revisionBefore = session.SessionRevision;
        var startedBefore = session.StartedAtUtc;
        var endedAt = Now.AddMinutes(10);

        session.End(revisionBefore, endedAt);

        Assert.Equal(SessionStatus.Ended, session.Status);
        Assert.False(session.AdmissionsOpen);
        Assert.Equal(revisionBefore + 1, session.SessionRevision);
        Assert.Equal(startedBefore, session.StartedAtUtc);
        Assert.Equal(endedAt, session.EndedAtUtc);
        Assert.Equal(endedAt, session.UpdatedAtUtc);
    }

    [Fact]
    public void End_SuSessioneTerminata_RifiutaEInvariaLoStato()
    {
        var session = EndedSession();
        var revisionBefore = session.SessionRevision;
        var endedBefore = session.EndedAtUtc;

        var exception = Assert.Throws<InvalidOperationException>(
            () => session.End(revisionBefore, Now.AddMinutes(20)));

        Assert.Contains("SessionStateConflict", exception.Message, StringComparison.Ordinal);
        Assert.Equal(SessionStatus.Ended, session.Status);
        Assert.Equal(revisionBefore, session.SessionRevision);
        Assert.Equal(endedBefore, session.EndedAtUtc);
    }

    [Fact]
    public void End_DopoIlTermine_NessunaTransizioneEAmmessa()
    {
        var session = ActiveSession();
        session.End(expectedSessionRevision: 2, Now.AddMinutes(10));
        var revision = session.SessionRevision;

        Assert.Equal(SessionStatus.Ended, session.Status);
        Assert.Throws<InvalidOperationException>(() => session.Start(revision, Now.AddMinutes(11), SampleCode));
        Assert.Throws<InvalidOperationException>(() => session.Pause(revision, Now.AddMinutes(11)));
        Assert.Throws<InvalidOperationException>(() => session.Resume(revision, Now.AddMinutes(11)));
        Assert.Throws<InvalidOperationException>(() => session.End(revision, Now.AddMinutes(11)));

        Assert.Equal(SessionStatus.Ended, session.Status);
        Assert.Equal(revision, session.SessionRevision);
        Assert.False(session.AdmissionsOpen);
        Assert.Equal(Now.AddMinutes(10), session.EndedAtUtc);
    }

    [Theory]
    [InlineData(RegiaTransition.Pause)]
    [InlineData(RegiaTransition.Resume)]
    [InlineData(RegiaTransition.End)]
    public void Transizione_ConRevisioneAttesaErrata_RifiutaEInvariaLoStato(RegiaTransition transition)
    {
        var session = ActiveSession();

        var exception = Assert.Throws<InvalidOperationException>(
            () => InvokeTransition(session, transition, expectedSessionRevision: 4, Now.AddMinutes(5)));

        Assert.Contains("SessionRevisionConflict", exception.Message, StringComparison.Ordinal);
        Assert.Equal(SessionStatus.Active, session.Status);
        Assert.Equal(2, session.SessionRevision);
        Assert.Equal(Now.AddMinutes(1), session.StartedAtUtc);
        Assert.Null(session.EndedAtUtc);
    }

    [Theory]
    [InlineData(RegiaTransition.Pause)]
    [InlineData(RegiaTransition.Resume)]
    [InlineData(RegiaTransition.End)]
    public void Transizione_ConIstanteNonUtc_Rifiuta(RegiaTransition transition)
    {
        var session = ActiveSession();
        var local = new DateTimeOffset(2026, 9, 30, 11, 0, 0, TimeSpan.FromHours(2));

        var exception = Assert.Throws<ArgumentException>(
            () => InvokeTransition(session, transition, expectedSessionRevision: 2, local));

        Assert.Equal("nowUtc", exception.ParamName);
        Assert.Equal(SessionStatus.Active, session.Status);
        Assert.Equal(2, session.SessionRevision);
    }

    /// <summary>Transizioni di regia coperte con lo stesso scenario negativo.</summary>
    public enum RegiaTransition
    {
        Pause,
        Resume,
        End,
    }

    private static void InvokeTransition(
        LessonSession session, RegiaTransition transition, long expectedSessionRevision, DateTimeOffset nowUtc)
    {
        switch (transition)
        {
            case RegiaTransition.Pause:
                session.Pause(expectedSessionRevision, nowUtc);
                break;
            case RegiaTransition.Resume:
                session.Resume(expectedSessionRevision, nowUtc);
                break;
            default:
                session.End(expectedSessionRevision, nowUtc);
                break;
        }
    }

    /// <summary>Sessione in play: avviata, ingressi aperti, revisione due.</summary>
    private static LessonSession ActiveSession() =>
        Restore(SessionStatus.Active, startedAt: Now.AddMinutes(1), sessionRevision: 2, admissionsOpen: true);

    /// <summary>Sessione terminata: avviata, ingressi chiusi, termine registrato, revisione tre.</summary>
    private static LessonSession EndedSession() =>
        Restore(SessionStatus.Ended, startedAt: Now.AddMinutes(1), endedAt: Now.AddMinutes(2), sessionRevision: 3);

    /// <summary>
    /// Ricostruzione con dati coerenti per impostazione predefinita: i casi negativi variano un
    /// solo campo, così il rifiuto osservato è attribuibile a quel campo. Il codice di ingresso è
    /// quello dei casi per una sessione avviata e assente per una sessione in attesa, che è lo stato
    /// coerente; le sessioni avviate prima dell'introduzione del codice restano ammesse con null.
    /// </summary>
    private static LessonSession Restore(
        SessionStatus status,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null,
        long sessionRevision = 1,
        long rowVersion = 1,
        bool admissionsOpen = false,
        SessionJoinCode? joinCode = null) =>
        LessonSession.Restore(
            Guid.NewGuid(), LessonId, ClassroomId, AdminId, status, admissionsOpen,
            sessionRevision, LessonSession.WaitingClassJson,
            joinCode ?? (status == SessionStatus.Lobby ? null : SampleCode),
            startedAt, endedAt, Now, Now, rowVersion);
}
