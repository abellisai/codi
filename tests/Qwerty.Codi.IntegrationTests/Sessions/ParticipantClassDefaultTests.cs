using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.Domain.Sessions;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Sessions;

/// <summary>
/// Lettura del default di classe della sessione (§6.1, §6.2). Il documento lo scrive il server, non
/// l'utente: questa è la sola lettura che l'ingresso fa dei valori della sessione, e un documento
/// che non si sa leggere non deve diventare una vista inventata per un alunno.
/// </summary>
public sealed class ParticipantClassDefaultTests
{
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("Waiting", ParticipantView.Waiting)]
    [InlineData("Exercise", ParticipantView.Exercise)]
    [InlineData("Goodbye", ParticipantView.Goodbye)]
    public void ReadView_ConVistaDichiarata_RestituisceLaVista(string declared, ParticipantView expected)
    {
        // Act
        var view = ParticipantClassDefault.ReadView($"{{\"view\":\"{declared}\"}}");

        // Assert
        Assert.Equal(expected, view);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("non-json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"view\":12}")]
    [InlineData("{\"view\":\"attesa\"}")]
    [InlineData("{\"vista\":\"Waiting\"}")]
    public void ReadView_ConDefaultIlleggibile_RifiutaIlDocumento(string classDefaultJson)
    {
        // Act / Assert: il rifiuto è dichiarato e non silenzioso: senza una vista riconosciuta
        // l'ingresso non ha un punto di partenza coerente con §6.2.
        Assert.Throws<InvalidOperationException>(() => ParticipantClassDefault.ReadView(classDefaultJson));
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("")]
    [InlineData("   ")]
    public void ReadView_SenzaDocumento_RifiutaIlValore(string classDefaultJson)
    {
        // La colonna è obbligatoria: un valore vuoto è un uso improprio dell'API, non un default.
        Assert.Throws<ArgumentException>(() => ParticipantClassDefault.ReadView(classDefaultJson));
    }
}
