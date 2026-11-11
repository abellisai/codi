using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Exercises;
using Qwerty.Codi.Application.Lessons;
using Qwerty.Codi.Application.Organization;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Lessons;

/// <summary>
/// Sostituzione ordinata degli esercizi di una lezione (§10.4, §7.3): è l'unica richiesta che
/// riscrive righe correlate, e il suo comportamento osservabile è che la sequenza cambia mentre
/// gli identificativi delle righe che restano non cambiano. Il riuso della riga non è un dettaglio
/// interno: le assegnazioni delle sessioni di T4 terranno un riferimento proprio a quella riga.
/// </summary>
public sealed class ReplaceLessonExercisesTests : IAsyncLifetime
{
    private CodiUseCaseHost _host = null!;

    public async Task InitializeAsync() => _host = await CodiUseCaseHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConOrdineInvertito_MantieneGliIdentificativiDelleRigheRiusate()
    {
        // Arrange
        var lesson = await CreateLessonAsync();
        var first = (await CreateExerciseAsync("Primo")).Id;
        var second = (await CreateExerciseAsync("Secondo")).Id;
        var third = (await CreateExerciseAsync("Terzo")).Id;

        var initial = await ReplaceAsync(lesson, [first, second, third]);
        Assert.Equal(new[] { first, second, third }, initial.Select(row => row.ExerciseId));

        // Act: l'ordine diventa l'inverso.
        var reversed = await ReplaceAsync(lesson, [third, second, first]);

        // Assert: le posizioni sono definitive e le righe sono le stesse, ricollocate.
        Assert.Equal(new[] { 1, 2, 3 }, reversed.Select(row => row.Position));
        Assert.Equal(new[] { third, second, first }, reversed.Select(row => row.ExerciseId));
        Assert.Equal(
            initial.Select(row => row.Id).Reverse(),
            reversed.Select(row => row.Id));
        Assert.All(reversed, row => Assert.True(row.RowVersion > 1));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConSequenzaPiuCorta_RimuoveLeRigheInEccessoELiberaLePosizioni()
    {
        // Arrange
        var lesson = await CreateLessonAsync();
        var first = (await CreateExerciseAsync("Primo")).Id;
        var second = (await CreateExerciseAsync("Secondo")).Id;
        var third = (await CreateExerciseAsync("Terzo")).Id;
        await ReplaceAsync(lesson, [first, second, third]);

        // Act
        var shortened = await ReplaceAsync(lesson, [second]);

        // Assert
        var only = Assert.Single(shortened);
        Assert.Equal(1, only.Position);
        Assert.Equal(second, only.ExerciseId);
        Assert.Equal(1L, await CountRowsAsync(lesson.Id));

        // La posizione liberata è di nuovo utilizzabile.
        var grown = await ReplaceAsync(lesson, [second, third]);
        Assert.Equal(new[] { 1, 2 }, grown.Select(row => row.Position));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConSequenzaVuota_LasciaLaLezioneSenzaEserciziSenzaToccareLaLibreria()
    {
        // Arrange
        var lesson = await CreateLessonAsync();
        var exercise = (await CreateExerciseAsync("Primo")).Id;
        await ReplaceAsync(lesson, [exercise]);

        // Act
        var empty = await ReplaceAsync(lesson, []);

        // Assert: la libreria non è una figlia della lezione e resta intatta.
        Assert.Empty(empty);
        Assert.Equal(0L, await CountRowsAsync(lesson.Id));
        Assert.Single(await _host.QueryAsync<ExerciseDto>(ListExercisesQuery.RequestName, new { }));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConEsercizioInesistente_RestituisceRisorsaNonTrovataESenzaToccareLaSequenza()
    {
        // Arrange
        var lesson = await CreateLessonAsync();
        var exercise = (await CreateExerciseAsync("Primo")).Id;
        var before = await ReplaceAsync(lesson, [exercise]);

        // Act
        var exception = await _host.ExpectFailureAsync(
            ReplaceLessonExercisesCommand.RequestName,
            new
            {
                lessonId = lesson.Id,
                baseRowVersion = lesson.RowVersion,
                exercises = new[] { new { exerciseId = Guid.NewGuid() } },
            });

        // Assert: la verifica precede le scritture, quindi la lezione resta com'era.
        Assert.Equal(CodiErrorCodes.ResourceNotFound, exception.Code);
        var after = await ListRowsAsync(lesson.Id);
        Assert.Equal(before, after);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConEsercizioArchiviato_RifiutaConValidazioneSenzaScrivere()
    {
        // Arrange
        var lesson = await CreateLessonAsync();
        var exercise = await CreateExerciseAsync("Primo");
        var archived = await _host.CommandAsync<ExerciseDto>(
            ArchiveExerciseCommand.RequestName,
            new { id = exercise.Id, baseRowVersion = exercise.RowVersion });

        // Act
        var exception = await _host.ExpectFailureAsync(
            ReplaceLessonExercisesCommand.RequestName,
            new
            {
                lessonId = lesson.Id,
                baseRowVersion = lesson.RowVersion,
                exercises = new[] { new { exerciseId = archived.Id } },
            });

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, exception.Code);
        Assert.Equal(0L, await CountRowsAsync(lesson.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConUnaCasellaNonUtilizzabile_RifiutaConValidazioneSenzaAprireLaTransazione()
    {
        // Arrange
        var lesson = await CreateLessonAsync();
        var exercise = (await CreateExerciseAsync("Primo")).Id;

        // Act: una casella che non riferisce niente e una sequenza assente sono due richieste
        // diverse che il caso d'uso rifiuta prima di aprire la scrittura.
        var emptyExercise = await _host.ExpectFailureAsync(
            ReplaceLessonExercisesCommand.RequestName,
            new
            {
                lessonId = lesson.Id,
                baseRowVersion = lesson.RowVersion,
                exercises = new[] { new { exerciseId = Guid.Empty } },
            });

        var withoutSequence = await _host.ExpectFailureAsync(
            ReplaceLessonExercisesCommand.RequestName,
            new { lessonId = lesson.Id, baseRowVersion = lesson.RowVersion, exercises = (object?)null });

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, emptyExercise.Code);
        Assert.Equal(CodiErrorCodes.ValidationFailed, withoutSequence.Code);
        Assert.Equal(0L, await CountRowsAsync(lesson.Id));
        Assert.Equal(exercise, Assert.Single(await ListExercisesAsync()).Id);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SenzaVersioneOConVersioneObsoletaORisorsaAssente_RifiutaSenzaScrivere()
    {
        // Arrange
        var lesson = await CreateLessonAsync();
        var exercise = (await CreateExerciseAsync("Primo")).Id;
        var exercises = new[] { new { exerciseId = exercise } };

        // Act
        var withoutVersion = await _host.ExpectFailureAsync(
            ReplaceLessonExercisesCommand.RequestName,
            new { lessonId = lesson.Id, exercises });
        var stale = await _host.ExpectFailureAsync(
            ReplaceLessonExercisesCommand.RequestName,
            new { lessonId = lesson.Id, baseRowVersion = lesson.RowVersion + 1, exercises });
        var missingLesson = await _host.ExpectFailureAsync(
            ReplaceLessonExercisesCommand.RequestName,
            new { lessonId = Guid.NewGuid(), baseRowVersion = lesson.RowVersion, exercises });

        // Assert
        Assert.Equal(CodiErrorCodes.EntityVersionRequired, withoutVersion.Code);
        Assert.Equal(CodiErrorCodes.RowVersionMismatch, stale.Code);
        Assert.Equal(CodiErrorCodes.ResourceNotFound, missingLesson.Code);
        Assert.Equal(0L, await CountRowsAsync(lesson.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConLoStessoEsercizioDueVolte_MantieneDueCaselleDistinte()
    {
        // Arrange: il vincolo è sulla posizione, non sulla coppia: la stessa attività può comparire
        // due volte nella lezione, e sono due righe diverse.
        var lesson = await CreateLessonAsync();
        var exercise = (await CreateExerciseAsync("Primo")).Id;

        // Act
        var rows = await ReplaceAsync(lesson, [exercise, exercise]);

        // Assert
        Assert.Equal(new[] { 1, 2 }, rows.Select(row => row.Position));
        Assert.All(rows, row => Assert.Equal(exercise, row.ExerciseId));
        Assert.NotEqual(rows[0].Id, rows[1].Id);
    }

    private async Task<LessonDto> CreateLessonAsync()
    {
        var course = await _host.CommandAsync<CourseDto>(
            CreateCourseCommand.RequestName, new { title = "Informatica di base" });

        return await _host.CommandAsync<LessonDto>(
            CreateLessonCommand.RequestName, new { courseId = course.Id, title = "Prima lezione" });
    }

    private Task<ExerciseDto> CreateExerciseAsync(string title) =>
        _host.CommandAsync<ExerciseDto>(CreateExerciseCommand.RequestName, new { title, exerciseType = "GridPath" });

    private Task<IReadOnlyList<ExerciseDto>> ListExercisesAsync() =>
        _host.QueryAsync<ExerciseDto>(ListExercisesQuery.RequestName, new { });

    private Task<IReadOnlyList<LessonExerciseDto>> ReplaceAsync(LessonDto lesson, Guid[] exerciseIds) =>
        _host.ListCommandAsync<LessonExerciseDto>(
            ReplaceLessonExercisesCommand.RequestName,
            new
            {
                lessonId = lesson.Id,
                baseRowVersion = lesson.RowVersion,
                exercises = exerciseIds.Select(exerciseId => new { exerciseId }).ToArray(),
            });

    private Task<IReadOnlyList<LessonExerciseDto>> ListRowsAsync(Guid lessonId) =>
        _host.QueryAsync<LessonExerciseDto>(ListLessonExercisesQuery.RequestName, new { lessonId });

    private async Task<long> CountRowsAsync(Guid lessonId) => (await ListRowsAsync(lessonId)).Count;
}
