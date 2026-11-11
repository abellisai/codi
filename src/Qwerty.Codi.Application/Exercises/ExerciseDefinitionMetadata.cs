using System.Text.Json;
using Qwerty.Codi.Domain.Exercises;

namespace Qwerty.Codi.Application.Exercises;

/// <summary>
/// Lettura dei metadati tecnici che il documento di definizione (§13.2) dichiara accanto alla parte
/// didattica: il tipo di esercizio e la versione del motore.
///
/// Serve a due consumatori che non devono interpretare la definizione intera: i DTO che espongono la
/// versione pubblicata, che il client usa per sapere **che cosa** sta per ricevere, e i casi in cui
/// il documento è un archivio opaco — per esempio una versione di prova con una definizione minima —
/// e va lasciato com'è.
///
/// La lettura è tollerante di proposito: un documento che non è JSON, o che non dichiara il tipo,
/// restituisce <c>null</c> invece di sollevare. Chi ha bisogno del documento **valido** usa
/// <see cref="ExerciseDefinitionJson.Parse"/>: qui si sta leggendo un'etichetta, non una regola.
/// </summary>
public static class ExerciseDefinitionMetadata
{
    /// <summary>Tipo di esercizio dichiarato dal documento, oppure <c>null</c> se non è leggibile.</summary>
    public static string? ExerciseType(string? definitionJson)
    {
        var value = Read(definitionJson, "exerciseType");

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Versione del motore dichiarata dal documento, oppure <c>null</c> se non è leggibile.</summary>
    public static string? EngineVersion(string? definitionJson)
    {
        var value = Read(definitionJson, "engineVersion");

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Tipo di esercizio del documento, con la versione del motore come ripiego: un documento che
    /// non dichiara il tipo ma dichiara una versione conosciuta appartiene comunque a un tipo, e la
    /// descrizione della versione lo sa. Serve ai percorsi che leggono una definizione scritta prima
    /// che il tipo diventasse un campo esplicito.
    /// </summary>
    public static string? ResolveExerciseType(string? definitionJson) =>
        ExerciseType(definitionJson)
        ?? ExerciseEngineRegistry.Find(EngineVersion(definitionJson))?.ExerciseType;

    private static string? Read(string? definitionJson, string property)
    {
        if (string.IsNullOrWhiteSpace(definitionJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(definitionJson);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(property, out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
