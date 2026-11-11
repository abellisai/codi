using Qwerty.Codi.Domain.Sessions;
using Xunit;
namespace Qwerty.Codi.Domain.Tests.Sessions;

public sealed class AttemptWorkspaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
    private static ExerciseAttempt Create() => ExerciseAttempt.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "{}", "hash", Now);
    [Fact]
    public void Salvataggio_AvanzaProgetto_EConservaIdentita()
    {
        var attempt = Create(); var id = attempt.Id; attempt.SaveWorkspace("{ }", "new-hash", 1, Now.AddSeconds(1));
        Assert.Equal(id, attempt.Id); Assert.Equal("{ }", attempt.WorkspaceJson); Assert.Equal(1, attempt.ProjectRevision); Assert.Equal(Now.AddSeconds(1), attempt.UpdatedAtUtc);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RevisioneNonAvanzata_Rifiuta(long revision)
    {
        Assert.Throws<ArgumentException>(() => Create().SaveWorkspace("{}", "hash", revision, Now));
    }
    [Fact]
    public void Archiviato_ConsenteSoloFinalizzazione()
    {
        var attempt = Create(); attempt.Archive(Now);
        Assert.Throws<InvalidOperationException>(() => attempt.SaveWorkspace("{}", "hash", 1, Now));
        attempt.SaveWorkspace("{}", "hash", 1, Now, true); Assert.Equal(AttemptStatus.Archived, attempt.Status);
    }
    [Fact]
    public void TempoNonUtc_Rifiuta()
    {
        Assert.Throws<ArgumentException>(() => Create().SaveWorkspace("{}", "hash", 1, Now.ToOffset(TimeSpan.FromHours(1))));
    }
}
