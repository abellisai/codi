namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Regole della versione <c>valueprogram-1</c> (§13.9): un programma che **conserva valori**.
///
/// È il secondo modello di esecuzione del prodotto, e non un percorso più grande. In
/// <c>gridpath-1</c> lo stato è dove si trova un personaggio su una griglia e una corsa finisce al
/// traguardo; qui lo stato è un insieme di variabili con il loro valore, l'esito è quello che il
/// programma **mostra**, e la corsa può fermarsi **in attesa di un valore** che l'alunno deve
/// fornire. Il traguardo non esiste: non c'è una cella da raggiungere, c'è un risultato da
/// calcolare.
/// </summary>
public static class ValueProgramEngine
{
    /// <summary>Tipo di esercizio dichiarato nella definizione.</summary>
    public const string ExerciseType = "ValueProgram";

    /// <summary>Versione del motore di questa prima forma.</summary>
    public const string Version = "valueprogram-1";

    /// <summary>Versione dello schema di definizione compresa da questa versione del motore.</summary>
    public const int SchemaVersion = 1;

    /// <summary>
    /// Versione dello schema del checkpoint. È **1 di questa versione del motore**, non un numero
    /// globale: il checkpoint di <c>gridpath-1</c> è un altro formato, con altri campi, e i due non
    /// si scambiano.
    /// </summary>
    public const int CheckpointSchemaVersion = 1;

    /// <summary>Numero massimo di variabili di un esercizio.</summary>
    public const int MaxVariables = 12;

    /// <summary>Lunghezza massima del nome di una variabile.</summary>
    public const int MaxVariableNameLength = 20;

    /// <summary>Lunghezza massima del testo mostrato con un'uscita.</summary>
    public const int MaxTextLength = 200;

    /// <summary>Numero massimo di parametri di ingresso di un esercizio.</summary>
    public const int MaxParameters = 4;

    /// <summary>Valore intero più grande ammesso in una variabile o in un confronto.</summary>
    public const int MaxInteger = 1_000_000;

    /// <summary>Conteggio minimo e massimo di un ciclo contato.</summary>
    public const int MinRepeatCount = 1;
    public const int MaxRepeatCount = 50;
}
