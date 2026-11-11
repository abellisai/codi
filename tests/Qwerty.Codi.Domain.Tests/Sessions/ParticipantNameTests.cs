using Qwerty.Codi.Domain.Sessions;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Sessions;

/// <summary>
/// Nome del partecipante (§7.4): 1–30 grafemi, senza caratteri di controllo e senza spazi ai bordi.
/// Il limite è sui grafemi e non sui caratteri, quindi i casi includono un'emoji composta, che
/// vale uno, e una lettera accentata in forma scomposta, che resta una lettera sola.
/// </summary>
public sealed class ParticipantNameTests
{
    [Theory]
    [InlineData("Luca")]
    [InlineData("  Anna Maria  ")]
    [InlineData("👩‍🏫")]
    [InlineData("e\u0301")]
    [Trait("Category", "Unit")]
    public void IsUsable_ConNomeValido_Accetta(string value)
    {
        Assert.True(ParticipantName.IsUsable(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Lu\tca")]
    [Trait("Category", "Unit")]
    public void IsUsable_ConNomeVuotoOSenzaTestoUtile_Rifiuta(string? value)
    {
        Assert.False(ParticipantName.IsUsable(value));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsUsable_AlLimiteDeiTrentaGrafemi_Accetta()
    {
        // Arrange: trenta grafemi sono il massimo accettato (§7.4).
        var atLimit = new string('a', ParticipantName.MaxGraphemes);

        // Act / Assert
        Assert.True(ParticipantName.IsUsable(atLimit));
        Assert.False(ParticipantName.IsUsable(atLimit + "a"));
    }

    [Theory]
    [InlineData("👩‍🏫", 1)]
    [InlineData("e\u0301", 1)]
    [InlineData("Anna Maria", 10)]
    [InlineData("à", 1)]
    [Trait("Category", "Unit")]
    public void GraphemeCount_ContaIGrafemiENonIPuntiDiCodice(string value, int expected)
    {
        // Una lettera accentata scomposta e un'emoji composta occupano più punti di codice ma sono
        // un solo «carattere»: contare i punti di codice rifiuterebbe nomi legittimi.
        Assert.Equal(expected, ParticipantName.GraphemeCount(value));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Trim_ToglieGliSpaziAiBordiEConservaQuelliInterni()
    {
        // Il nome lo scrive un bambino: riscrivere gli spazi interni cambierebbe ciò che ha scritto,
        // mentre gli spazi ai bordi non fanno parte del nome.
        Assert.Equal("Anna  Maria", ParticipantName.Trim("  Anna  Maria "));
    }
}
