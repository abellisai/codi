using Qwerty.Codi.Domain.Lessons;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Lessons;

/// <summary>
/// Invarianti della lezione (§7.3): appartiene a un corso, ha un titolo utilizzabile, una
/// descrizione facoltativa normalizzata a stringa vuota e un'archiviazione registrata una volta
/// sola. Il corso non cambia dopo la creazione.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LessonTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid CourseId = Guid.Parse("8a1f6b6c-0d2e-4a1b-9c3d-5e6f7a8b9c0d");

    [Fact]
    public void Create_ConTitoloESenzaDescrizione_ConservaIlCorsoERipulisceLaDescrizione()
    {
        var lesson = Lesson.Create(CourseId, "  Prima lezione  ", null, Now);

        Assert.NotEqual(Guid.Empty, lesson.Id);
        Assert.Equal(CourseId, lesson.CourseId);
        Assert.Equal("Prima lezione", lesson.Title);
        Assert.Equal(string.Empty, lesson.Description);
        Assert.False(lesson.IsArchived);
        Assert.Equal(Now, lesson.CreatedAtUtc);
        Assert.Equal(Now, lesson.UpdatedAtUtc);
        Assert.Equal(1, lesson.RowVersion);
    }

    [Fact]
    public void Create_SenzaCorso_Rifiuta()
    {
        var exception = Assert.Throws<ArgumentException>(() => Lesson.Create(Guid.Empty, "Prima lezione", null, Now));

        Assert.Equal("courseId", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ConTitoloInutilizzabile_Rifiuta(string title)
    {
        var exception = Assert.Throws<ArgumentException>(() => Lesson.Create(CourseId, title, null, Now));

        Assert.Equal("title", exception.ParamName);
    }

    [Fact]
    public void Create_ConTitoloTroppoLungo_Rifiuta()
    {
        var tooLong = new string('a', Lesson.MaxTitleLength + 1);

        var exception = Assert.Throws<ArgumentException>(() => Lesson.Create(CourseId, tooLong, null, Now));

        Assert.Equal("title", exception.ParamName);
    }

    [Fact]
    public void Create_ConDescrizioneTroppoLunga_Rifiuta()
    {
        var tooLong = new string('a', Lesson.MaxDescriptionLength + 1);

        var exception = Assert.Throws<ArgumentException>(() => Lesson.Create(CourseId, "Prima lezione", tooLong, Now));

        Assert.Equal("description", exception.ParamName);
    }

    [Fact]
    public void Create_ConIstanteNonUtc_Rifiuta()
    {
        var local = new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.FromHours(2));

        var exception = Assert.Throws<ArgumentException>(() => Lesson.Create(CourseId, "Prima lezione", null, local));

        Assert.Equal("nowUtc", exception.ParamName);
    }

    [Fact]
    public void Update_ConValoriDiversi_CambiaTitoloEDescrizioneEAggiornaLaModifica()
    {
        var lesson = Lesson.Create(CourseId, "Prima lezione", "Descrizione", Now);
        var later = Now.AddMinutes(10);

        Assert.True(lesson.Update("Seconda lezione", "Altra descrizione", later));

        Assert.Equal("Seconda lezione", lesson.Title);
        Assert.Equal("Altra descrizione", lesson.Description);
        Assert.Equal(later, lesson.UpdatedAtUtc);
        Assert.Equal(Now, lesson.CreatedAtUtc);
        Assert.Equal(CourseId, lesson.CourseId);
    }

    [Theory]
    [InlineData("Prima lezione", null)]
    [InlineData("  Prima lezione  ", "")]
    [InlineData("Prima lezione", "   ")]
    public void Update_ConGliStessiValori_NonEUnaModifica(string title, string? description)
    {
        var lesson = Lesson.Create(CourseId, "Prima lezione", null, Now);

        Assert.False(lesson.Update(title, description, Now.AddMinutes(5)));
        Assert.Equal(Now, lesson.UpdatedAtUtc);
    }

    [Fact]
    public void Archive_PrimaVolta_CambiaStatoESecondaVolta_NonEUnErrore()
    {
        var lesson = Lesson.Create(CourseId, "Prima lezione", null, Now);
        var archived = Now.AddMinutes(1);

        Assert.True(lesson.Archive(archived));
        Assert.True(lesson.IsArchived);
        Assert.False(lesson.Archive(Now.AddMinutes(2)));
        Assert.Equal(archived, lesson.UpdatedAtUtc);
    }

    [Fact]
    public void Restore_SenzaIdentificativoOSenzaCorso_RifiutaLaRicostruzione()
    {
        var withoutId = Assert.Throws<ArgumentException>(() => Lesson.Restore(
            Guid.Empty, CourseId, "Prima lezione", string.Empty, isArchived: false, Now, Now, rowVersion: 1));
        var withoutCourse = Assert.Throws<ArgumentException>(() => Lesson.Restore(
            Guid.NewGuid(), Guid.Empty, "Prima lezione", string.Empty, isArchived: false, Now, Now, rowVersion: 1));

        Assert.Equal("id", withoutId.ParamName);
        Assert.Equal("courseId", withoutCourse.ParamName);
    }

    [Fact]
    public void Restore_ConUltimaModificaPrecedenteAllaCreazione_RifiutaLaRicostruzione()
    {
        var exception = Assert.Throws<ArgumentException>(() => Lesson.Restore(
            Guid.NewGuid(), CourseId, "Prima lezione", string.Empty,
            isArchived: false, createdAtUtc: Now, updatedAtUtc: Now.AddMinutes(-1), rowVersion: 1));

        Assert.Equal("updatedAtUtc", exception.ParamName);
    }

    [Fact]
    public void Restore_ConVersioneNonPositiva_RifiutaLaRicostruzione()
    {
        var exception = Assert.Throws<ArgumentException>(() => Lesson.Restore(
            Guid.NewGuid(), CourseId, "Prima lezione", string.Empty,
            isArchived: false, Now, Now, rowVersion: 0));

        Assert.Equal("rowVersion", exception.ParamName);
    }

    [Fact]
    public void WithRowVersion_ConVersioneNonPositiva_Rifiuta()
    {
        var lesson = Lesson.Create(CourseId, "Prima lezione", null, Now);

        var exception = Assert.Throws<ArgumentException>(() => lesson.WithRowVersion(0));

        Assert.Equal("rowVersion", exception.ParamName);
    }
}
