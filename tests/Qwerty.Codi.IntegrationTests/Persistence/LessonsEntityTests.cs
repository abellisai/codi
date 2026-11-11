using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Exercises;
using Qwerty.Codi.Domain.Lessons;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Persistence;

/// <summary>
/// Conversione fra dominio ed entità di persistenza per lezioni, esercizi e caselle della sequenza
/// (§7.2, §7.3): identità conservata e righe fuori formato rifiutate in lettura.
/// </summary>
public sealed class LessonsEntityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Unit")]
    public void Mapping_DiUnaLezione_PreservaIdentificativoTitoloEDate()
    {
        var courseId = Guid.NewGuid();
        var lesson = Lesson.Create(courseId, "Prima lezione", "Descrizione", Now);

        var row = LessonEntity.FromDomain(lesson);
        var restored = row.ToDomain();

        Assert.Equal(lesson.Id, restored.Id);
        Assert.Equal(courseId, restored.CourseId);
        Assert.Equal("Prima lezione", restored.Title);
        Assert.Equal("Descrizione", restored.Description);
        Assert.Equal(Now.ToUnixTimeMilliseconds(), row.CreatedAtUtcMs);
        Assert.False(restored.IsArchived);
        Assert.Equal(1, restored.RowVersion);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Mapping_DiUnEsercizio_PreservaTitoloEDescrizioneVuota()
    {
        // La colonna della descrizione non è annullabile: «nessuna descrizione» è una stringa
        // vuota, non un dato mancante.
        var row = ExerciseEntity.FromDomain(Exercise.Create("GridPath", "Ciclo con i blocchi", null, Now));
        var restored = row.ToDomain();

        Assert.Equal(string.Empty, row.Description);
        Assert.Equal("Ciclo con i blocchi", restored.Title);
        Assert.Equal(string.Empty, restored.Description);
        Assert.False(restored.IsArchived);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Mapping_DiUnaCasellaDellaSequenza_PreservaPosizioneEEsercizio()
    {
        var lessonId = Guid.NewGuid();
        var exerciseId = Guid.NewGuid();

        var row = LessonExerciseEntity.FromDomain(LessonExercise.Create(lessonId, exerciseId, 2, Now));
        var restored = row.ToDomain();

        Assert.Equal(lessonId, restored.LessonId);
        Assert.Equal(exerciseId, restored.ExerciseId);
        Assert.Equal(2, restored.Position);
        Assert.Equal(Now.ToUnixTimeMilliseconds(), row.CreatedAtUtcMs);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToDomain_ConFlagNonBooleani_RifiutaLaRiga()
    {
        // Un flag diverso da 0 e 1 non è uno stato: leggerlo come «falso» riaprirebbe in
        // silenzio una lezione archiviata o un esercizio archiviato.
        var lesson = LessonEntity.FromDomain(
            Lesson.Create(Guid.NewGuid(), "Prima lezione", null, Now));
        lesson.IsArchivedFlag = 2;

        var exercise = ExerciseEntity.FromDomain(Exercise.Create("GridPath", "Ciclo con i blocchi", null, Now));
        exercise.IsArchivedFlag = 2;

        Assert.Equal(
            nameof(LessonEntity.IsArchivedFlag),
            Assert.Throws<ArgumentException>(() => lesson.ToDomain()).ParamName);
        Assert.Equal(
            nameof(ExerciseEntity.IsArchivedFlag),
            Assert.Throws<ArgumentException>(() => exercise.ToDomain()).ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToDomain_ConPosizioneFuoriIntervallo_RifiutaLaRiga()
    {
        // La posizione sta in un intero: un valore oltre l'intervallo, scritto da uno strumento
        // esterno, non è una posizione e non deve entrare nella sequenza.
        var row = LessonExerciseEntity.FromDomain(
            LessonExercise.Create(Guid.NewGuid(), Guid.NewGuid(), 1, Now));
        row.Position = (long)int.MaxValue + 1;

        Assert.Equal(
            nameof(LessonExerciseEntity.Position),
            Assert.Throws<ArgumentException>(() => row.ToDomain()).ParamName);
    }
}
