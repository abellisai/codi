using Qwerty.Codi.Domain.Sessions;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Sessions;

/// <summary>
/// Testo di un messaggio della chat (§8, §15.1): il limite è in grafemi come per il nome del
/// partecipante, perché il testo lo scrive un bambino. Non esiste un filtro sul contenuto — si
/// verifica solo la forma — quindi i casi riguardano il vuoto, i caratteri di controllo e il limite
/// contato come lo conta una persona: un'emoji composta e una lettera accentata scomposta valgono uno.
/// </summary>
public sealed class ParticipantMessageTextTests
{
    [Theory]
    [InlineData("Maestra, non capisco il blocco")]
    [InlineData("  ho finito  ")]
    [InlineData("👩‍🏫")]
    [InlineData("e\u0301")]
    [Trait("Category", "Unit")]
    public void IsUsable_ConTestoValido_Accetta(string value)
    {
        Assert.True(ParticipantMessageText.IsUsable(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ciao\nCiao")]
    [InlineData("Ciao\tCiao")]
    [Trait("Category", "Unit")]
    public void IsUsable_ConTestoVuotoOConCaratteriDiControllo_Rifiuta(string? value)
    {
        // Un a capo nascosto non si mostra nella chat dell'alunno: il carattere di controllo non è
        // parte di ciò che il bambino ha scritto, e un testo vuoto non è un messaggio.
        Assert.False(ParticipantMessageText.IsUsable(value));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsUsable_AlLimiteDeiTrecentoGrafemi_AccettaEoltreRifiuta()
    {
        var atLimit = new string('a', ParticipantMessageText.MaxGraphemes);

        Assert.True(ParticipantMessageText.IsUsable(atLimit));
        Assert.False(ParticipantMessageText.IsUsable(atLimit + "a"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsUsable_ConTrecentoGrafemiDiEmojiContatiComePersone_Accetta()
    {
        // Trenta emoji composte sono trenta grafemi anche se occupano più punti di codice: contare i
        // punti di codice rifiuterebbe un messaggio legittimo scritto da un bambino.
        var emoji = string.Join("", Enumerable.Repeat("👩‍🏫", 30));
        Assert.Equal(30, ParticipantMessageText.GraphemeCount(emoji));
        Assert.True(ParticipantMessageText.IsUsable(emoji));
    }

    [Theory]
    [InlineData("👩‍🏫", 1)]
    [InlineData("e\u0301", 1)]
    [InlineData("Anna Maria", 10)]
    [Trait("Category", "Unit")]
    public void GraphemeCount_ContaIGrafemiENonIPuntiDiCodice(string value, int expected)
    {
        Assert.Equal(expected, ParticipantMessageText.GraphemeCount(value));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Trim_ToglieGliSpaziAiBordiEConservaQuelliInterni()
    {
        // Gli spazi interni fanno parte di ciò che il bambino ha scritto: riscriverli cambierebbe il
        // messaggio mostrato al docente.
        Assert.Equal("non  capisco", ParticipantMessageText.Trim("  non  capisco  "));
    }
}
