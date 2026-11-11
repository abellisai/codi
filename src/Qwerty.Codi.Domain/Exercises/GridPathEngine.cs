namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Regole della prima versione del motore degli esercizi, <c>gridpath-1</c> (§13.1, §13.2).
/// Sono **una versione** e non «le regole attuali»: una versione pubblicata porta con sé la propria
/// <see cref="Version"/> e continua a essere interpretata con queste regole anche quando ne
/// esisterà una più grande con più blocchi.
///
/// Regole di esecuzione di <c>gridpath-1</c>:
/// <list type="bullet">
/// <item>un passo fuori dalla griglia o su un ostacolo termina la corsa con esito didattico di
/// errore e mantiene la posizione precedente;</item>
/// <item>il raggiungimento del traguardo conclude la corsa con successo e il resto del programma
/// non viene eseguito;</item>
/// <item>le rotazioni cambiano soltanto l'orientamento.</item>
/// </list>
/// </summary>
public static class GridPathEngine
{
    /// <summary>Tipo di esercizio dichiarato nella definizione (§13.2).</summary>
    public const string ExerciseType = "GridPath";

    /// <summary>Versione del motore di questa prima forma di esercizio.</summary>
    public const string Version = "gridpath-1";

    /// <summary>Versione dello schema di definizione comprensibile da questa versione del motore.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Lato minimo della griglia (§13.2).</summary>
    public const int MinSide = 2;

    /// <summary>Lato massimo della griglia (§13.2).</summary>
    public const int MaxSide = 20;

    /// <summary>Conteggio minimo di <c>aula_repeat</c> (§13.4).</summary>
    public const int MinRepeatCount = 1;

    /// <summary>Conteggio massimo di <c>aula_repeat</c> (§13.4).</summary>
    public const int MaxRepeatCount = 20;

    // Le versioni conosciute da questa build non sono più un elenco qui: vivono nel registro dei
    // motori (ExerciseEngineRegistry), che è l'unico punto da cui si sa che cosa si sa interpretare.
    // Questo tipo resta la dichiarazione delle regole di gridpath-1.
}
