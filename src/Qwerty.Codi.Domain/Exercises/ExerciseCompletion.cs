namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Condizione di completamento della definizione (§13.2): è la regola che decide quando l'esercizio
/// è risolto, ed è distinta dal traguardo disegnato nella scena perché un tipo di attività
/// successivo potrebbe concludersi per un motivo diverso dal trovarsi su una cella. Finché la sola
/// condizione è <see cref="ExerciseCompletionKind.ReachCell"/>, la definizione verifica che la
/// cella del completamento coincida con quella del traguardo: due valori che devono essere uguali
/// non vanno lasciati liberi di divergere.
/// </summary>
public sealed class ExerciseCompletion
{
    private ExerciseCompletion(ExerciseCompletionKind kind, GridPosition target)
    {
        Kind = kind;
        Target = target;
    }

    /// <summary>Regola che conclude la corsa con successo.</summary>
    public ExerciseCompletionKind Kind { get; }

    /// <summary>Cella che soddisfa la condizione.</summary>
    public GridPosition Target { get; }

    public static ExerciseCompletion Create(ExerciseCompletionKind kind, GridPosition target)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind), kind, "La condizione di completamento non è una di quelle previste.");
        }

        return new ExerciseCompletion(kind, target);
    }
}
