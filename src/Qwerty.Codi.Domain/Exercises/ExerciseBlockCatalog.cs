namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Nomi dei blocchi del catalogo (§13.4).
///
/// Il catalogo è **per versione del motore**: pubblicando un esercizio si fissa la versione, e da
/// quel momento il catalogo che lo riguarda è il suo. Aggiungere blocchi significa una nuova
/// versione del motore, non l'allargamento silenzioso di quella esistente, che cambierebbe il
/// significato di un esercizio già pubblicato.
///
/// Le costanti qui sotto sono i nomi dei blocchi di <c>gridpath-1</c>. Le versioni disponibili e la
/// loro composizione vivono nel registro (<see cref="ExerciseEngineRegistry"/>): questa classe
/// conserva i nomi, che il dominio, il validatore del progetto e l'editor condividono.
/// </summary>
public static class ExerciseBlockCatalog
{
    /// <summary>Punto di ingresso eseguibile: senza di lui il programma non ha nulla da eseguire.</summary>
    public const string CodiStart = "aula_start";

    /// <summary>Un passo nella direzione corrente.</summary>
    public const string CodiMove = "aula_move";

    /// <summary>Rotazione di 90 gradi a sinistra o a destra.</summary>
    public const string CodiTurn = "aula_turn";

    /// <summary>Ripetizione di una sequenza interna, da 1 a 20 volte (§13.4).</summary>
    public const string CodiRepeat = "aula_repeat";

    /// <summary>Campo del verso di rotazione di <c>aula_turn</c> (§13.4).</summary>
    public const string TurnField = "TURN";

    /// <summary>Campo con il numero di ripetizioni di <c>aula_repeat</c> (§13.4).</summary>
    public const string RepeatCountField = "COUNT";

    /// <summary>Ingresso che contiene la sequenza ripetuta di <c>aula_repeat</c> (§13.4).</summary>
    public const string RepeatBodyInput = "DO";

    /// <summary>Blocchi ammessi da una versione del motore; vuoto se la versione è sconosciuta.</summary>
    public static IReadOnlyList<string> ForEngine(string? engineVersion) =>
        ExerciseEngineRegistry.Find(engineVersion)?.Blocks.Select(block => block.Type).ToArray() ?? [];

    /// <summary>
    /// Descrittore della versione dichiarata, oppure <c>null</c> se questa build non la conosce.
    /// È il punto da cui i consumatori prendono blocchi, campi e punto di ingresso senza conoscere
    /// l'elenco dei tipi di esercizio.
    /// </summary>
    public static IExerciseEngineDescriptor? Descriptor(string? engineVersion) =>
        ExerciseEngineRegistry.Find(engineVersion);
}
