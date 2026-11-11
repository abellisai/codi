namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Orientamento del personaggio (§13.2). Le rotazioni cambiano soltanto l'orientamento e mai la
/// posizione. L'ordine dei valori è quello di rotazione oraria, così girare a destra o a sinistra
/// è un passo di uno in avanti o all'indietro nell'enumerazione e non serve una tabella di
/// corrispondenze che potrebbe divergere fra i due interpreti di §13.8.
/// </summary>
public enum GridDirection
{
    North = 0,
    East = 1,
    South = 2,
    West = 3,
}
