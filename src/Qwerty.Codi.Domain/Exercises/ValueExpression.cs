namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Un'espressione che produce un valore: un numero scritto, il contenuto di una variabile, la
/// **risposta** dell'alunno, oppure una somma. Le espressioni si **annidano** dentro le istruzioni,
/// ed è la novità che distingue questo motore dal percorso: là un blocco conteneva soltanto altri
/// blocchi di istruzione, qui un blocco di comando contiene un'espressione, che a sua volta ne
/// contiene altre.
///
/// Sono i «reporter» del corso, che si inseriscono nei fori dei blocchi: `risposta` e il nome di una
/// variabile sono due reporter distinti, e trattarli come lo stesso li confonderebbe — la risposta
/// non è una variabile che il docente dichiara, è ciò che l'ultima lettura ha prodotto.
/// </summary>
public abstract record ValueExpression;

/// <summary>Un numero scritto nel programma.</summary>
public sealed record LiteralValue(int Value) : ValueExpression;

/// <summary>Il contenuto di una variabile, letto al momento dell'esecuzione.</summary>
public sealed record VariableValue(string Variable) : ValueExpression;

/// <summary>
/// La **risposta** all'ultima domanda letta. Nel corso è il reporter «risposta», e non è una variabile
/// del docente: esiste da quando il programma ha chiesto un valore, e chiedere di nuovo la sostituisce
/// mantenendo ciò che le variabili avevano già memorizzato. Per questo il motore la tiene a parte
/// dall'ambiente delle variabili: confonderle renderebbe impossibile l'esercizio della somma, dove
/// «A» e «B» conservano i due valori mentre la risposta cambia.
/// </summary>
public sealed record AnswerValue : ValueExpression;

/// <summary>La somma di due espressioni.</summary>
public sealed record AddValue(ValueExpression Left, ValueExpression Right) : ValueExpression;

/// <summary>
/// Un confronto fra due espressioni: non produce un numero, decide un ramo. È distinto dalle
/// espressioni di valore perché non può essere sommato né mostrato: un programma che mostra un
/// confronto non vuol dire nulla.
/// </summary>
public sealed record ValueComparison(ValueExpression Left, ComparisonOperator Operator, ValueExpression Right);

/// <summary>Le relazioni che il motore sa decidere.</summary>
public enum ComparisonOperator
{
    /// <summary>Maggiore.</summary>
    Greater,

    /// <summary>Minore.</summary>
    Less,

    /// <summary>Uguale.</summary>
    Equal,
}
