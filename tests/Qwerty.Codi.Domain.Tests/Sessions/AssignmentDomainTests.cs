using Qwerty.Codi.Domain.Sessions;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Sessions;

/// <summary>
/// Assegnazione dell'esercizio e tentativo dell'alunno (§6.5, §7.3). Le regole verificate qui sono
/// quelle che il database ripete con i suoi vincoli: l'ambito decide se il destinatario esiste, la
/// versione è quella fissata e un tentativo nasce pronto e bloccato con il proprio progetto.
/// </summary>
public sealed class AssignmentDomainTests
{
    private static readonly Guid SessionId = Guid.Parse("a970f347-5052-46ad-b8c5-2390ec5bd986");
    private static readonly Guid ParticipantId = Guid.Parse("ce760e35-cd4d-4a38-932a-a37c3bb91978");
    private static readonly Guid OtherParticipantId = Guid.Parse("0d1f9d1e-6c17-4c02-8e6f-3a9a1c2b7f10");
    private static readonly Guid VersionId = Guid.Parse("d62cf6ab-3caa-41ab-b2f1-3bc82e9fc92a");
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Unit")]
    public void Assegnazione_DiClasse_RaggiungeTuttiGliAmmessi()
    {
        var assignment = ActivityAssignment.Create(
            SessionId,
            lessonExerciseId: null,
            AssignmentScope.Class,
            targetParticipantId: null,
            VersionId,
            Now);

        Assert.Equal(AssignmentScope.Class, assignment.Scope);
        Assert.Null(assignment.TargetParticipantId);
        Assert.Equal(VersionId, assignment.ExerciseVersionId);
        Assert.True(assignment.Reaches(ParticipantId));
        Assert.True(assignment.Reaches(Guid.NewGuid()));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Assegnazione_Individuale_RaggiungeIlSoloDestinatario()
    {
        var assignment = ActivityAssignment.Create(
            SessionId,
            lessonExerciseId: null,
            AssignmentScope.Participant,
            ParticipantId,
            VersionId,
            Now);

        Assert.True(assignment.Reaches(ParticipantId));
        Assert.False(assignment.Reaches(Guid.NewGuid()));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Assegnazione_IndividualeSenzaDestinatario_Rifiuta()
    {
        Assert.Throws<ArgumentException>(() => ActivityAssignment.Create(
            SessionId,
            lessonExerciseId: null,
            AssignmentScope.Participant,
            targetParticipantId: null,
            VersionId,
            Now));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Assegnazione_DiClasseConDestinatario_Rifiuta()
    {
        // Un'assegnazione di classe con un destinatario sarebbe ambigua: non si saprebbe chi raggiunge.
        Assert.Throws<ArgumentException>(() => ActivityAssignment.Create(
            SessionId,
            lessonExerciseId: null,
            AssignmentScope.Class,
            ParticipantId,
            VersionId,
            Now));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Assegnazione_SenzaVersione_Rifiuta()
    {
        Assert.Throws<ArgumentException>(() => ActivityAssignment.Create(
            SessionId,
            lessonExerciseId: null,
            AssignmentScope.Class,
            targetParticipantId: null,
            Guid.Empty,
            Now));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Tentativo_NasceProntoEBloccatoConIlProprioProgetto()
    {
        var attempt = NewAttempt();

        Assert.Equal(AttemptStatus.Ready, attempt.Status);
        Assert.Equal(1L, attempt.AttemptNumber);
        Assert.Equal(0L, attempt.ProjectRevision);
        Assert.Equal(VersionId, attempt.ExerciseVersionId);
        Assert.Equal("""{"blocks":[]}""", attempt.WorkspaceJson);
        Assert.Equal("impronta-1", attempt.WorkspaceHash);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Tentativo_ConNumeroNonPositivo_Rifiuta()
    {
        Assert.Throws<ArgumentException>(() => ExerciseAttempt.Create(
            SessionId,
            ParticipantId,
            Guid.NewGuid(),
            VersionId,
            attemptNumber: 0,
            """{"blocks":[]}""",
            "impronta-1",
            Now));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Tentativo_SenzaProgettoOSenzaImpronta_Rifiuta()
    {
        Assert.Throws<ArgumentException>(() => ExerciseAttempt.Create(
            SessionId,
            ParticipantId,
            Guid.NewGuid(),
            VersionId,
            attemptNumber: 1,
            " ",
            "impronta-1",
            Now));

        Assert.Throws<ArgumentException>(() => ExerciseAttempt.Create(
            SessionId,
            ParticipantId,
            Guid.NewGuid(),
            VersionId,
            attemptNumber: 1,
            """{"blocks":[]}""",
            " ",
            Now));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Tentativo_DiUnAltroIstanteNonUtc_Rifiuta()
    {
        Assert.Throws<ArgumentException>(() => ExerciseAttempt.Create(
            SessionId,
            ParticipantId,
            Guid.NewGuid(),
            VersionId,
            attemptNumber: 1,
            """{"blocks":[]}""",
            "impronta-1",
            new DateTimeOffset(2026, 10, 2, 11, 0, 0, TimeSpan.FromHours(2))));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Tentativo_Riletto_ConservaProgettoRevisioneEVersione()
    {
        var created = NewAttempt();

        var restored = ExerciseAttempt.Restore(
            created.Id,
            created.SessionId,
            created.ParticipantId,
            created.AssignmentId,
            created.ExerciseVersionId,
            created.AttemptNumber,
            AttemptStatus.Paused,
            """{"blocks":[{"type":"TURN"}]}""",
            "impronta-2",
            projectRevision: 7,
            created.CreatedAtUtc,
            created.UpdatedAtUtc,
            rowVersion: 3);

        Assert.Equal(AttemptStatus.Paused, restored.Status);
        Assert.Equal(7L, restored.ProjectRevision);
        Assert.Equal(3L, restored.RowVersion);
        Assert.Equal("impronta-2", restored.WorkspaceHash);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Tentativo_RilettoConStatoNonRiconosciuto_Rifiuta()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExerciseAttempt.Restore(
            Guid.NewGuid(),
            SessionId,
            ParticipantId,
            Guid.NewGuid(),
            VersionId,
            1,
            (AttemptStatus)99,
            """{"blocks":[]}""",
            "impronta-1",
            0,
            Now,
            Now,
            1));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Assegnazione_SenzaSessione_Rifiuta()
    {
        Assert.Throws<ArgumentException>(() => ActivityAssignment.Create(
            Guid.Empty,
            lessonExerciseId: null,
            AssignmentScope.Class,
            targetParticipantId: null,
            VersionId,
            Now));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Assegnazione_RilettaConservaCasellaEAmbito()
    {
        var lessonExerciseId = Guid.NewGuid();

        var restored = ActivityAssignment.Restore(
            Guid.NewGuid(),
            SessionId,
            lessonExerciseId,
            AssignmentScope.Participant,
            ParticipantId,
            VersionId,
            Now);

        Assert.Equal(lessonExerciseId, restored.LessonExerciseId);
        Assert.True(restored.Reaches(ParticipantId));
        Assert.False(restored.Reaches(OtherParticipantId));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Assegnazione_RilettaConAmbitoNonRiconosciuto_Rifiuta()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ActivityAssignment.Restore(
            Guid.NewGuid(),
            SessionId,
            lessonExerciseId: null,
            (AssignmentScope)99,
            targetParticipantId: null,
            VersionId,
            Now));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Assegnazione_RilettaSenzaIdentificativo_Rifiuta()
    {
        Assert.Throws<ArgumentException>(() => ActivityAssignment.Restore(
            Guid.Empty,
            SessionId,
            lessonExerciseId: null,
            AssignmentScope.Class,
            targetParticipantId: null,
            VersionId,
            Now));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Assegnazione_ConIstanteNonUtc_Rifiuta()
    {
        Assert.Throws<ArgumentException>(() => ActivityAssignment.Create(
            SessionId,
            lessonExerciseId: null,
            AssignmentScope.Class,
            targetParticipantId: null,
            VersionId,
            new DateTimeOffset(2026, 10, 2, 11, 0, 0, TimeSpan.FromHours(2))));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Tentativo_SenzaAssegnazioneOSenzaPartecipante_Rifiuta()
    {
        Assert.Throws<ArgumentException>(() => ExerciseAttempt.Create(
            SessionId,
            Guid.Empty,
            Guid.NewGuid(),
            VersionId,
            attemptNumber: 1,
            """{"blocks":[]}""",
            "impronta-1",
            Now));

        Assert.Throws<ArgumentException>(() => ExerciseAttempt.Create(
            SessionId,
            ParticipantId,
            Guid.Empty,
            VersionId,
            attemptNumber: 1,
            """{"blocks":[]}""",
            "impronta-1",
            Now));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Tentativo_RilettoConDatiIncoerenti_Rifiuta()
    {
        var id = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => ExerciseAttempt.Restore(
            id, SessionId, ParticipantId, Guid.NewGuid(), VersionId, 1, AttemptStatus.Ready,
            """{"blocks":[]}""", "impronta-1", projectRevision: -1, Now, Now, rowVersion: 1));

        Assert.Throws<ArgumentException>(() => ExerciseAttempt.Restore(
            id, SessionId, ParticipantId, Guid.NewGuid(), VersionId, 1, AttemptStatus.Ready,
            """{"blocks":[]}""", "impronta-1", projectRevision: 0, Now, Now, rowVersion: 0));

        Assert.Throws<ArgumentException>(() => ExerciseAttempt.Restore(
            id, SessionId, ParticipantId, Guid.NewGuid(), VersionId, 1, AttemptStatus.Ready,
            """{"blocks":[]}""", "impronta-1", projectRevision: 0, Now, Now.AddMinutes(-1), rowVersion: 1));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Tentativo_ConVersioneDiRigaNuova_CopiaSenzaPerdereIlProgetto()
    {
        var created = NewAttempt();

        var versioned = created.WithRowVersion(4);

        Assert.Equal(4L, versioned.RowVersion);
        Assert.Equal(created.Id, versioned.Id);
        Assert.Equal(created.WorkspaceHash, versioned.WorkspaceHash);
        Assert.Throws<ArgumentException>(() => created.WithRowVersion(0));
    }

    private static ExerciseAttempt NewAttempt() => ExerciseAttempt.Create(
        SessionId,
        ParticipantId,
        Guid.NewGuid(),
        VersionId,
        attemptNumber: 1,
        """{"blocks":[]}""",
        "impronta-1",
        Now);
}
