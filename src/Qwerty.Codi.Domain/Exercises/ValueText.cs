namespace Qwerty.Codi.Domain.Exercises;

/// <summary>Come si scrive il nome di una variabile di un esercizio.</summary>
public static class ValueText
{
    /// <summary>
    /// Vero se il nome è utilizzabile: una lettera iniziale, poi lettere, cifre o trattino basso.
    /// Un nome con spazi o simboli non si può mostrare in un blocco senza confondere un bambino di
    /// otto anni, che legge il nome per riconoscere la variabile.
    /// </summary>
    public static bool IsUsableName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > ValueProgramEngine.MaxVariableNameLength)
        {
            return false;
        }

        if (!char.IsLetter(name[0]))
        {
            return false;
        }

        return name.All(character => char.IsLetterOrDigit(character) || character == '_');
    }

    /// <summary>Confronto fra nomi di variabile: il programma distingue maiuscole e minuscole.</summary>
    public static bool SameName(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);
}
