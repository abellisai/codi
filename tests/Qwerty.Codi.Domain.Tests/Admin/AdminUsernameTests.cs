using Qwerty.Codi.Domain.Admin;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Admin;

/// <summary>
/// Normalizzazione del nome amministrativo (§7.2): decide l'unicità dell'accesso, quindi deve
/// rendere uguali le scritture dello stesso nome e diversi i nomi diversi. Sono test di sola
/// logica: nessun database, nessun file.
/// </summary>
public sealed class AdminUsernameTests
{
    [Theory]
    [InlineData("Maestra")]
    [InlineData("MAESTRA")]
    [InlineData("maestra")]
    [InlineData("  Maestra  ")]
    [InlineData("maestrà")]
    [InlineData("Maestra\u0300")]
    public void Normalize_ConScrittureDiverseDelloStessoNome_ProduceLoStessoValore(string written)
    {
        Assert.Equal("maestra", AdminUsername.Normalize(written));
    }

    [Theory]
    [InlineData("Anna  Maria", "anna maria")]
    [InlineData("Anna Maria", "anna maria")]
    [InlineData("Anna\tMaria", "anna maria")]
    [InlineData("Mae\tstra", "mae stra")]
    public void Normalize_ConSpaziInterni_RiduceAUnoSolo(string written, string expected)
    {
        // Gli spazi interni si riducono, non si eliminano: «AnnaMaria» e «Anna Maria» restano due
        // nomi diversi, e questo è il motivo per cui un tab dentro una parola non diventa la
        // parola unita.
        Assert.Equal(expected, AdminUsername.Normalize(written));
    }

    [Fact]
    public void Normalize_ConNomiDiversi_ProduceValoriDiversi()
    {
        Assert.NotEqual(AdminUsername.Normalize("Maestra"), AdminUsername.Normalize("Maestro"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    [InlineData("Maestra\u0007")]
    public void IsValid_ConNomeInutilizzabile_Rifiuta(string? username)
    {
        Assert.False(AdminUsername.IsValid(username));
    }

    [Fact]
    public void IsValid_ConNomeTroppoLungo_Rifiuta()
    {
        var tooLong = new string('a', AdminUsername.MaxLength + 1);

        Assert.False(AdminUsername.IsValid(tooLong));
        Assert.True(AdminUsername.IsValid(new string('a', AdminUsername.MaxLength)));
    }

    [Fact]
    public void IsValid_ConNomeOrdinario_Accetta()
    {
        Assert.True(AdminUsername.IsValid("Maestra Anna"));
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(" ")]
    [InlineData("\u0300")]
    public void IsValid_ConNomeDigitatoTroppoLungoAncheSeNormalizzatoBreve_Rifiuta(string padding)
    {
        // Arrange: il database limita anche il nome visualizzato, non soltanto la chiave.
        var username = "a" + string.Concat(Enumerable.Repeat(padding, AdminUsername.MaxLength)) + "b";

        // Act / Assert
        Assert.True(AdminUsername.Normalize(username).Length <= AdminUsername.MaxLength);
        Assert.False(AdminUsername.IsValid(username));
    }

}
