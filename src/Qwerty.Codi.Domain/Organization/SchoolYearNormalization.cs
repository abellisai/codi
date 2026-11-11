using System.Globalization;

namespace Qwerty.Codi.Domain.Organization;

/// <summary>
/// Forma dell'anno scolastico di una classe (§7.2). Sta in un tipo a sé e non dentro
/// <see cref="Classroom"/> perché è una regola di formato, come quella dei nomi confrontabili:
/// il nome del tipo non ripete quello della proprietà, che è ciò che renderebbe ambigua la
/// lettura di <c>SchoolYear.TryNormalize</c> dentro la classe.
///
/// L'anno è parte dell'identità della classe — nome e anno insieme la rendono unica nella scuola —
/// quindi ha una sola forma: due anni consecutivi. Il trattino è accettato e ricondotto alla barra.
/// </summary>
public static class SchoolYearNormalization
{
    /// <summary>Lunghezza della forma canonica: quattro cifre, la barra, quattro cifre.</summary>
    public const int Length = 9;

    /// <summary>
    /// Normalizza l'anno digitato: `2026/2027` e `2026-2027` danno lo stesso valore. Restituisce
    /// falso per una forma diversa, per anni non consecutivi e per un testo vuoto.
    /// </summary>
    public static bool TryNormalize(string? written, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(written))
        {
            return false;
        }

        var trimmed = written.Trim();
        if (trimmed.Length != Length || trimmed[4] is not ('/' or '-'))
        {
            return false;
        }

        if (!TryReadYear(trimmed.AsSpan(0, 4), out var first) ||
            !TryReadYear(trimmed.AsSpan(5, 4), out var second) ||
            second != first + 1)
        {
            return false;
        }

        normalized = $"{first}/{second}";

        return true;
    }

    /// <summary>Vero se l'anno è già nella forma canonica.</summary>
    public static bool IsValid(string? written) => TryNormalize(written, out _);

    private static bool TryReadYear(ReadOnlySpan<char> digits, out int year) =>
        int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out year)
        && year is >= 1000 and <= 9999;
}
