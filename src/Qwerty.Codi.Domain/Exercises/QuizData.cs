namespace Qwerty.Codi.Domain.Exercises;

/// <summary>Modo di confronto fra la risposta dell'alunno e quella attesa.</summary>
public enum QuizAnswerMatch
{
    /// <summary>Testo confrontato senza spazi ai bordi e senza distinzione di maiuscole.</summary>
    Text,

    /// <summary>Numero intero: «07» e «7» sono la stessa risposta.</summary>
    Integer,
}

/// <summary>
/// Dati didattici del tipo dichiarativo: che cosa l'alunno legge, che cosa deve produrre e come si
/// riconosce una risposta corretta.
///
/// Non contiene la risposta attesa. Quella vive nella soluzione privata della versione pubblicata,
/// che non esce mai verso gli alunni (§13.1): se stesse qui, il documento che il browser riceve
/// per mostrare l'esercizio conterrebbe la soluzione, e la correzione sarebbe una formalità.
/// </summary>
public sealed class QuizData : IExerciseTypeData
{
    /// <summary>Lunghezza massima della rappresentazione mostrata e della domanda.</summary>
    public const int MaxTextLength = 4000;

    /// <summary>Lunghezza massima di una risposta attesa.</summary>
    public const int MaxAnswerLength = 200;

    private QuizData(string stimulus, string prompt, QuizAnswerMatch match, ExerciseFeedback feedback)
    {
        Stimulus = stimulus;
        Prompt = prompt;
        Match = match;
        Feedback = feedback;
    }

    /// <summary>Rappresentazione da leggere: un numero binario, una somma, una scena descritta.</summary>
    public string Stimulus { get; }

    /// <summary>Che cosa deve produrre l'alunno.</summary>
    public string Prompt { get; }

    /// <summary>Come si confronta la risposta.</summary>
    public QuizAnswerMatch Match { get; }

    /// <summary>Messaggi di esito mostrati al bambino: risposta corretta e risposta sbagliata.</summary>
    public ExerciseFeedback Feedback { get; }

    public static QuizData Create(
        string stimulus,
        string prompt,
        QuizAnswerMatch match,
        ExerciseFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(feedback);

        if (!Enum.IsDefined(match))
        {
            throw new ArgumentOutOfRangeException(
                nameof(match), match, "Il modo di confronto della risposta non è uno di quelli previsti.");
        }

        return new QuizData(
            EnsureText(stimulus, nameof(stimulus), "La rappresentazione da leggere"),
            EnsureText(prompt, nameof(prompt), "La domanda"),
            match,
            feedback);
    }

    /// <summary>
    /// Confronta la risposta dell'alunno con quella attesa secondo il modo dichiarato. Il confronto
    /// vive nel dominio perché è la regola dell'esercizio, non una decisione di chi lo disegna.
    /// </summary>
    public bool Accepts(string? answer, string? expected)
    {
        var given = answer?.Trim() ?? string.Empty;
        var wanted = expected?.Trim() ?? string.Empty;

        if (given.Length == 0 || wanted.Length == 0)
        {
            return false;
        }

        if (Match == QuizAnswerMatch.Integer)
        {
            return long.TryParse(given, out var givenNumber)
                && long.TryParse(wanted, out var wantedNumber)
                && givenNumber == wantedNumber;
        }

        return string.Equals(given, wanted, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureText(string? value, string parameterName, string what)
    {
        var trimmed = value?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            throw new ArgumentException($"{what} non può essere vuota.", parameterName);
        }

        if (trimmed.Length > MaxTextLength)
        {
            throw new ArgumentException($"{what} supera i {MaxTextLength} caratteri.", parameterName);
        }

        return trimmed;
    }
}
