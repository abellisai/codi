using Qwerty.Codi.Domain.Organization;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Organization;

/// <summary>
/// Forma dell'anno scolastico (§7.2): due anni consecutivi, con la barra come forma canonica.
/// Sono test di sola logica: nessun database, nessun file.
/// </summary>
public sealed class SchoolYearNormalizationTests
{
    [Theory]
    [InlineData("2026/2027", "2026/2027")]
    [InlineData("2026-2027", "2026/2027")]
    [InlineData("  2026/2027  ", "2026/2027")]
    [InlineData("9998/9999", "9998/9999")]
    public void TryNormalize_ConAnnoUtilizzabile_ProduceLaFormaCanonica(string written, string expected)
    {
        // Il trattino è accettato perché il docente non deve ricordare quale separatore è quello
        // giusto, ma le due scritture devono dare la stessa chiave di unicità.
        Assert.True(SchoolYearNormalization.TryNormalize(written, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2026")]
    [InlineData("2026/2027/2028")]
    [InlineData("2026/27")]
    [InlineData("2026 2027")]
    [InlineData("abcd/efgh")]
    [InlineData("2026/2028")]
    [InlineData("2027/2026")]
    public void TryNormalize_ConAnnoNonUtilizzabile_Rifiuta(string? written)
    {
        Assert.False(SchoolYearNormalization.TryNormalize(written, out var normalized));
        Assert.Equal(string.Empty, normalized);
    }

    [Fact]
    public void IsValid_SegueLaStessaRegolaDellaNormalizzazione()
    {
        Assert.True(SchoolYearNormalization.IsValid("2026/2027"));
        Assert.False(SchoolYearNormalization.IsValid("2026-2028"));
    }
}
