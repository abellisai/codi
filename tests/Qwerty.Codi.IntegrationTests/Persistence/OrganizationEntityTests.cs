using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Organization;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Persistence;

/// <summary>
/// Conversione fra dominio ed entità di persistenza per scuole, classi e corsi (§7.1):
/// formato esplicito, identità conservata e righe fuori formato rifiutate in lettura.
/// </summary>
public sealed class OrganizationEntityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid SchoolId = Guid.Parse("3b3f1d7c-6c8f-4a1e-92b5-2e6f5a4d3c2b");

    [Fact]
    [Trait("Category", "Unit")]
    public void Mapping_ConClasseArchiviata_PreservaScuolaAnnoEFormato()
    {
        // Arrange
        var classroom = Classroom.Create(SchoolId, "2ª A", "2026-2027", Now);
        classroom.Archive(Now.AddMinutes(20));

        // Act
        var row = ClassroomEntity.FromDomain(classroom);
        var restored = row.ToDomain();

        // Assert
        Assert.Equal(classroom.Id.ToString("D"), row.Id);
        Assert.Equal(SchoolId.ToString("D"), row.SchoolId);
        Assert.Equal("2026/2027", row.SchoolYear);
        Assert.Equal(1, row.IsArchivedFlag);
        Assert.Equal(Now.ToUnixTimeMilliseconds(), row.CreatedAtUtcMs);

        Assert.Equal(classroom.Id, restored.Id);
        Assert.Equal(SchoolId, restored.SchoolId);
        Assert.Equal("2ª A", restored.Name);
        Assert.Equal("2ª a", restored.NormalizedName);
        Assert.Equal("2026/2027", restored.SchoolYear);
        Assert.True(restored.IsArchived);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToDomain_ConAnnoNonCanonico_RifiutaLaRiga()
    {
        var row = ClassroomEntity.FromDomain(Classroom.Create(SchoolId, "2ª A", "2026/2027", Now));
        row.SchoolYear = "2026-2027";

        var error = Assert.Throws<ArgumentException>(() => row.ToDomain());

        Assert.Equal("schoolYear", error.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Mapping_ConCorsoSenzaDescrizione_RiempieLaColonnaConUnaStringaVuota()
    {
        var course = Course.Create("Informatica di base", null, Now);

        var row = CourseEntity.FromDomain(course);
        var restored = row.ToDomain();

        Assert.Equal(string.Empty, row.Description);
        Assert.Equal(course.Id, restored.Id);
        Assert.Equal("Informatica di base", restored.Title);
        Assert.Equal(string.Empty, restored.Description);
        Assert.False(restored.IsArchived);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToDomain_ConFlagNonBooleani_RifiutaLaRiga()
    {
        // Un flag diverso da 0 e 1 non è uno stato: leggerlo come «falso» riaprirebbe in
        // silenzio una classe archiviata o un corso archiviato.
        var classroom = ClassroomEntity.FromDomain(Classroom.Create(SchoolId, "2ª A", "2026/2027", Now));
        classroom.IsArchivedFlag = 2;

        var course = CourseEntity.FromDomain(Course.Create("Informatica di base", null, Now));
        course.IsArchivedFlag = 2;

        Assert.Equal(nameof(ClassroomEntity.IsArchivedFlag), Assert.Throws<ArgumentException>(() => classroom.ToDomain()).ParamName);
        Assert.Equal(nameof(CourseEntity.IsArchivedFlag), Assert.Throws<ArgumentException>(() => course.ToDomain()).ParamName);
    }
}
