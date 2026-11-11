using System.Globalization;

namespace Qwerty.Codi.Domain.Sessions;

/// <summary>
/// Chi ha scritto un messaggio della chat fra docente e singolo alunno (§8, §15.1). Sono i due
/// autori possibili: la chat non ha un terzo autore perché non esiste una chat fra bambini.
/// </summary>
public enum MessageAuthor
{
    /// <summary>Scritto dal docente che observa la sessione.</summary>
    Teacher,

    /// <summary>Scritto dall'alunno della chat.</summary>
    Participant,
}

/// <summary>
/// Testo di un messaggio della chat (§8, §15.1). Il limite è in **grafemi** come per il nome del
/// partecipante (§7.4), perché il testo lo scrive e lo legge un bambino di otto anni.
///
/// Non esiste un filtro sul contenuto: verificare le parole contro un elenco direbbe al docente
/// quali messaggi sono ammessi, e la chat serve proprio a dire ciò che non è prevedibile. Si
/// verificano solo la forma e la lunghezza — testo non vuoto, senza caratteri di controllo — perché
/// un testo vuoto o con a capo nascosti non è un messaggio e non si può mostrare.
/// </summary>
public static class ParticipantMessageText
{
    /// <summary>Limite in grafemi, come li conta una persona, non in caratteri UTF-16.</summary>
    public const int MaxGraphemes = 300;

    /// <summary>Vero se il testo può essere il contenuto di un messaggio.</summary>
    public static bool IsUsable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            return false;
        }

        var graphemes = GraphemeCount(value.Trim());

        return graphemes >= 1 && graphemes <= MaxGraphemes;
    }

    /// <summary>Testo che il dominio conserva: senza spazi ai bordi, come il nome del partecipante.</summary>
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
