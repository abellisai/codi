namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Registro delle versioni del motore conosciute da questa build.
///
/// Le versioni dichiarate una volta non si tolgono: un esercizio pubblicato le porta con sé e
/// deve continuare a essere interpretabile. Un tipo o una versione ripetuti sarebbero un errore di
/// programmazione, e si scoprono alla costruzione del registro, non alla prima pubblicazione.
/// </summary>
public static class ExerciseEngineRegistry
{
    private static readonly IReadOnlyList<IExerciseEngineDescriptor> Engines =
    [
        new GridPathExerciseData(),
        new QuizExerciseData(),
        new ValueProgramExerciseData(),
    ];

    /// <summary>Descrizioni registrate, nell'ordine di registrazione.</summary>
    public static IReadOnlyList<IExerciseEngineDescriptor> Registered => Engines;

    /// <summary>Versioni conosciute, nell'ordine di registrazione: la più vecchia per prima.</summary>
    public static IReadOnlyList<string> KnownVersions { get; } = [.. Engines.Select(engine => engine.Version)];

    /// <summary>
    /// Tipi di esercizio che questa build sa svolgere, distinti e nell'ordine di registrazione.
    /// Un tipo può avere più versioni: l'elenco è dei tipi, non delle versioni.
    /// </summary>
    public static IReadOnlyList<string> KnownExerciseTypes { get; } =
        [.. Engines.Select(engine => engine.ExerciseType).Distinct(StringComparer.Ordinal)];

    /// <summary>Descrizione di un tipo, alla sua versione più recente registrata.</summary>
    public static IExerciseEngineDescriptor? FindType(string? exerciseType) =>
        exerciseType is null
            ? null
            : Engines.LastOrDefault(
                engine => string.Equals(engine.ExerciseType, exerciseType, StringComparison.Ordinal));

    /// <summary>
    /// Descrizione della versione dichiarata. Restituisce <c>null</c> per una versione che questa
    /// build non conosce: una versione futura non viene interpretata come se fosse quella corrente.
    /// </summary>
    public static IExerciseEngineDescriptor? Find(string? engineVersion) =>
        engineVersion is null
            ? null
            : Engines.FirstOrDefault(
                engine => string.Equals(engine.Version, engineVersion, StringComparison.Ordinal));

    /// <summary>
    /// Descrizione richiesta, o <see cref="ArgumentException"/> se la versione non è conosciuta.
    /// Il messaggio elenca ciò che questa build sa interpretare: chi corregge una definizione deve
    /// sapere che cosa può dichiarare.
    /// </summary>
    public static IExerciseEngineDescriptor Require(string? engineVersion, string parameterName) =>
        Find(engineVersion)
        ?? throw new ArgumentException(
            $"La versione del motore «{engineVersion}» non è fra quelle conosciute ({string.Join(", ", KnownVersions)}).",
            parameterName);
}
