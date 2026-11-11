using System.Security.Cryptography;
using System.Text;

namespace Qwerty.Codi.Application.Exercises;

/// <summary>
/// Impronta del contenuto di una versione (§13.8): definizione canonica, progetto iniziale e
/// versioni del motore. Serve a riconoscere una pubblicazione ripetuta — stessa impronta e stessa
/// soluzione privata significano la stessa versione — e a dare al docente un riferimento breve per
/// confrontare due versioni senza leggere i documenti.
///
/// Il calcolo è deterministico: stesso contenuto, stessa impronta. I separatori impediscono che due
/// documenti concatenati in modo diverso producano lo stesso testo, e l'impronta è esadecimale
/// minuscola, come le altre impronte dello schema.
/// </summary>
public static class ExerciseContentHash
{
    /// <summary>Impronta SHA-256 del contenuto ricevuto, nella forma conservata dalla versione.</summary>
    public static string Compute(
        string definitionJson,
        string workspaceJson,
        string engineVersion,
        string schemaVersion)
    {
        ArgumentNullException.ThrowIfNull(definitionJson);
        ArgumentNullException.ThrowIfNull(workspaceJson);
        ArgumentNullException.ThrowIfNull(engineVersion);
        ArgumentNullException.ThrowIfNull(schemaVersion);

        var text = string.Join('\n', definitionJson, workspaceJson, engineVersion, schemaVersion);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>
    /// Impronta del **solo progetto**: è quella che un tentativo porta con sé, e serve a riconoscere
    /// un salvataggio ripetuto — stesso progetto, stessa impronta — senza dipendere dalla definizione
    /// dell'esercizio, che l'alunno non cambia (§7.3, §15.2).
    /// </summary>
    public static string ComputeWorkspace(string workspaceJson)
    {
        ArgumentNullException.ThrowIfNull(workspaceJson);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(workspaceJson)));
    }
}
