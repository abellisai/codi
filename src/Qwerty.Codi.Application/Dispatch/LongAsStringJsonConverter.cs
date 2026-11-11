using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Qwerty.Codi.Application.Dispatch;

/// <summary>
/// Contatori a 64 bit rappresentati come stringhe decimali (§10.2). Un intero a 64 bit non
/// sopravvive al passaggio per il tipo <c>number</c> di JavaScript, che è un doppio: una versione
/// di riga o un contatore di sessione grandi perderebbero cifre in silenzio. La lettura accetta
/// anche un numero, perché un client non allineato deve poter ricevere il rifiuto del contratto e
/// non un errore di deserializzazione oscuro.
/// </summary>
internal sealed class LongAsStringJsonConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String when long.TryParse(
                reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            JsonTokenType.Number when reader.TryGetInt64(out var number) => number,
            _ => throw new JsonException("Atteso un intero a 64 bit come stringa decimale."),
        };

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}
