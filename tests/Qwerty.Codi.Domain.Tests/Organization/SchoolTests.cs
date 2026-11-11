using Qwerty.Codi.Domain.Organization;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Organization;

/// <summary>
/// Invarianti della scuola (§7.2, §6.1): nome normalizzato insieme a quello digitato, stato di
/// archiviazione che si registra una sola volta e ricostruzione che non si fida dei dati letti.
/// </summary>
public sealed class SchoolTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ConNomeUtilizzabile_NormalizzaESalvaIlNomeDigitato()
    {
        var school = School.Create("  Scuola   Manzòni ", Now);

        Assert.NotEqual(Guid.Empty, school.Id);
        Assert.Equal("Scuola   Manzòni", school.Name);
        Assert.Equal("scuola manzoni", school.NormalizedName);
        Assert.False(school.IsArchived);
        Assert.Equal(Now, school.CreatedAtUtc);
        Assert.Equal(Now, school.UpdatedAtUtc);
        Assert.Equal(1, school.RowVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ConNomeInutilizzabile_Rifiuta(string name)
    {
        var exception = Assert.Throws<ArgumentException>(() => School.Create(name, Now));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void Create_ConIstanteNonUtc_Rifiuta()
    {
        var local = new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.FromHours(2));

        var exception = Assert.Throws<ArgumentException>(() => School.Create("Scuola Manzoni", local));

        Assert.Equal("nowUtc", exception.ParamName);
    }

    [Fact]
    public void Rename_ConNomeDiverso_CambiaNomeNormalizzatoEAggiornaLaModifica()
    {
        var school = School.Create("Scuola Manzoni", Now);
        var later = Now.AddMinutes(5);

        var changed = school.Rename("  Scuola   Manzoni Due ", later);

        Assert.True(changed);
        Assert.Equal("Scuola   Manzoni Due", school.Name);
        Assert.Equal("scuola manzoni due", school.NormalizedName);
        Assert.Equal(later, school.UpdatedAtUtc);
        Assert.Equal(Now, school.CreatedAtUtc);
    }

    [Theory]
    [InlineData("Scuola Manzoni")]
    [InlineData("  Scuola Manzoni  ")]
    [Trait("Category", "Unit")]
    public void Rename_ConLoStessoNomeVisualizzato_NonEUnaModifica(string sameName)
    {
        // Salvare lo stesso testo, a parte gli spazi ai bordi, non modifica la scuola.
        var school = School.Create("Scuola Manzoni", Now);

        var changed = school.Rename(sameName, Now.AddMinutes(5));

        Assert.False(changed);
        Assert.Equal("Scuola Manzoni", school.Name);
        Assert.Equal(Now, school.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("SCUOLA MANZONI")]
    [InlineData("Scuola Manzòni")]
    [InlineData("Scuola  Manzoni")]
    [Trait("Category", "Unit")]
    public void Rename_ConNomeNormalizzatoUgualeECorrezioneVisibile_AggiornaIlNome(string newName)
    {
        // Arrange
        var school = School.Create("Scuola Manzoni", Now);
        var later = Now.AddMinutes(5);

        // Act
        var changed = school.Rename(newName, later);

        // Assert: l'identità e la chiave univoca non cambiano, il nome mostrato sì.
        Assert.True(changed);
        Assert.Equal(newName, school.Name);
        Assert.Equal("scuola manzoni", school.NormalizedName);
        Assert.Equal(later, school.UpdatedAtUtc);
        Assert.Equal(Now, school.CreatedAtUtc);
    }

    [Fact]
    public void Rename_ConNomeInutilizzabile_RifiutaSenzaModificare()
    {
        var school = School.Create("Scuola Manzoni", Now);

        Assert.Throws<ArgumentException>(() => school.Rename("   ", Now.AddMinutes(1)));
        Assert.Equal("Scuola Manzoni", school.Name);
        Assert.Equal(Now, school.UpdatedAtUtc);
    }

    [Fact]
    public void Archive_PrimaVolta_CambiaStatoESecondaVolta_NonEUnErrore()
    {
        var school = School.Create("Scuola Manzoni", Now);
        var archived = Now.AddMinutes(3);

        Assert.True(school.Archive(archived));
        Assert.True(school.IsArchived);
        Assert.Equal(archived, school.UpdatedAtUtc);

        Assert.False(school.Archive(Now.AddMinutes(4)));
        Assert.Equal(archived, school.UpdatedAtUtc);
    }

    [Fact]
    public void WithRowVersion_ConVersionePositiva_CopiaLaVersioneSenzaToccareIlResto()
    {
        var school = School.Create("Scuola Manzoni", Now);

        var copy = school.WithRowVersion(4);

        Assert.Equal(4, copy.RowVersion);
        Assert.Equal(1, school.RowVersion);
        Assert.Equal(school.Id, copy.Id);
        Assert.Equal(school.NormalizedName, copy.NormalizedName);
    }

    [Fact]
    public void Restore_ConNomeNonUtilizzabile_RifiutaLaRicostruzione()
    {
        var exception = Assert.Throws<ArgumentException>(() => School.Restore(
            Guid.NewGuid(), "   ", "scuola manzoni", isArchived: false, Now, Now, rowVersion: 1));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void WithRowVersion_ConVersioneNonPositiva_Rifiuta()
    {
        var school = School.Create("Scuola Manzoni", Now);

        var exception = Assert.Throws<ArgumentException>(() => school.WithRowVersion(0));

        Assert.Equal("rowVersion", exception.ParamName);
    }

    [Fact]
    public void Restore_ConNomeNormalizzatoIncoerente_RifiutaLaRicostruzione()
    {
        // Un archivio alterato non deve produrre un'entità incoerente: il nome normalizzato è la
        // chiave di unicità, quindi non può essere un dato qualsiasi.
        var exception = Assert.Throws<ArgumentException>(() => School.Restore(
            Guid.NewGuid(), "Scuola Manzoni", "scuola-manzoni", isArchived: false, Now, Now, rowVersion: 1));

        Assert.Equal("normalizedName", exception.ParamName);
    }

    [Fact]
    public void Restore_ConVersioneNonPositiva_RifiutaLaRicostruzione()
    {
        var exception = Assert.Throws<ArgumentException>(() => School.Restore(
            Guid.NewGuid(), "Scuola Manzoni", "scuola manzoni", isArchived: false, Now, Now, rowVersion: 0));

        Assert.Equal("rowVersion", exception.ParamName);
    }

    [Fact]
    public void Restore_ConUltimaModificaPrecedenteAllaCreazione_RifiutaLaRicostruzione()
    {
        var exception = Assert.Throws<ArgumentException>(() => School.Restore(
            Guid.NewGuid(), "Scuola Manzoni", "scuola manzoni",
            isArchived: false, Now, Now.AddMinutes(-1), rowVersion: 1));

        Assert.Equal("updatedAtUtc", exception.ParamName);
    }
}
