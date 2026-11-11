using Qwerty.Codi.Domain.Sessions;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Sessions;

/// <summary>
/// Codice di ingresso della sessione (§6.1, §9.4): otto cifre decimali, prima cifra diversa da zero,
/// forma verificata sia in generazione sia in ricostruzione. Il codice si detta a voce e si digita:
/// accettare una forma diversa significherebbe far entrare in una sessione con un valore che nessuno
/// ha detto.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SessionJoinCodeTests
{
    [Fact]
    public void Create_ProduceOttoCifreSenzaZeroIniziale()
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var code = SessionJoinCode.Create();

            Assert.Equal(SessionJoinCode.Length, code.Value.Length);
            Assert.True(SessionJoinCode.IsWellFormed(code.Value), $"Codice non ammissibile: '{code.Value}'.");
            Assert.NotEqual('0', code.Value[0]);
            Assert.Equal(code.Value, code.ToString());
        }
    }

    [Fact]
    public void Restore_ConCodiceValido_ConservaLeOttoCifre()
    {
        var code = SessionJoinCode.Restore("87654321");

        Assert.Equal("87654321", code.Value);
        Assert.Equal("87654321", code.ToString());
    }

    [Theory]
    [InlineData("1234567")]
    [InlineData("123456789")]
    [InlineData("")]
    [InlineData("01234567")]
    [InlineData("1234567a")]
    [InlineData("1234 678")]
    [InlineData("1234-678")]
    [InlineData("１２３４５６７８")]
    public void Restore_ConFormaNonAmmessa_Rifiuta(string value)
    {
        var exception = Assert.Throws<ArgumentException>(() => SessionJoinCode.Restore(value));

        Assert.Equal("value", exception.ParamName);
    }

    [Theory]
    [InlineData("12345678", true)]
    [InlineData("99999999", true)]
    [InlineData("10000000", true)]
    [InlineData("09999999", false)]
    [InlineData("1234567", false)]
    [InlineData(null, false)]
    public void IsWellFormed_RiconosceSoloLeOttoCifre(string? value, bool expected)
    {
        Assert.Equal(expected, SessionJoinCode.IsWellFormed(value));
    }
}
