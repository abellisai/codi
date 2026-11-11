namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Messaggi mostrati al bambino quando la corsa finisce (§13.2). Sono parte della definizione, e
/// non del client, perché sono la voce del docente: l'esercizio spiega l'esito con le sue parole.
/// </summary>
public sealed class ExerciseFeedback
{
    /// <summary>Lunghezza massima di un messaggio; scelta locale, non un requisito del dizionario.</summary>
    public const int MaxMessageLength = 500;

    private ExerciseFeedback(string success, string collision)
    {
        Success = success;
        Collision = collision;
    }

    /// <summary>Messaggio di esercizio risolto.</summary>
    public string Success { get; }

    /// <summary>Messaggio di corsa terminata contro un ostacolo (§13.2).</summary>
    public string Collision { get; }

    public static ExerciseFeedback Create(string success, string collision) =>
        new(EnsureMessage(success, nameof(success)), EnsureMessage(collision, nameof(collision)));

    private static string EnsureMessage(string message, string parameterName)
    {
        var trimmed = message?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Il messaggio di esito non può essere vuoto.", parameterName);
        }

        if (trimmed.Length > MaxMessageLength)
        {
            throw new ArgumentException(
                $"Il messaggio di esito supera i {MaxMessageLength} caratteri.", parameterName);
        }

        return trimmed;
    }
}
