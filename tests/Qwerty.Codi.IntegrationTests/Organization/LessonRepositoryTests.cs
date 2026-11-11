using Microsoft.Data.Sqlite;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Lessons;
using Qwerty.Codi.Domain.Organization;
using Qwerty.Codi.Infrastructure.Migrations;
using Qwerty.Codi.Infrastructure.Persistence;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Organization;

/// <summary>
/// Persistenza della lezione (§7.3, §20.3): andata e ritorno dei campi, filtro per corso,
/// controllo di versione e chiave esterna verso il corso.
/// </summary>
public sealed class LessonRepositoryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private TempSqliteDatabase _database = null!;

    private IUnitOfWork _work = null!;

    private ICourseRepository _courses = null!;

    private ILessonRepository _lessons = null!;

    public async Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();

        var runner = new MigrationRunner(_database.Factory, new TestTimeProvider());
        await runner.ApplyAsync(
            MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly),
            CancellationToken.None);

        _work = await _database.Units.CreateAsync();
        _courses = _work.Repo<ICourseRepository>();
        _lessons = _work.Repo<ILessonRepository>();
    }

    public async Task DisposeAsync()
    {
        await _work.DisposeAsync();
        _database.Dispose();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InsertAsync_ConLezioneNuova_SiRileggeUguale()
    {
        // Arrange
        var course = await CreateCourseAsync("Informatica di base");
        var lesson = Lesson.Create(course.Id, "  Prima lezione  ", "Le regole", Now);

        // Act
        Assert.Equal(lesson.Id.ToString("D"), await _lessons.InsertAsync(
            LessonEntity.FromDomain(lesson), CancellationToken.None));

        // Assert
        var read = await ReadLessonAsync(lesson.Id);

        Assert.NotNull(read);
        Assert.Equal(course.Id, read.CourseId);
        Assert.Equal("Prima lezione", read.Title);
        Assert.Equal("Le regole", read.Description);
        Assert.False(read.IsArchived);
        Assert.Equal(Now, read.CreatedAtUtc);
        Assert.Equal(1, read.RowVersion);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task QueryAsync_PerCorso_RestituisceSoloLeLezioniDelCorso()
    {
        // Arrange
        var firstCourse = await CreateCourseAsync("Informatica di base");
        var secondCourse = await CreateCourseAsync("Matematica");

        await _lessons.InsertAsync(
            LessonEntity.FromDomain(Lesson.Create(firstCourse.Id, "Prima lezione", null, Now)),
            CancellationToken.None);
        await _lessons.InsertAsync(
            LessonEntity.FromDomain(Lesson.Create(firstCourse.Id, "Seconda lezione", null, Now)),
            CancellationToken.None);
        await _lessons.InsertAsync(
            LessonEntity.FromDomain(Lesson.Create(secondCourse.Id, "Altra lezione", null, Now)),
            CancellationToken.None);

        // Act
        var rows = await _lessons.QueryAsync(
            entity => entity.CourseId == firstCourse.Id.ToString("D"), CancellationToken.None);

        // Assert
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(firstCourse.Id.ToString("D"), row.CourseId));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InsertAsync_ConCorsoInesistente_ViolaLaChiaveEsternaSenzaDiventareUnConflitto()
    {
        // Arrange: una lezione non può esistere senza il suo corso, e la chiave esterna violata
        // deve restare un errore visibile invece di trasformarsi in un esito previsto.
        var lesson = Lesson.Create(Guid.NewGuid(), "Prima lezione", null, Now);

        // Act
        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            _lessons.InsertAsync(LessonEntity.FromDomain(lesson), CancellationToken.None));

        // Assert
        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.Equal(787, exception.SqliteExtendedErrorCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateAsync_ConVersioneObsoleta_RestituisceFalsoSenzaSovrascrivere()
    {
        // Arrange: due copie della stessa riga, come due richieste concorrenti.
        var course = await CreateCourseAsync("Informatica di base");
        var lesson = Lesson.Create(course.Id, "Prima lezione", null, Now);
        await _lessons.InsertAsync(LessonEntity.FromDomain(lesson), CancellationToken.None);

        var first = await ReadLessonAsync(lesson.Id);
        var second = await ReadLessonAsync(lesson.Id);

        first!.Archive(Now.AddMinutes(1));
        Assert.True(await _lessons.UpdateAsync(LessonEntity.FromDomain(first), CancellationToken.None));

        // Act
        second!.Update("Seconda lezione", null, Now.AddMinutes(2));
        var written = await _lessons.UpdateAsync(LessonEntity.FromDomain(second), CancellationToken.None);

        // Assert
        Assert.False(written);

        var reloaded = await ReadLessonAsync(lesson.Id);
        Assert.Equal("Prima lezione", reloaded!.Title);
        Assert.True(reloaded.IsArchived);
        Assert.Equal(2, reloaded.RowVersion);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateAsync_ConModificaNuova_IncrementaLaVersioneEMantieneLaCreazione()
    {
        // Arrange
        var course = await CreateCourseAsync("Informatica di base");
        var lesson = Lesson.Create(course.Id, "Prima lezione", null, Now);
        await _lessons.InsertAsync(LessonEntity.FromDomain(lesson), CancellationToken.None);

        var stored = await ReadLessonAsync(lesson.Id);
        stored!.Update("Seconda lezione", "Con descrizione", Now.AddMinutes(5));

        // Act
        Assert.True(await _lessons.UpdateAsync(LessonEntity.FromDomain(stored), CancellationToken.None));

        // Assert
        var reloaded = await ReadLessonAsync(lesson.Id);
        Assert.Equal("Seconda lezione", reloaded!.Title);
        Assert.Equal("Con descrizione", reloaded.Description);
        Assert.Equal(2, reloaded.RowVersion);
        Assert.Equal(Now, reloaded.CreatedAtUtc);
        Assert.Equal(course.Id, reloaded.CourseId);
    }

    private async Task<Course> CreateCourseAsync(string title)
    {
        var course = Course.Create(title, null, Now);
        await _courses.InsertAsync(CourseEntity.FromDomain(course), CancellationToken.None);

        return course;
    }

    private async Task<Lesson?> ReadLessonAsync(Guid id) =>
        (await _lessons.GetAsync(id.ToString("D"), CancellationToken.None))?.ToDomain();
}
