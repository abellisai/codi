using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Lessons;
using Qwerty.Codi.Application.Organization;
using Qwerty.Codi.Domain.Lessons;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Lessons;

/// <summary>
/// Casi d'uso della lezione (§10.4, §10.5) attraverso il percorso reale: registro, canale,
/// policy, deserializzazione, mediator, handler, repository e database migrato.
/// </summary>
public sealed class LessonUseCaseTests : IAsyncLifetime
{
    private CodiUseCaseHost _host = null!;

    public async Task InitializeAsync() => _host = await CodiUseCaseHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_CreateLesson_CreaLaLezioneNelCorsoECompareNellElenco()
    {
        // Arrange
        var course = await CreateCourseAsync("Informatica di base");

        // Act
        var lesson = await _host.CommandAsync<LessonDto>(
            CreateLessonCommand.RequestName,
            new { courseId = course.Id, title = "  Prima lezione  ", description = "Le regole" });

        // Assert
        Assert.Equal(course.Id, lesson.CourseId);
        Assert.Equal("Prima lezione", lesson.Title);
        Assert.Equal("Le regole", lesson.Description);
        Assert.False(lesson.IsArchived);
        Assert.Equal(1, lesson.RowVersion);
        Assert.Equal(lesson, Assert.Single(await ListLessonsAsync(course.Id)));
        Assert.Equal(lesson, await GetLessonAsync(lesson.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_CreateLessonConCorsoInesistente_RestituisceRisorsaNonTrovata()
    {
        // Act
        var exception = await _host.ExpectFailureAsync(
            CreateLessonCommand.RequestName,
            new { courseId = Guid.NewGuid(), title = "Prima lezione" });

        // Assert: la chiave esterna non è il modo in cui si risponde a un identificativo sbagliato.
        Assert.Equal(CodiErrorCodes.ResourceNotFound, exception.Code);
        Assert.Empty(await _host.QueryAsync<LessonDto>(ListLessonsQuery.RequestName, new { courseId = Guid.Empty }));
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_CreateLessonConTitoloNonUtilizzabile_RifiutaConIlCodiceDelNome(string title)
    {
        // Arrange
        var course = await CreateCourseAsync("Informatica di base");

        // Act
        var exception = await _host.ExpectFailureAsync(
            CreateLessonCommand.RequestName, new { courseId = course.Id, title });

        // Assert
        Assert.Equal(CodiErrorCodes.InvalidName, exception.Code);
        Assert.Empty(await ListLessonsAsync(course.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UpdateLessonEArchiveLesson_SeguonoLaVersioneDiRiga()
    {
        // Arrange
        var course = await CreateCourseAsync("Informatica di base");
        var lesson = await CreateLessonAsync(course.Id, "Prima lezione");

        // Act: modifica riuscita, poi modifica con versione obsoleta.
        var updated = await _host.CommandAsync<LessonDto>(
            UpdateLessonCommand.RequestName,
            new { id = lesson.Id, title = "Seconda lezione", description = "Altra", baseRowVersion = lesson.RowVersion });

        var stale = await _host.ExpectFailureAsync(
            UpdateLessonCommand.RequestName,
            new { id = lesson.Id, title = "Terza lezione", baseRowVersion = lesson.RowVersion });

        // Assert
        Assert.Equal("Seconda lezione", updated.Title);
        Assert.Equal(2, updated.RowVersion);
        Assert.Equal(CodiErrorCodes.RowVersionMismatch, stale.Code);

        // Act: versione mancante, archiviazione, archiviazione ripetuta.
        var withoutVersion = await _host.ExpectFailureAsync(
            ArchiveLessonCommand.RequestName, new { id = lesson.Id });

        var archived = await _host.CommandAsync<LessonDto>(
            ArchiveLessonCommand.RequestName, new { id = lesson.Id, baseRowVersion = updated.RowVersion });

        var archivedAgain = await _host.CommandAsync<LessonDto>(
            ArchiveLessonCommand.RequestName, new { id = lesson.Id, baseRowVersion = archived.RowVersion });

        // Assert
        Assert.Equal(CodiErrorCodes.EntityVersionRequired, withoutVersion.Code);
        Assert.True(archived.IsArchived);
        Assert.Equal(3, archived.RowVersion);
        Assert.Equal(archived, archivedAgain);
        Assert.Empty(await ListLessonsAsync(course.Id));
        Assert.Single(await ListLessonsAsync(course.Id, includeArchived: true));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UpdateLessonConModificaIdentica_NonAlzaLaVersione()
    {
        // Arrange
        var course = await CreateCourseAsync("Informatica di base");
        var lesson = await CreateLessonAsync(course.Id, "Prima lezione");

        // Act
        var unchanged = await _host.CommandAsync<LessonDto>(
            UpdateLessonCommand.RequestName,
            new { id = lesson.Id, title = "  Prima lezione  ", baseRowVersion = lesson.RowVersion });

        // Assert
        Assert.Equal(1, unchanged.RowVersion);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ListLessons_FiltraPerCorsoEOrdinaPerTitoloSenzaDistinguereLeMaiuscole()
    {
        // Arrange
        var firstCourse = await CreateCourseAsync("Informatica di base");
        var secondCourse = await CreateCourseAsync("Matematica");
        await CreateLessonAsync(firstCourse.Id, "zebra");
        await CreateLessonAsync(firstCourse.Id, "Ancora");
        await CreateLessonAsync(secondCourse.Id, "Altra lezione");

        // Act
        var lessons = await ListLessonsAsync(firstCourse.Id);
        var unknown = await ListLessonsAsync(Guid.NewGuid());

        // Assert
        Assert.Equal(new[] { "Ancora", "zebra" }, lessons.Select(lesson => lesson.Title));
        Assert.Empty(unknown);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UpdateEGetLessonSuIdentificativoInesistente_RestituisconoRisorsaNonTrovata()
    {
        // Act
        var update = await _host.ExpectFailureAsync(
            UpdateLessonCommand.RequestName,
            new { id = Guid.NewGuid(), title = "Prima lezione", baseRowVersion = 1L });

        var read = await _host.ExpectQueryFailureAsync(GetLessonQuery.RequestName, new { id = Guid.NewGuid() });

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, update.Code);
        Assert.Equal(CodiErrorCodes.ResourceNotFound, read.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_CreateUpdateEArchiveLessonConDatiNonValidi_RifiutanoConIlCodiceAtteso()
    {
        // Arrange
        var course = await CreateCourseAsync("Informatica di base");
        var lesson = await CreateLessonAsync(course.Id, "Prima lezione");
        var tooLongDescription = new string('a', Lesson.MaxDescriptionLength + 1);

        // Act
        var withoutCourse = await _host.ExpectFailureAsync(
            CreateLessonCommand.RequestName, new { courseId = Guid.Empty, title = "Prima lezione" });
        var longDescription = await _host.ExpectFailureAsync(
            CreateLessonCommand.RequestName,
            new { courseId = course.Id, title = "Altra lezione", description = tooLongDescription });
        var invalidTitle = await _host.ExpectFailureAsync(
            UpdateLessonCommand.RequestName,
            new { id = lesson.Id, title = "  ", baseRowVersion = lesson.RowVersion });
        var validationOnUpdate = await _host.ExpectFailureAsync(
            UpdateLessonCommand.RequestName,
            new { id = lesson.Id, title = "Altra", description = tooLongDescription, baseRowVersion = lesson.RowVersion });
        var staleArchive = await _host.ExpectFailureAsync(
            ArchiveLessonCommand.RequestName, new { id = lesson.Id, baseRowVersion = 2L });
        var missingArchive = await _host.ExpectFailureAsync(
            ArchiveLessonCommand.RequestName, new { id = Guid.NewGuid(), baseRowVersion = 1L });

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, withoutCourse.Code);
        Assert.Equal(CodiErrorCodes.ValidationFailed, longDescription.Code);
        Assert.Equal(CodiErrorCodes.InvalidName, invalidTitle.Code);
        Assert.Equal(CodiErrorCodes.ValidationFailed, validationOnUpdate.Code);
        Assert.Equal(CodiErrorCodes.RowVersionMismatch, staleArchive.Code);
        Assert.Equal(CodiErrorCodes.ResourceNotFound, missingArchive.Code);
        Assert.Single(await ListLessonsAsync(course.Id));
    }

    private Task<CourseDto> CreateCourseAsync(string title) =>
        _host.CommandAsync<CourseDto>(CreateCourseCommand.RequestName, new { title });

    private Task<LessonDto> CreateLessonAsync(Guid courseId, string title, string? description = null) =>
        _host.CommandAsync<LessonDto>(CreateLessonCommand.RequestName, new { courseId, title, description });

    private Task<IReadOnlyList<LessonDto>> ListLessonsAsync(Guid courseId, bool includeArchived = false) =>
        _host.QueryAsync<LessonDto>(ListLessonsQuery.RequestName, new { courseId, includeArchived });

    private Task<LessonDto> GetLessonAsync(Guid id) =>
        _host.QueryOneAsync<LessonDto>(GetLessonQuery.RequestName, new { id });
}
