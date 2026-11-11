using Microsoft.Data.Sqlite;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Organization;
using Qwerty.Codi.Infrastructure.Migrations;
using Qwerty.Codi.Infrastructure.Persistence;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Organization;

/// <summary>
/// Repository di classi, corsi e associazioni su un database temporaneo migrato (§7.2, §20.3):
/// unicità della coppia nella scuola, controllo di versione, chiavi esterne e persistenza dei
/// flag di stato.
/// </summary>
public sealed class OrganizationRepositoryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private TempSqliteDatabase _database = null!;

    private IUnitOfWork _work = null!;

    private ISchoolRepository _schools = null!;

    private IClassroomRepository _classrooms = null!;

    private ICourseRepository _courses = null!;

    public async Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();

        var runner = new MigrationRunner(_database.Factory, new TestTimeProvider());
        await runner.ApplyAsync(
            MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly),
            CancellationToken.None);

        _work = await _database.Units.CreateAsync();
        _schools = _work.Repo<ISchoolRepository>();
        _classrooms = _work.Repo<IClassroomRepository>();
        _courses = _work.Repo<ICourseRepository>();
    }

    public async Task DisposeAsync()
    {
        await _work.DisposeAsync();
        _database.Dispose();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TryInsertAsync_ConClasseNuova_SiRileggeUguale()
    {
        // Arrange
        var school = await CreateSchoolAsync("Scuola Manzoni");
        var classroom = Classroom.Create(school.Id, "2ª A", "2026-2027", Now);

        // Act
        Assert.True(await _classrooms.TryInsertAsync(classroom, CancellationToken.None));

        // Assert
        var read = await ReadClassroomAsync(classroom.Id);

        Assert.NotNull(read);
        Assert.Equal(school.Id, read.SchoolId);
        Assert.Equal("2ª A", read.Name);
        Assert.Equal("2ª a", read.NormalizedName);
        Assert.Equal("2026/2027", read.SchoolYear);
        Assert.False(read.IsArchived);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TryInsertAsync_ConStessoNomeNellaStessaScuolaEAnno_RifiutaSenzaDuplicare()
    {
        // Arrange
        var school = await CreateSchoolAsync("Scuola Manzoni");
        var first = Classroom.Create(school.Id, "2ª A", "2026/2027", Now);
        Assert.True(await _classrooms.TryInsertAsync(first, CancellationToken.None));

        // Act: stessa coppia normalizzata, scrittura diversa.
        var second = Classroom.Create(school.Id, "  2ª  A ", "2026-2027", Now);
        var inserted = await _classrooms.TryInsertAsync(second, CancellationToken.None);

        // Assert
        Assert.False(inserted);
        Assert.Single(await _classrooms.QueryAsync(entity => entity.SchoolId == school.Id.ToString("D"), CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TryInsertAsync_ConStessoNomeInUnAltraScuolaOUnAltroAnno_Accetta()
    {
        // Arrange: l'unicità è per scuola e anno, non sul nome in assoluto.
        var first = await CreateSchoolAsync("Scuola Manzoni");
        var second = await CreateSchoolAsync("Scuola Dante");

        // Act / Assert
        Assert.True(await _classrooms.TryInsertAsync(
            Classroom.Create(first.Id, "2ª A", "2026/2027", Now), CancellationToken.None));
        Assert.True(await _classrooms.TryInsertAsync(
            Classroom.Create(second.Id, "2ª A", "2026/2027", Now), CancellationToken.None));
        Assert.True(await _classrooms.TryInsertAsync(
            Classroom.Create(first.Id, "2ª A", "2027/2028", Now), CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TryInsertAsync_ConScuolaInesistente_ViolaLaChiaveEsternaSenzaDiventareUnConflitto()
    {
        // Arrange: una chiave esterna violata non è «nome già usato»; è un errore che deve
        // restare visibile, altrimenti il caso d'uso non potrebbe distinguere i due esiti.
        var classroom = Classroom.Create(Guid.NewGuid(), "2ª A", "2026/2027", Now);

        // Act
        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            _classrooms.TryInsertAsync(classroom, CancellationToken.None));

        // Assert
        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.Equal(787, exception.SqliteExtendedErrorCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateAsync_ConVersioneObsoleta_RestituisceFalsoSenzaSovrascrivere()
    {
        // Arrange: due copie della stessa riga, come due richieste concorrenti.
        var school = await CreateSchoolAsync("Scuola Manzoni");
        var classroom = Classroom.Create(school.Id, "2ª A", "2026/2027", Now);
        Assert.True(await _classrooms.TryInsertAsync(classroom, CancellationToken.None));

        var first = await ReadClassroomAsync(classroom.Id);
        var second = await ReadClassroomAsync(classroom.Id);

        first!.Archive(Now.AddMinutes(1));
        var firstRow = ClassroomEntity.FromDomain(first);
        Assert.True(await _classrooms.UpdateAsync(firstRow, CancellationToken.None));

        // Act
        second!.Update("2ª B", "2026/2027", Now.AddMinutes(2));
        var secondRow = ClassroomEntity.FromDomain(second);
        var written = await _classrooms.UpdateAsync(secondRow, CancellationToken.None);

        // Assert
        Assert.False(written);

        var reloaded = await ReadClassroomAsync(classroom.Id);
        Assert.Equal("2ª A", reloaded!.Name);
        Assert.True(reloaded.IsArchived);
        Assert.Equal(2, reloaded.RowVersion);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateAsync_DiUnCorso_PersisteTitoloDescrizioneEStato()
    {
        // Arrange
        var course = Course.Create("Informatica di base", null, Now);
        await _courses.InsertAsync(CourseEntity.FromDomain(course), CancellationToken.None);

        var stored = await ReadCourseAsync(course.Id);
        stored!.Update("Informatica avanzata", "Con esercizi", Now.AddMinutes(5));

        // Act
        var row = CourseEntity.FromDomain(stored);
        Assert.True(await _courses.UpdateAsync(row, CancellationToken.None));

        // Assert
        var reloaded = await ReadCourseAsync(course.Id);
        Assert.Equal("Informatica avanzata", reloaded!.Title);
        Assert.Equal("Con esercizi", reloaded.Description);
        Assert.Equal(2, reloaded.RowVersion);
        Assert.Equal(Now, reloaded.CreatedAtUtc);
    }

    private async Task<School> CreateSchoolAsync(string name)
    {
        var school = School.Create(name, Now);
        Assert.True(await _schools.TryInsertAsync(school, CancellationToken.None));

        return school;
    }

    private async Task<School?> ReadSchoolAsync(Guid id) =>
        (await _schools.GetAsync(id.ToString("D"), CancellationToken.None))?.ToDomain();

    private async Task<Classroom?> ReadClassroomAsync(Guid id) =>
        (await _classrooms.GetAsync(id.ToString("D"), CancellationToken.None))?.ToDomain();

    private async Task<Course?> ReadCourseAsync(Guid id) =>
        (await _courses.GetAsync(id.ToString("D"), CancellationToken.None))?.ToDomain();
}
