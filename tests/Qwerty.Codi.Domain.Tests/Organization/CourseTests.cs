using Qwerty.Codi.Domain.Organization;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Organization;

/// <summary>
/// Invarianti del corso (§7.2): titolo obbligatorio e utilizzabile, descrizione facoltativa
/// normalizzata a stringa vuota, archiviazione registrata una volta sola.
/// </summary>
public sealed class CourseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ConTitoloESenzaDescrizione_RipulisceEAccettaLaDescrizioneAssente()
    {
        var course = Course.Create("  Informatica di base  ", null, Now);

        Assert.NotEqual(Guid.Empty, course.Id);
        Assert.Equal("Informatica di base", course.Title);
        Assert.Equal(string.Empty, course.Description);
        Assert.False(course.IsArchived);
        Assert.Equal(Now, course.CreatedAtUtc);
        Assert.Equal(1, course.RowVersion);
    }

    [Fact]
    public void Create_ConDescrizioneDiSoliSpazi_LaTrattaComeAssente()
    {
        var course = Course.Create("Informatica di base", "   ", Now);

        Assert.Equal(string.Empty, course.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ConTitoloInutilizzabile_Rifiuta(string title)
    {
        var exception = Assert.Throws<ArgumentException>(() => Course.Create(title, null, Now));

        Assert.Equal("title", exception.ParamName);
    }

    [Fact]
    public void Create_ConTitoloTroppoLungo_Rifiuta()
    {
        var tooLong = new string('a', Course.MaxTitleLength + 1);

        var exception = Assert.Throws<ArgumentException>(() => Course.Create(tooLong, null, Now));

        Assert.Equal("title", exception.ParamName);
    }

    [Fact]
    public void Create_ConDescrizioneTroppoLunga_Rifiuta()
    {
        var tooLong = new string('a', Course.MaxDescriptionLength + 1);

        var exception = Assert.Throws<ArgumentException>(() => Course.Create("Informatica di base", tooLong, Now));

        Assert.Equal("description", exception.ParamName);
    }

    [Fact]
    public void Create_ConIstanteNonUtc_Rifiuta()
    {
        var local = new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.FromHours(2));

        var exception = Assert.Throws<ArgumentException>(() => Course.Create("Informatica di base", null, local));

        Assert.Equal("nowUtc", exception.ParamName);
    }

    [Fact]
    public void Update_ConTitoloDiverso_CambiaIlTitoloEAggiornaLaModifica()
    {
        var course = Course.Create("Informatica di base", "Prima", Now);
        var later = Now.AddMinutes(10);

        Assert.True(course.Update("Informatica avanzata", "Seconda", later));

        Assert.Equal("Informatica avanzata", course.Title);
        Assert.Equal("Seconda", course.Description);
        Assert.Equal(later, course.UpdatedAtUtc);
        Assert.Equal(Now, course.CreatedAtUtc);
    }

    [Theory]
    [InlineData("Informatica di base", null)]
    [InlineData("  Informatica di base  ", "")]
    [InlineData("Informatica di base", "   ")]
    public void Update_ConGliStessiValori_NonEUnaModifica(string title, string? description)
    {
        var course = Course.Create("Informatica di base", null, Now);

        Assert.False(course.Update(title, description, Now.AddMinutes(5)));
        Assert.Equal(Now, course.UpdatedAtUtc);
    }

    [Fact]
    public void Archive_PrimaVolta_CambiaStatoESecondaVolta_NonEUnErrore()
    {
        var course = Course.Create("Informatica di base", null, Now);
        var archived = Now.AddMinutes(1);

        Assert.True(course.Archive(archived));
        Assert.True(course.IsArchived);
        Assert.False(course.Archive(Now.AddMinutes(2)));
        Assert.Equal(archived, course.UpdatedAtUtc);
    }

    [Fact]
    public void Restore_ConTitoloNonUtilizzabile_RifiutaLaRicostruzione()
    {
        var exception = Assert.Throws<ArgumentException>(() => Course.Restore(
            Guid.NewGuid(), "   ", string.Empty, isArchived: false, Now, Now, rowVersion: 1));

        Assert.Equal("title", exception.ParamName);
    }

    [Fact]
    public void Restore_ConVersioneNonPositiva_RifiutaLaRicostruzione()
    {
        var exception = Assert.Throws<ArgumentException>(() => Course.Restore(
            Guid.NewGuid(), "Informatica di base", string.Empty, isArchived: false, Now, Now, rowVersion: 0));

        Assert.Equal("rowVersion", exception.ParamName);
    }

    [Fact]
    public void WithRowVersion_ConVersionePositiva_CopiaSenzaToccareIlResto()
    {
        var course = Course.Create("Informatica di base", "Descrizione", Now);

        var copy = course.WithRowVersion(3);

        Assert.Equal(3, copy.RowVersion);
        Assert.Equal(1, course.RowVersion);
        Assert.Equal(course.Id, copy.Id);
        Assert.Equal(course.Description, copy.Description);
    }
}
