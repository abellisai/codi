namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Un'istruzione del programma didattico. È la rappresentazione che il motore esegue, distinta dal
/// progetto a blocchi (che è la forma con cui l'alunno lo compone) e dalla definizione (che dice le
/// regole): sono tre formati diversi e non vanno scambiati (§13.3).
///
/// Ogni istruzione conserva l'identificativo del blocco da cui nasce, così la scena può evidenziare
/// il blocco in esecuzione.
/// </summary>
public abstract record ValueInstruction(string SourceBlockId);

/// <summary>Assegna a una variabile il risultato di un'espressione.</summary>
public sealed record SetVariable(string SourceBlockId, string Variable, ValueExpression Value)
    : ValueInstruction(SourceBlockId);

/// <summary>
/// **Cambia** una variabile di una quantità: legge il valore, lo somma e lo riscrive.
///
/// È un'istruzione propria e non una scorciatoia per «porta la variabile a se stessa più uno», perché
/// nel corso è un altro blocco e si legge in un altro modo: «cambia Punti di 1» dice l'operazione
/// senza ripetere il nome della variabile due volte, che è la forma in cui un bambino la sbaglia.
/// </summary>
public sealed record ChangeVariable(string SourceBlockId, string Variable, ValueExpression Delta)
    : ValueInstruction(SourceBlockId);

/// <summary>Mostra un'espressione: è l'uscita visibile del programma, il «dire» del corso.</summary>
public sealed record ShowValue(string SourceBlockId, ValueExpression Value)
    : ValueInstruction(SourceBlockId);

/// <summary>
/// Legge un valore dal primo parametro non ancora consumato. È l'istruzione che fa **fermare** la
/// corsa quando il valore non c'è ancora: la corsa riprenderà da qui, non dall'inizio.
///
/// <see cref="Question"/> è la domanda che l'alunno legge mentre il programma aspetta — «Numero della
/// carta?» — e serve alla pagina per dire che cosa si sta aspettando. Il motore non la usa per
/// eseguire, ma non è un dettaglio della scena: senza, il campo del valore non direbbe che cosa
/// chiedere, e il bambino vedrebbe una richiesta muta.
///
/// <see cref="Variable"/> è la variabile in cui il valore va conservato, quando c'è. Nel corso il
/// blocco dice soltanto «chiedi … e attendi» e la risposta entra in un secondo momento con «porta
/// Numero a (risposta)»; la forma con la variabile resta perché è quella del primo esercizio, dove
/// chiedere e conservare erano un blocco solo.
/// </summary>
public sealed record AskValue(string SourceBlockId, string? Variable, string Question = "")
    : ValueInstruction(SourceBlockId);

/// <summary>
/// Esegue una sequenza se il confronto è vero. <see cref="Otherwise"/> è la sequenza del ramo
/// «altrimenti», vuota quando l'istruzione è un semplice «se».
/// </summary>
public sealed record IfElse(
    string SourceBlockId,
    ValueComparison Condition,
    IReadOnlyList<ValueInstruction> Then,
    IReadOnlyList<ValueInstruction> Otherwise)
    : ValueInstruction(SourceBlockId);

/// <summary>Ripete una sequenza un numero fisso di volte.</summary>
public sealed record RepeatTimes(
    string SourceBlockId,
    int Count,
    IReadOnlyList<ValueInstruction> Body)
    : ValueInstruction(SourceBlockId);
