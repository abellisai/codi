using Qwerty.Codi.Domain.Sessions;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Sessions;

/// <summary>
/// Archiviazione del tentativo per un azzeramento (§10.4, `ResetWork`): il lavoro resta nello storico
/// con il progetto che aveva, e un tentativo già archiviato non si archivia una seconda volta.
/// </summary>
public sealed class AttemptArchiveTests
{
    private static readonly Guid SessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ParticipantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AssignmentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid VersionId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Archivia_CambiaLoStatoEConservaIlProgetto()
    {
        var attempt = Attempt();

        attempt.Archive(Now.AddMinutes(5));

        Assert.Equal(AttemptStatus.Archived, attempt.Status);
        Assert.Equal("""{"blocks":[7]}""", attempt.WorkspaceJson);
        Assert.Equal(Now.AddMinutes(5), attempt.UpdatedAtUtc);
    }

    [Fact]
    public void ArchiviaDueVolte_Rifiuta()
    {
        var attempt = Attempt();
        attempt.Archive(Now);

        var failure = Assert.Throws<InvalidOperationException>(() => attempt.Archive(Now));

        Assert.Contains("già archiviato", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchiviaConIstanteNonUtc_Rifiuta()
    {
        var attempt = Attempt();
        var local = new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.FromHours(1));

        Assert.Throws<ArgumentException>(() => attempt.Archive(local));
        Assert.Equal(AttemptStatus.Ready, attempt.Status);
    }

    private static ExerciseAttempt Attempt() =>
        ExerciseAttempt.Create(
            SessionId,
            ParticipantId,
            AssignmentId,
            VersionId,
            attemptNumber: 1,
            """{"blocks":[7]}""",
            "impronta-del-progetto",
            Now);
}
