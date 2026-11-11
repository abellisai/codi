using Qwerty.Codi.Domain.Lessons;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Lessons;

/// <summary>
/// Invarianti dell'esercizio di lezione (§7.3): posizione diversa da zero, riferimento a un
/// esercizio esistente per costruzione e cursore di modifica aggiornato quando la riga si sposta.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LessonExerciseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid LessonId = Guid.Parse("1b2c3d4e-5f60-4a1b-8c2d-3e4f5a6b7c8d");

    private static readonly Guid ExerciseId = Guid.Parse("2c3d4e5f-6071-4b2c-9d3e-4f5a6b7c8d9e");

    [Fact]
    public void Create_ConPosizioneDefinitiva_CreaLaRigaAllaPosizioneIndicata()
    {
        var row = LessonExercise.Create(LessonId, ExerciseId, 3, Now);

        Assert.NotEqual(Guid.Empty, row.Id);
        Assert.Equal(LessonId, row.LessonId);
        Assert.Equal(ExerciseId, row.ExerciseId);
        Assert.Equal(3, row.Position);
        Assert.False(row.HasTemporaryPosition);
        Assert.Equal(Now, row.CreatedAtUtc);
        Assert.Equal(Now, row.UpdatedAtUtc);
        Assert.Equal(1, row.RowVersion);
    }

    [Fact]
    public void Create_ConPosizioneTemporanea_LaRiconosceComeTemporanea()
    {
        var row = LessonExercise.Create(LessonId, ExerciseId, -2, Now);

        Assert.Equal(-2, row.Position);
        Assert.True(row.HasTemporaryPosition);
    }

    [Fact]
    public void Create_ConPosizioneZero_Rifiuta()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => LessonExercise.Create(LessonId, ExerciseId, 0, Now));

        Assert.Equal("position", exception.ParamName);
    }

    [Fact]
    public void Create_SenzaLezioneOSenzaEsercizio_Rifiuta()
    {
        var withoutLesson = Assert.Throws<ArgumentException>(
            () => LessonExercise.Create(Guid.Empty, ExerciseId, 1, Now));
        var withoutExercise = Assert.Throws<ArgumentException>(
            () => LessonExercise.Create(LessonId, Guid.Empty, 1, Now));

        Assert.Equal("lessonId", withoutLesson.ParamName);
        Assert.Equal("exerciseId", withoutExercise.ParamName);
    }

    [Fact]
    public void Create_ConIstanteNonUtc_Rifiuta()
    {
        var local = new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.FromHours(2));

        var exception = Assert.Throws<ArgumentException>(
            () => LessonExercise.Create(LessonId, ExerciseId, 1, local));

        Assert.Equal("nowUtc", exception.ParamName);
    }

    [Fact]
    public void PlaceAt_CambiaPosizioneEEsercizioEAggiornaLaModifica()
    {
        var row = LessonExercise.Create(LessonId, ExerciseId, -1, Now);
        var moved = Now.AddMinutes(5);
        var otherExercise = Guid.NewGuid();

        row.PlaceAt(2, otherExercise, moved);

        Assert.Equal(2, row.Position);
        Assert.Equal(otherExercise, row.ExerciseId);
        Assert.False(row.HasTemporaryPosition);
        Assert.Equal(moved, row.UpdatedAtUtc);
        Assert.Equal(Now, row.CreatedAtUtc);
    }

    [Fact]
    public void PlaceAt_ConDatiNonValidi_RifiutaSenzaModificareLaRiga()
    {
        var row = LessonExercise.Create(LessonId, ExerciseId, 1, Now);

        Assert.Throws<ArgumentException>(() => row.PlaceAt(0, Guid.NewGuid(), Now.AddMinutes(1)));
        Assert.Throws<ArgumentException>(() => row.PlaceAt(2, Guid.Empty, Now.AddMinutes(1)));

        // La riga è rimasta quella di prima: nessuna delle due chiamate ha scritto.
        Assert.Equal(1, row.Position);
        Assert.Equal(ExerciseId, row.ExerciseId);
        Assert.Equal(Now, row.UpdatedAtUtc);
    }

    [Fact]
    public void Restore_ConDatiNonRappresentabili_RifiutaLaRicostruzione()
    {
        var withoutId = Assert.Throws<ArgumentException>(() => LessonExercise.Restore(
            Guid.Empty, LessonId, ExerciseId, 1, Now, Now, rowVersion: 1));
        var withoutLesson = Assert.Throws<ArgumentException>(() => LessonExercise.Restore(
            Guid.NewGuid(), Guid.Empty, ExerciseId, 1, Now, Now, rowVersion: 1));
        var withoutExercise = Assert.Throws<ArgumentException>(() => LessonExercise.Restore(
            Guid.NewGuid(), LessonId, Guid.Empty, 1, Now, Now, rowVersion: 1));
        var zeroPosition = Assert.Throws<ArgumentException>(() => LessonExercise.Restore(
            Guid.NewGuid(), LessonId, ExerciseId, 0, Now, Now, rowVersion: 1));
        var negativeVersion = Assert.Throws<ArgumentException>(() => LessonExercise.Restore(
            Guid.NewGuid(), LessonId, ExerciseId, 1, Now, Now, rowVersion: 0));

        Assert.Equal("id", withoutId.ParamName);
        Assert.Equal("lessonId", withoutLesson.ParamName);
        Assert.Equal("exerciseId", withoutExercise.ParamName);
        Assert.Equal("position", zeroPosition.ParamName);
        Assert.Equal("rowVersion", negativeVersion.ParamName);
    }

    [Fact]
    public void Restore_ConModificaPrecedenteAllaCreazione_RifiutaLaRicostruzione()
    {
        var exception = Assert.Throws<ArgumentException>(() => LessonExercise.Restore(
            Guid.NewGuid(), LessonId, ExerciseId, 1, Now, Now.AddMinutes(-1), rowVersion: 1));

        Assert.Equal("updatedAtUtc", exception.ParamName);
    }

    [Fact]
    public void WithRowVersion_ConVersioneNonPositiva_Rifiuta()
    {
        var row = LessonExercise.Create(LessonId, ExerciseId, 1, Now);

        var exception = Assert.Throws<ArgumentException>(() => row.WithRowVersion(0));

        Assert.Equal("rowVersion", exception.ParamName);
    }
}
