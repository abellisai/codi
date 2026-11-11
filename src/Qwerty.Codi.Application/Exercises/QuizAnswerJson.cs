using System.Text.Json;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Domain.Exercises;

namespace Qwerty.Codi.Application.Exercises;

/// <summary>
/// Il lavoro di un alunno su un esercizio dichiarativo: **quale** risposta ha dato. Non è un
/// progetto a blocchi e non ha una forma di Blockly, ma è il lavoro del tentativo come l'altro — per
/// questo vive nello stesso campo del database e viene verificato, non interpretato.
///
/// Il formato è minimo e stabile: <c>{ "answer": "testo" }</c>. La risposta è una stringa anche
/// quando rappresenta un numero, perché è quello che il bambino ha digitato: il confronto numerico
/// appartiene alla definizione dell'esercizio, non al formato del verbale.
/// </summary>
public static class QuizAnswerJson
{
    /// <summary>Nome della proprietà che porta la risposta.</summary>
    public const string AnswerProperty = "answer";

    /// <summary>Un verbale senza risposta: è il lavoro iniziale del tentativo, prima che l'alunno scriva.</summary>
    public static string Empty => "{\"answer\":\"\"}";

    /// <summary>
    /// Verifica il verbale di risposta: un oggetto JSON con la sola proprietà della risposta, che
    /// deve essere una stringa entro il limite. Un campo in più è un formato che non conosciamo.
    /// </summary>
    public static string Validate(
        string? json,
        QuizData data,
        string? referenceSolutionJson,
        string requestName,
        string label)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new CodiRequestException(CodiErrorCodes.ValidationFailed, $"Manca {label}.");
        }

        if (json.Length > ExerciseWorkspaceJson.MaxLength)
        {
            throw new CodiRequestException(
                CodiErrorCodes.PayloadTooLarge, $"{Capitalize(label)} supera i {ExerciseWorkspaceJson.MaxLength} caratteri.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException failure)
        {
            throw CodiRequestException.InvalidPayload(requestName, failure);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new CodiRequestException(
                    CodiErrorCodes.ValidationFailed, $"{Capitalize(label)} deve essere un oggetto JSON.");
            }

            foreach (var property in root.EnumerateObject())
            {
                if (!string.Equals(property.Name, AnswerProperty, StringComparison.Ordinal))
                {
                    throw new CodiRequestException(
                        CodiErrorCodes.ValidationFailed,
                        $"{Capitalize(label)} ha una proprietà «{property.Name}» che non è prevista.");
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new CodiRequestException(
                        CodiErrorCodes.ValidationFailed,
                        $"La risposta in {label} deve essere testo.");
                }

                if ((property.Value.GetString() ?? string.Empty).Length > QuizData.MaxAnswerLength)
                {
                    throw new CodiRequestException(
                        CodiErrorCodes.ValidationFailed,
                        $"La risposta supera i {QuizData.MaxAnswerLength} caratteri.");
                }
            }
        }

        return json;
    }

    /// <summary>Legge la risposta dal verbale; una stringa vuota o assente è «non ha ancora risposto».</summary>
    public static string Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(AnswerProperty, out var answer)
                && answer.ValueKind == JsonValueKind.String
                ? answer.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Legge la risposta attesa dalla soluzione privata della versione, che ha la stessa forma del
    /// verbale. Restituisce <c>null</c> quando la versione non ne ha una: senza soluzione il server
    /// non può riconoscere una risposta corretta, e lo dice invece di dichiararla giusta o sbagliata
    /// a caso.
    /// </summary>
    public static string? ReadExpected(string? referenceSolutionJson)
    {
        var expected = Read(referenceSolutionJson);

        return expected.Length == 0 ? null : expected;
    }

    private static string Capitalize(string label) =>
        label.Length == 0 ? label : char.ToUpperInvariant(label[0]) + label[1..];
}
