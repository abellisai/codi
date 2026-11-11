namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Nomi dei blocchi di <c>valueprogram-1</c> (§13.9).
///
/// Il catalogo è per versione del motore, e questi nomi sono di questa versione. Stanno in una classe
/// propria e non in <see cref="ExerciseBlockCatalog"/> perché quello conserva i nomi di
/// <c>gridpath-1</c>: mescolarli farebbe sembrare che un blocco valga per entrambi i motori, e non è
/// così. Il percorso ha un personaggio da muovere, il programma di valori ha variabili da riempire:
/// sono due vocabolari diversi.
///
/// I blocchi si dividono in due famiglie, e la distinzione conta: i **comandi** stanno in una catena
/// e si eseguono in ordine, le **espressioni** si annidano dentro i comandi e producono un valore. Un
/// blocco di espressione non può aprire una catena, e un comando non può stare dentro un valore.
/// </summary>
public static class ValueBlockCatalog
{
    // ---------------------------------------------------------------- comandi

    /// <summary>Punto di ingresso: apre la catena eseguibile.</summary>
    public const string Start = "val_start";

    /// <summary>Assegna una variabile: «porta … a …».</summary>
    public const string Set = "val_set";

    /// <summary>
    /// Cambia una variabile di una quantità: «cambia Punti di 1». È il blocco con cui il corso conta,
    /// e non è una scorciatoia per «porta Punti a Punti + 1»: dice l'operazione senza ripetere il nome
    /// della variabile, che è la forma in cui un bambino la sbaglia.
    /// </summary>
    public const string Change = "val_change";

    /// <summary>Mostra un valore: è l'uscita visibile del programma, il «dire» del corso.</summary>
    public const string Show = "val_show";

    /// <summary>
    /// Legge un valore e lo mette in una variabile: è il blocco che fa attendere la corsa. Nel corso
    /// dice «chiedi [domanda?] e attendi», e la risposta si usa con il reporter <see cref="Answer"/>.
    /// </summary>
    public const string Ask = "val_ask";

    /// <summary>Esegue una sequenza se il confronto è vero, e un'altra altrimenti.</summary>
    public const string If = "val_if";

    /// <summary>Ripete una sequenza un numero fisso di volte.</summary>
    public const string Repeat = "val_repeat";

    // ---------------------------------------------------------------- espressioni

    /// <summary>Un numero scritto nel programma.</summary>
    public const string Number = "val_number";

    /// <summary>Il contenuto di una variabile.</summary>
    public const string Variable = "val_variable";

    /// <summary>
    /// La risposta all'ultima domanda: è il reporter «risposta» del corso, e non è una variabile del
    /// docente. Sta a parte perché chiedere di nuovo la sostituisce, mentre le variabili conservano i
    /// valori memorizzati — è la differenza che l'esercizio della somma mette alla prova.
    /// </summary>
    public const string Answer = "val_answer";

    /// <summary>La somma di due valori.</summary>
    public const string Add = "val_add";

    /// <summary>Un confronto fra due valori: decide un ramo, non produce un numero da mostrare.</summary>
    public const string Compare = "val_compare";

    // ---------------------------------------------------------------- campi

    /// <summary>Campo con il nome della variabile assegnata, mostrata o riempita.</summary>
    public const string VariableField = "VAR";

    /// <summary>Campo con la domanda che «chiedi … e attendi» mostra mentre aspetta.</summary>
    public const string QuestionField = "QUESTION";

    /// <summary>Campo con la quantità di «cambia … di …».</summary>
    public const string ChangeField = "DELTA";

    /// <summary>Campo con il numero scritto in un blocco numerico.</summary>
    public const string NumberField = "NUM";

    /// <summary>Campo con il numero di ripetizioni di un ciclo contato.</summary>
    public const string RepeatCountField = "COUNT";

    /// <summary>Campo con la relazione di un confronto.</summary>
    public const string CompareField = "OP";

    // ---------------------------------------------------------------- innesti

    /// <summary>Ingresso con il valore da assegnare, mostrare o sommare.</summary>
    public const string ValueInput = "VALUE";

    /// <summary>Secondo valore di una somma o di un confronto.</summary>
    public const string OtherInput = "OTHER";

    /// <summary>Ingresso con la sequenza eseguita quando il confronto è vero.</summary>
    public const string ThenInput = "DO";

    /// <summary>Ingresso con la sequenza eseguita quando il confronto è falso.</summary>
    public const string ElseInput = "ELSE";

    /// <summary>Ingresso con la sequenza ripetuta di un ciclo contato.</summary>
    public const string RepeatBodyInput = "DO";

    /// <summary>Relazioni ammesse in un confronto, nell'ordine in cui si offrono.</summary>
    public static readonly IReadOnlyList<string> ComparisonValues =
        [nameof(ComparisonOperator.Greater), nameof(ComparisonOperator.Less), nameof(ComparisonOperator.Equal)];

    /// <summary>
    /// Comandi del catalogo: si concatenano in una catena e si eseguono in ordine. Sono i blocchi che
    /// possono stare sotto il punto di ingresso.
    /// </summary>
    public static readonly IReadOnlyList<string> Commands =
        [Start, Set, Change, Show, Ask, If, Repeat];

    /// <summary>
    /// Espressioni del catalogo: si annidano dentro un ingresso di valore e non aprono una catena.
    /// Sono i «reporter» del corso, che si inseriscono nei fori dei blocchi.
    /// </summary>
    public static readonly IReadOnlyList<string> Expressions =
        [Number, Variable, Answer, Add, Compare];

    /// <summary>Vero se il blocco è un comando di questa versione.</summary>
    public static bool IsCommand(string? blockType) =>
        blockType is not null && Commands.Contains(blockType, StringComparer.Ordinal);

    /// <summary>Vero se il blocco è un'espressione di questa versione.</summary>
    public static bool IsExpression(string? blockType) =>
        blockType is not null && Expressions.Contains(blockType, StringComparer.Ordinal);
}
