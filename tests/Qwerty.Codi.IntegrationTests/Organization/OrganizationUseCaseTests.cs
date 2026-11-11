using Qwerty.Codi.Application.Dto;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Organization;
using Qwerty.Codi.Infrastructure.Migrations;
using Qwerty.Codi.Infrastructure.Persistence;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Organization;

/// <summary>
/// Casi d'uso di classi, corsi e associazioni (§10.4, §10.5) attraverso il percorso reale:
/// registro, canale, policy, deserializzazione, mediator, handler, repository e database migrato.
/// </summary>
public sealed class OrganizationUseCaseTests : IAsyncLifetime
{
    private const string SchoolYear = "2026/2027";

    private TempSqliteDatabase _database = null!;

    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();

        var runner = new MigrationRunner(_database.Factory, new TestTimeProvider());
        await runner.ApplyAsync(
            MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly),
            CancellationToken.None);

        var services = new ServiceCollection();
        services.AddCodiRequests();
        services.AddSingleton(_database.Factory);
        services.AddCodiPersistence();
        services.AddSingleton<TimeProvider>(new TestTimeProvider());

        _services = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        _database.Dispose();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_CreateClassroom_CreaLaClasseNellaScuola()
    {
        // Arrange
        var executor = CreateExecutor();
        var school = await CreateSchoolAsync(executor, "Scuola Manzoni");

        // Act
        var created = await CreateClassroomAsync(executor, school.Id, "  2ª A ", "2026-2027");

        // Assert
        Assert.Equal(school.Id, created.SchoolId);
        Assert.Equal("2ª A", created.Name);
        Assert.Equal(SchoolYear, created.SchoolYear);
        Assert.Equal(1, created.RowVersion);

        var classrooms = await ListClassroomsAsync(executor, school.Id);
        Assert.Equal(created, Assert.Single(classrooms));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_CreateClassroomConNomeEAnnoGiaUsati_RifiutaSenzaDuplicare()
    {
        // Arrange
        var executor = CreateExecutor();
        var school = await CreateSchoolAsync(executor, "Scuola Manzoni");
        await CreateClassroomAsync(executor, school.Id, "2ª A", SchoolYear);

        using var payload = Payload(new { schoolId = school.Id, name = "  2ª  A ", schoolYear = "2026-2027" });

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                CreateClassroomCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                AdminContext(),
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.ClassroomAlreadyExists, exception.Code);
        Assert.Single(await ListClassroomsAsync(executor, school.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_CreateClassroomInUnaScuolaInesistente_RestituisceRisorsaNonTrovata()
    {
        // Arrange
        var executor = CreateExecutor();
        using var payload = Payload(new { schoolId = Guid.NewGuid(), name = "2ª A", schoolYear = SchoolYear });

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                CreateClassroomCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                AdminContext(),
                CancellationToken.None)
            .AsTask());

        // Assert: la chiave esterna non è il modo in cui si risponde a un identificativo sbagliato.
        Assert.Equal(CodiErrorCodes.ResourceNotFound, exception.Code);
    }

    [Theory]
    [InlineData("2026", CodiErrorCodes.ValidationFailed)]
    [InlineData("2026/2028", CodiErrorCodes.ValidationFailed)]
    [InlineData("   ", CodiErrorCodes.InvalidName)]
    public async Task ExecuteAsync_CreateClassroomConDatiNonValidi_RifiutaConIlCodiceAtteso(
        string written, string expectedCode)
    {
        // Arrange
        var executor = CreateExecutor();
        var school = await CreateSchoolAsync(executor, "Scuola Manzoni");
        var isYear = written.Contains('/') || written == "2026";
        using var payload = Payload(new
        {
            schoolId = school.Id,
            name = isYear ? "2ª A" : written,
            schoolYear = isYear ? written : SchoolYear,
        });

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                CreateClassroomCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                AdminContext(),
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(expectedCode, exception.Code);
        Assert.Empty(await ListClassroomsAsync(executor, school.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ListClassrooms_OrdinaPerAnnoDecrescenteEPoiPerNome()
    {
        // Arrange
        var executor = CreateExecutor();
        var school = await CreateSchoolAsync(executor, "Scuola Manzoni");
        await CreateClassroomAsync(executor, school.Id, "2ª B", "2025/2026");
        await CreateClassroomAsync(executor, school.Id, "2ª B", "2026/2027");
        await CreateClassroomAsync(executor, school.Id, "1ª A", "2026/2027");

        // Act
        var classrooms = await ListClassroomsAsync(executor, school.Id);

        // Assert: le classi dell'anno più recente vengono prima, in ordine di nome.
        Assert.Equal(
            new[] { "1ª A/2026/2027", "2ª B/2026/2027", "2ª B/2025/2026" },
            classrooms.Select(classroom => $"{classroom.Name}/{classroom.SchoolYear}"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UpdateClassroom_CambiaNomeEAnnoIncrementandoLaVersione()
    {
        // Arrange
        var executor = CreateExecutor();
        var school = await CreateSchoolAsync(executor, "Scuola Manzoni");
        var created = await CreateClassroomAsync(executor, school.Id, "2ª A", SchoolYear);

        // Act
        var updated = await UpdateClassroomAsync(executor, created, "2ª B", "2027/2028");

        // Assert
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("2ª B", updated.Name);
        Assert.Equal("2027/2028", updated.SchoolYear);
        Assert.Equal(2, updated.RowVersion);
        Assert.Equal(updated, await GetClassroomAsync(executor, created.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UpdateClassroomSenzaVersioneOConVersioneObsoleta_Rifiuta()
    {
        // Arrange
        var executor = CreateExecutor();
        var school = await CreateSchoolAsync(executor, "Scuola Manzoni");
        var created = await CreateClassroomAsync(executor, school.Id, "2ª A", SchoolYear);

        using var withoutVersion = Payload(new { id = created.Id, name = "2ª B", schoolYear = SchoolYear });

        // Act
        var missing = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                UpdateClassroomCommand.RequestName,
                CodiRequestChannel.Http,
                withoutVersion.RootElement,
                AdminContext(),
                CancellationToken.None)
            .AsTask());

        await UpdateClassroomAsync(executor, created, "2ª B", SchoolYear);

        using var stale = Payload(new
        {
            id = created.Id,
            name = "2ª C",
            schoolYear = SchoolYear,
            baseRowVersion = created.RowVersion,
        });
        var obsolete = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                UpdateClassroomCommand.RequestName,
                CodiRequestChannel.Http,
                stale.RootElement,
                AdminContext(),
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.EntityVersionRequired, missing.Code);
        Assert.Equal(CodiErrorCodes.RowVersionMismatch, obsolete.Code);
        Assert.Equal("2ª B", (await GetClassroomAsync(executor, created.Id)).Name);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UpdateClassroomVersoUnaCoppiaGiaUsata_RifiutaSenzaModificare()
    {
        // Arrange
        var executor = CreateExecutor();
        var school = await CreateSchoolAsync(executor, "Scuola Manzoni");
        await CreateClassroomAsync(executor, school.Id, "2ª A", SchoolYear);
        var second = await CreateClassroomAsync(executor, school.Id, "2ª B", SchoolYear);

        using var payload = Payload(new
        {
            id = second.Id,
            name = "2ª A",
            schoolYear = SchoolYear,
            baseRowVersion = second.RowVersion,
        });

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                UpdateClassroomCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                AdminContext(),
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.ClassroomAlreadyExists, exception.Code);
        Assert.Equal("2ª B", (await GetClassroomAsync(executor, second.Id)).Name);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ArchiveClassroom_ToglieLaClasseDallElencoPredefinito()
    {
        // Arrange
        var executor = CreateExecutor();
        var school = await CreateSchoolAsync(executor, "Scuola Manzoni");
        var created = await CreateClassroomAsync(executor, school.Id, "2ª A", SchoolYear);
        using var payload = Payload(new { id = created.Id, baseRowVersion = created.RowVersion });

        // Act
        var archived = Assert.IsType<ClassroomDto>(await executor.ExecuteAsync(
            CodiRequestKind.Command,
            ArchiveClassroomCommand.RequestName,
            CodiRequestChannel.Http,
            payload.RootElement,
            AdminContext(),
            CancellationToken.None));

        // Assert
        Assert.True(archived.IsArchived);
        Assert.Equal(2, archived.RowVersion);
        Assert.Empty(await ListClassroomsAsync(executor, school.Id));
        Assert.True((await GetClassroomAsync(executor, created.Id)).IsArchived);
        Assert.Single(await ListClassroomsAsync(executor, school.Id, includeArchived: true));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SuUnaClasseInesistente_RestituisceRisorsaNonTrovata()
    {
        // Arrange
        var executor = CreateExecutor();
        using var payload = Payload(new { id = Guid.NewGuid(), baseRowVersion = 1 });

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                ArchiveClassroomCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                AdminContext(),
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, exception.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_CreateCourse_AccettaLaDescrizioneAssenteEOrdinaPerTitolo()
    {
        // Arrange
        var executor = CreateExecutor();

        // Act
        var withoutDescription = await CreateCourseAsync(executor, "Informatica di base");
        var withDescription = await CreateCourseAsync(executor, "Atelier di robotica", "Con i sensori");

        // Assert
        Assert.Equal(string.Empty, withoutDescription.Description);
        Assert.Equal("Con i sensori", withDescription.Description);
        Assert.Equal(
            new[] { "Atelier di robotica", "Informatica di base" },
            (await ListCoursesAsync(executor)).Select(course => course.Title));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UpdateCourseEArchiveCourse_SeguonoLaVersioneDiRiga()
    {
        // Arrange
        var executor = CreateExecutor();
        var course = await CreateCourseAsync(executor, "Informatica di base");

        using var update = Payload(new
        {
            id = course.Id,
            title = "Informatica avanzata",
            description = "Con esercizi",
            baseRowVersion = course.RowVersion,
        });

        // Act
        var updated = Assert.IsType<CourseDto>(await executor.ExecuteAsync(
            CodiRequestKind.Command,
            UpdateCourseCommand.RequestName,
            CodiRequestChannel.Http,
            update.RootElement,
            AdminContext(),
            CancellationToken.None));

        using var archive = Payload(new { id = course.Id, baseRowVersion = updated.RowVersion });
        var archived = Assert.IsType<CourseDto>(await executor.ExecuteAsync(
            CodiRequestKind.Command,
            ArchiveCourseCommand.RequestName,
            CodiRequestChannel.Http,
            archive.RootElement,
            AdminContext(),
            CancellationToken.None));

        // Assert
        Assert.Equal("Informatica avanzata", updated.Title);
        Assert.Equal("Con esercizi", updated.Description);
        Assert.Equal(2, updated.RowVersion);
        Assert.True(archived.IsArchived);
        Assert.Equal(3, archived.RowVersion);
        Assert.Empty(await ListCoursesAsync(executor));
        Assert.Single(await ListCoursesAsync(executor, includeArchived: true));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UpdateClassroomConDatiNonValidi_RifiutaConIlCodiceAtteso()
    {
        // Arrange
        var executor = CreateExecutor();
        var school = await CreateSchoolAsync(executor, "Scuola Manzoni");
        var created = await CreateClassroomAsync(executor, school.Id, "2ª A", SchoolYear);

        // Act
        var invalidName = await ExpectFailureAsync(executor, UpdateClassroomCommand.RequestName, new
        {
            id = created.Id,
            name = "   ",
            schoolYear = SchoolYear,
            baseRowVersion = created.RowVersion,
        });
        var invalidYear = await ExpectFailureAsync(executor, UpdateClassroomCommand.RequestName, new
        {
            id = created.Id,
            name = "2ª B",
            schoolYear = "2026/2028",
            baseRowVersion = created.RowVersion,
        });

        // Assert
        Assert.Equal(CodiErrorCodes.InvalidName, invalidName.Code);
        Assert.Equal(CodiErrorCodes.ValidationFailed, invalidYear.Code);
        Assert.Equal("2ª A", (await GetClassroomAsync(executor, created.Id)).Name);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UpdateClassroomConGliStessiValori_NonAlzaLaVersione()
    {
        // Arrange
        var executor = CreateExecutor();
        var school = await CreateSchoolAsync(executor, "Scuola Manzoni");
        var created = await CreateClassroomAsync(executor, school.Id, "2ª A", SchoolYear);

        using var payload = Payload(new
        {
            id = created.Id,
            name = "  2ª  A ",
            schoolYear = "2026-2027",
            baseRowVersion = created.RowVersion,
        });

        // Act
        var unchanged = Assert.IsType<ClassroomDto>(await executor.ExecuteAsync(
            CodiRequestKind.Command,
            UpdateClassroomCommand.RequestName,
            CodiRequestChannel.Http,
            payload.RootElement,
            AdminContext(),
            CancellationToken.None));

        // Assert: nome e anno uguali nella forma normalizzata non sono una modifica.
        Assert.Equal(created, unchanged);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ArchiveClassroomSenzaVersioneOConVersioneObsoleta_Rifiuta()
    {
        // Arrange
        var executor = CreateExecutor();
        var school = await CreateSchoolAsync(executor, "Scuola Manzoni");
        var created = await CreateClassroomAsync(executor, school.Id, "2ª A", SchoolYear);

        // Act
        var missing = await ExpectFailureAsync(
            executor, ArchiveClassroomCommand.RequestName, new { id = created.Id });
        var obsolete = await ExpectFailureAsync(executor, ArchiveClassroomCommand.RequestName, new
        {
            id = created.Id,
            baseRowVersion = 9,
        });

        // Assert
        Assert.Equal(CodiErrorCodes.EntityVersionRequired, missing.Code);
        Assert.Equal(CodiErrorCodes.RowVersionMismatch, obsolete.Code);
        Assert.False((await GetClassroomAsync(executor, created.Id)).IsArchived);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ArchiveClassroomRipetuta_NonCambiaNulla()
    {
        // Arrange
        var executor = CreateExecutor();
        var school = await CreateSchoolAsync(executor, "Scuola Manzoni");
        var created = await CreateClassroomAsync(executor, school.Id, "2ª A", SchoolYear);
        var archived = Assert.IsType<ClassroomDto>(await SendAsync(
            executor, ArchiveClassroomCommand.RequestName,
            new { id = created.Id, baseRowVersion = created.RowVersion }));

        // Act
        var again = Assert.IsType<ClassroomDto>(await SendAsync(
            executor, ArchiveClassroomCommand.RequestName,
            new { id = created.Id, baseRowVersion = archived.RowVersion }));

        // Assert
        Assert.Equal(archived, again);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_GetCourse_RestituisceIlDettaglioERifiutaUnIdentificativoInesistente()
    {
        // Arrange
        var executor = CreateExecutor();
        var course = await CreateCourseAsync(executor, "Informatica di base", "Con esercizi");

        // Act
        using var payload = Payload(new { id = course.Id });
        var found = Assert.IsType<CourseDto>(await executor.ExecuteAsync(
            CodiRequestKind.Query,
            GetCourseQuery.RequestName,
            CodiRequestChannel.Http,
            payload.RootElement,
            AdminContext(),
            CancellationToken.None));

        using var unknown = Payload(new { id = Guid.NewGuid() });
        var missing = await ExpectQueryFailureAsync(executor, GetCourseQuery.RequestName, new { id = Guid.NewGuid() });

        // Assert
        Assert.Equal(course, found);
        Assert.Equal(CodiErrorCodes.ResourceNotFound, missing.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_CreateCourseConDatiNonValidi_RifiutaConIlCodiceAtteso()
    {
        // Arrange
        var executor = CreateExecutor();

        // Act
        var invalidTitle = await ExpectFailureAsync(
            executor, CreateCourseCommand.RequestName, new { title = "   " });
        var longDescription = await ExpectFailureAsync(executor, CreateCourseCommand.RequestName, new
        {
            title = "Informatica di base",
            description = new string('a', 2001),
        });

        // Assert
        Assert.Equal(CodiErrorCodes.InvalidName, invalidTitle.Code);
        Assert.Equal(CodiErrorCodes.ValidationFailed, longDescription.Code);
        Assert.Empty(await ListCoursesAsync(executor));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UpdateCourseSenzaVersioneConVersioneObsoletaEConDatiNonValidi_Rifiuta()
    {
        // Arrange
        var executor = CreateExecutor();
        var course = await CreateCourseAsync(executor, "Informatica di base");

        // Act
        var invalidTitle = await ExpectFailureAsync(executor, UpdateCourseCommand.RequestName, new
        {
            id = course.Id,
            title = "   ",
            baseRowVersion = course.RowVersion,
        });
        var missingVersion = await ExpectFailureAsync(executor, UpdateCourseCommand.RequestName, new
        {
            id = course.Id,
            title = "Informatica avanzata",
        });
        var obsolete = await ExpectFailureAsync(executor, UpdateCourseCommand.RequestName, new
        {
            id = course.Id,
            title = "Informatica avanzata",
            baseRowVersion = 9,
        });
        var unknown = await ExpectFailureAsync(executor, UpdateCourseCommand.RequestName, new
        {
            id = Guid.NewGuid(),
            title = "Informatica avanzata",
            baseRowVersion = 1,
        });

        // Assert
        Assert.Equal(CodiErrorCodes.InvalidName, invalidTitle.Code);
        Assert.Equal(CodiErrorCodes.EntityVersionRequired, missingVersion.Code);
        Assert.Equal(CodiErrorCodes.RowVersionMismatch, obsolete.Code);
        Assert.Equal(CodiErrorCodes.ResourceNotFound, unknown.Code);
        Assert.Equal("Informatica di base", Assert.Single(await ListCoursesAsync(executor)).Title);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ArchiveCourseSenzaVersioneOConVersioneObsoleta_Rifiuta()
    {
        // Arrange
        var executor = CreateExecutor();
        var course = await CreateCourseAsync(executor, "Informatica di base");

        // Act
        var missing = await ExpectFailureAsync(
            executor, ArchiveCourseCommand.RequestName, new { id = course.Id });
        var obsolete = await ExpectFailureAsync(executor, ArchiveCourseCommand.RequestName, new
        {
            id = course.Id,
            baseRowVersion = 4,
        });

        // Assert
        Assert.Equal(CodiErrorCodes.EntityVersionRequired, missing.Code);
        Assert.Equal(CodiErrorCodes.RowVersionMismatch, obsolete.Code);
        Assert.False(Assert.Single(await ListCoursesAsync(executor)).IsArchived);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UpdateCourseConGliStessiValori_NonAlzaLaVersione()
    {
        // Arrange
        var executor = CreateExecutor();
        var course = await CreateCourseAsync(executor, "Informatica di base", "Con esercizi");

        using var payload = Payload(new
        {
            id = course.Id,
            title = "  Informatica di base  ",
            description = "Con esercizi",
            baseRowVersion = course.RowVersion,
        });

        // Act
        var unchanged = Assert.IsType<CourseDto>(await executor.ExecuteAsync(
            CodiRequestKind.Command,
            UpdateCourseCommand.RequestName,
            CodiRequestChannel.Http,
            payload.RootElement,
            AdminContext(),
            CancellationToken.None));

        // Assert
        Assert.Equal(course, unchanged);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UpdateCourseConDescrizioneTroppoLunga_RifiutaConIlCodiceDiValidazione()
    {
        // Arrange
        var executor = CreateExecutor();
        var course = await CreateCourseAsync(executor, "Informatica di base");

        // Act
        var exception = await ExpectFailureAsync(executor, UpdateCourseCommand.RequestName, new
        {
            id = course.Id,
            title = "Informatica di base",
            description = new string('a', 2001),
            baseRowVersion = course.RowVersion,
        });

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, exception.Code);
        Assert.Equal(string.Empty, Assert.Single(await ListCoursesAsync(executor)).Description);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ArchiveCourseRipetuta_NonCambiaNulla()
    {
        // Arrange
        var executor = CreateExecutor();
        var course = await CreateCourseAsync(executor, "Informatica di base");
        var archived = Assert.IsType<CourseDto>(await SendAsync(
            executor, ArchiveCourseCommand.RequestName,
            new { id = course.Id, baseRowVersion = course.RowVersion }));

        // Act
        var again = Assert.IsType<CourseDto>(await SendAsync(
            executor, ArchiveCourseCommand.RequestName,
            new { id = course.Id, baseRowVersion = archived.RowVersion }));

        // Assert
        Assert.Equal(archived, again);
    }

    private static CodiRequestContext AdminContext() => new(isAuthenticated: true, [CodiPolicy.Admin]);

    private CodiRequestExecutor CreateExecutor()
    {
        var scope = _services.CreateScope();

        return scope.ServiceProvider.GetRequiredService<CodiRequestExecutor>();
    }

    private static JsonDocument Payload(object body) =>
        JsonDocument.Parse(JsonSerializer.Serialize(body, CodiJson.Payload));

    private static async Task<object?> SendAsync(CodiRequestExecutor executor, string name, object body)
    {
        using var payload = Payload(body);

        return await executor.ExecuteAsync(
            CodiRequestKind.Command, name, CodiRequestChannel.Http, payload.RootElement,
            AdminContext(), CancellationToken.None);
    }

    /** Esegue un comando che deve essere rifiutato e restituisce l'errore applicativo prodotto. */
    private static Task<CodiRequestException> ExpectFailureAsync(
        CodiRequestExecutor executor, string name, object body) =>
        Assert.ThrowsAsync<CodiRequestException>(() => SendAsync(executor, name, body));

    /** Come sopra, per una query: il documento del payload resta vivo fino alla fine dell'attesa. */
    private static async Task<CodiRequestException> ExpectQueryFailureAsync(
        CodiRequestExecutor executor, string name, object body)
    {
        using var payload = Payload(body);

        return await Assert.ThrowsAsync<CodiRequestException>(async () =>
            await executor.ExecuteAsync(
                CodiRequestKind.Query, name, CodiRequestChannel.Http, payload.RootElement,
                AdminContext(), CancellationToken.None));
    }

    private static async Task<SchoolDto> CreateSchoolAsync(CodiRequestExecutor executor, string name)
    {
        using var payload = Payload(new { name });

        return Assert.IsType<SchoolDto>(await executor.ExecuteAsync(
            CodiRequestKind.Command, CreateSchoolCommand.RequestName, CodiRequestChannel.Http,
            payload.RootElement, AdminContext(), CancellationToken.None));
    }

    private static async Task<ClassroomDto> CreateClassroomAsync(
        CodiRequestExecutor executor, Guid schoolId, string name, string schoolYear)
    {
        using var payload = Payload(new { schoolId, name, schoolYear });

        return Assert.IsType<ClassroomDto>(await executor.ExecuteAsync(
            CodiRequestKind.Command, CreateClassroomCommand.RequestName, CodiRequestChannel.Http,
            payload.RootElement, AdminContext(), CancellationToken.None));
    }

    private static async Task<ClassroomDto> UpdateClassroomAsync(
        CodiRequestExecutor executor, ClassroomDto classroom, string name, string schoolYear)
    {
        using var payload = Payload(new { id = classroom.Id, name, schoolYear, baseRowVersion = classroom.RowVersion });

        return Assert.IsType<ClassroomDto>(await executor.ExecuteAsync(
            CodiRequestKind.Command, UpdateClassroomCommand.RequestName, CodiRequestChannel.Http,
            payload.RootElement, AdminContext(), CancellationToken.None));
    }

    private static async Task<ClassroomDto> GetClassroomAsync(CodiRequestExecutor executor, Guid id)
    {
        using var payload = Payload(new { id });

        return Assert.IsType<ClassroomDto>(await executor.ExecuteAsync(
            CodiRequestKind.Query, GetClassroomQuery.RequestName, CodiRequestChannel.Http,
            payload.RootElement, AdminContext(), CancellationToken.None));
    }

    private static async Task<IReadOnlyList<ClassroomDto>> ListClassroomsAsync(
        CodiRequestExecutor executor, Guid schoolId, bool includeArchived = false)
    {
        using var payload = Payload(new { schoolId, includeArchived });

        return Assert.IsAssignableFrom<IReadOnlyList<ClassroomDto>>(await executor.ExecuteAsync(
            CodiRequestKind.Query, ListClassroomsQuery.RequestName, CodiRequestChannel.Http,
            payload.RootElement, AdminContext(), CancellationToken.None));
    }

    private static async Task<CourseDto> CreateCourseAsync(
        CodiRequestExecutor executor, string title, string? description = null)
    {
        using var payload = Payload(new { title, description });

        return Assert.IsType<CourseDto>(await executor.ExecuteAsync(
            CodiRequestKind.Command, CreateCourseCommand.RequestName, CodiRequestChannel.Http,
            payload.RootElement, AdminContext(), CancellationToken.None));
    }

    private static async Task<IReadOnlyList<CourseDto>> ListCoursesAsync(
        CodiRequestExecutor executor, bool includeArchived = false)
    {
        using var payload = Payload(new { includeArchived });

        return Assert.IsAssignableFrom<IReadOnlyList<CourseDto>>(await executor.ExecuteAsync(
            CodiRequestKind.Query, ListCoursesQuery.RequestName, CodiRequestChannel.Http,
            payload.RootElement, AdminContext(), CancellationToken.None));
    }
}
