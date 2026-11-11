using Qwerty.Codi.Domain.Admin;
using Qwerty.Codi.Domain.Common;
using Qwerty.Codi.Domain.Organization;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Organization;

/// <summary>
/// Ammissibilità e normalizzazione del nome di una scuola (§7.2). Sono test di sola logica: la
/// normalizzazione decide l'unicità, quindi deve rendere uguali le scritture dello stesso nome.
/// </summary>
public sealed class SchoolNameTests
{
    [Theory]
    [InlineData("Scuola Primaria Manzoni")]
    [InlineData("  Scuola Primaria Manzoni  ")]
    public void IsValid_ConNomeUtilizzabile_Accetta(string name)
    {
        Assert.True(SchoolName.IsValid(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    [InlineData("Scuola\u0007Manzoni")]
    public void IsValid_ConNomeInutilizzabile_Rifiuta(string? name)
    {
        Assert.False(SchoolName.IsValid(name));
    }

    [Fact]
    public void IsValid_ConNomeAlLimite_AccettaEOltre_Rifiuta()
    {
        var atLimit = new string('a', SchoolName.MaxLength);
        var beyondLimit = new string('a', SchoolName.MaxLength + 1);

        Assert.True(SchoolName.IsValid(atLimit));
        Assert.False(SchoolName.IsValid(beyondLimit));
    }

    [Theory]
    [InlineData("Scuola Manzoni")]
    [InlineData("SCUOLA MANZONI")]
    [InlineData("scuola manzoni")]
    [InlineData("  Scuola   Manzoni  ")]
    [InlineData("Scuola Manzòni")]
    public void Normalize_ConScrittureDiverseDelloStessoNome_ProduceLoStessoValore(string written)
    {
        Assert.Equal("scuola manzoni", SchoolName.Normalize(written));
    }

    [Fact]
    public void Normalize_ConNomiDiversi_ProduceValoriDiversi()
    {
        Assert.NotEqual(SchoolName.Normalize("Scuola Manzoni"), SchoolName.Normalize("Scuola Manzoni 2"));
    }

    [Fact]
    public void IsUsable_ConLimiteNonPositivo_RifiutaLaConfigurazione()
    {
        // Il limite è un parametro delle regole condivise: un valore non positivo è un errore di
        // programmazione, non un nome inutilizzabile.
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => NameNormalization.IsUsable("Scuola Manzoni", 0));

        Assert.Equal("maxLength", exception.ParamName);
    }

    [Fact]
    public void Normalize_ConNomeAmministrativoEConNomeDiScuola_UsaLaStessaRegola()
    {
        // La regola è condivisa: due implementazioni diverse darebbero due risultati diversi per
        // lo stesso testo, e l'unicità dipenderebbe da quale delle due è stata usata.
        const string Written = "  Sant'Ambrogio  ";

        Assert.Equal(AdminUsername.Normalize(Written), SchoolName.Normalize(Written));
    }
}
