using System.Text.Json;
using Qwerty.Codi.Application.Dispatch;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Dispatch;

/// <summary>
/// Formato dei corpi del dispatcher (§10.2): i contatori a 64 bit viaggiano come stringhe decimali,
/// perché il numero JSON è un doppio e perderebbe cifre su valori grandi. Sono test unitari: non
/// aprono database e non toccano il disco.
/// </summary>
public sealed class CodiJsonPayloadTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_ConContatoreA64Bit_ScriveUnaStringaDecimale()
    {
        var json = JsonSerializer.Serialize(42L, CodiJson.Payload);

        Assert.Equal("\"42\"", json);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Serialize_ConValoreMassimo_NonPerdeCifre()
    {
        // Un numero JSON non rappresenta long.MaxValue senza perdere precisione: è il motivo per
        // cui il contratto lo vuole come stringa.
        var json = JsonSerializer.Serialize(long.MaxValue, CodiJson.Payload);

        Assert.Equal($"\"{long.MaxValue}\"", json);
        Assert.Equal(long.MaxValue, JsonSerializer.Deserialize<long>(json, CodiJson.Payload));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Deserialize_ConNumero_AccettaIlValore()
    {
        // Un client che manda un numero non deve ricevere un errore di deserializzazione oscuro:
        // la richiesta si legge, e sono le regole del caso d'uso a decidere l'esito.
        Assert.Equal(7L, JsonSerializer.Deserialize<long>("7", CodiJson.Payload));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Deserialize_ConStringaNonNumerica_RifiutaIlValore()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<long>("\"sette\"", CodiJson.Payload));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Deserialize_ConValoreAssente_LasciaNullIlContatoreFacoltativo()
    {
        // La versione attesa di §10.2 è facoltativa nel record: la sua assenza ha un codice
        // proprio, quindi il deserializzatore deve poterla distinguere da un corpo non valido.
        var request = JsonSerializer.Deserialize<TestPayload>(
            """{"name":"Scuola Manzoni"}""", CodiJson.Payload);

        Assert.NotNull(request);
        Assert.Null(request.BaseRowVersion);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Deserialize_ConContatoreComeStringa_LoAssegna()
    {
        var request = JsonSerializer.Deserialize<TestPayload>(
            """{"name":"Scuola Manzoni","baseRowVersion":"12"}""", CodiJson.Payload);

        Assert.Equal(12L, request!.BaseRowVersion);
    }

    private sealed record TestPayload(string Name, long? BaseRowVersion = null);
}
