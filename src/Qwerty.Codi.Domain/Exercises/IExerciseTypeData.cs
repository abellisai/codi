namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Dati didattici propri di un tipo di esercizio. Sono la parte della definizione che non si può
/// descrivere in modo generale: una griglia con partenza, ostacoli e traguardo per <c>GridPath</c>,
/// una rappresentazione da leggere e una risposta attesa per un tipo dichiarativo.
///
/// Un tipo di esercizio porta la propria implementazione, e il registro dei motori è l'unico punto
/// che sa quale implementazione corrisponde a una versione del motore: chi legge una definizione
/// non deve conoscere i tipi esistenti, altrimenti ogni tipo nuovo costringerebbe a modificare
/// ogni consumatore.
/// </summary>
public interface IExerciseTypeData
{
}
