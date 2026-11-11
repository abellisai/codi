namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Condizione che conclude un esercizio (§13.2). Per ora ne esiste una sola: raggiungere una cella.
/// L'enumerazione esiste perché la definizione la dichiara, e aggiungerne una è una nuova versione
/// del motore e non un campo interpretato diversamente dalla stessa versione.
/// </summary>
public enum ExerciseCompletionKind
{
    /// <summary>La corsa termina con successo quando il personaggio entra nella cella indicata.</summary>
    ReachCell,
}
