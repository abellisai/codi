namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Regole del tipo dichiarativo, <c>quiz-1</c> (§13.9).
///
/// L'alunno legge una rappresentazione, produce una risposta e riceve un esito. Non c'è un
/// personaggio da guidare, non c'è un progetto a blocchi e non c'è un'esecuzione da sospendere: per
/// questo il tipo non appartiene al player e non produce checkpoint. La sua versione del motore
/// dichiara uno schema di checkpoint che non usa, perché il registro ne chiede uno a ogni versione.
/// </summary>
public static class QuizEngine
{
    /// <summary>Tipo di esercizio dichiarato nella definizione.</summary>
    public const string ExerciseType = "Quiz";

    /// <summary>Versione del motore di questa prima forma dichiarativa.</summary>
    public const string Version = "quiz-1";

    /// <summary>Versione dello schema di definizione compresa da questa versione del motore.</summary>
    public const int SchemaVersion = 1;
}
