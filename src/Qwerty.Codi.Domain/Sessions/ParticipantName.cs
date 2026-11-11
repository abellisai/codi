using System.Globalization;

namespace Qwerty.Codi.Domain.Sessions;

/// <summary>
/// Nome visualizzato di un partecipante (§7.4). Non è una chiave: nomi uguali sono ammessi
/// (§5.2) e non esiste una forma normalizzata da confrontare. L'unica modifica ammessa è togliere
/// gli spazi ai bordi, perché il nome lo legge un bambino e riscriverne gli spazi interni
/// cambierebbe ciò che ha scritto.
/// </summary>
public static class ParticipantName
{
    /// <summary>
    /// Limite in **grafemi** e non in caratteri (§7.4): «Anna Maria» sono dieci, un'emoji uno solo,
    /// e una lettera accentata scritta in forma scomposta resta una lettera.
    /// </summary>
    public const int MaxGraphemes = 30;

    /// <summary>Vero se il testo può essere il nome di un partecipante.</summary>
    public static bool IsUsable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            return false;
        }

        var trimmed = value.Trim();
        var graphemes = GraphemeCount(trimmed);

        return graphemes >= 1 && graphemes <= MaxGraphemes;
    }

    /// <summary>Testo che il dominio conserva e la persistenza scrive: senza spazi ai bordi.</summary>
    public static string Trim(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.Trim();
    }

    /// <summary>Numero di grafemi, cioè di «caratteri» come li conta una persona.</summary>
    public static int GraphemeCount(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new StringInfo(value).LengthInTextElements;
    }
}
