using System.Globalization;
using System.Text;

namespace Qwerty.Codi.Domain.Common;

/// <summary>
/// Regole comuni ai nomi che l'applicazione usa come chiave di confronto (§7.2). Stanno in un solo
/// posto perché due normalizzazioni diverse darebbero due entità per lo stesso nome: la differenza
/// fra «Maestra» e «MAESTRA» non può dipendere dal punto del codice che la calcola.
/// </summary>
public static class NameNormalization
{
    /// <summary>
    /// Vero se il testo è utilizzabile come nome: non vuoto, senza caratteri di controllo e non
    /// più lungo del limite, sia come è stato digitato sia dopo la normalizzazione.
    /// </summary>
    public static bool IsUsable(string? value, int maxLength)
    {
        if (maxLength < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxLength), maxLength, "Il limite di lunghezza deve essere positivo.");
        }

        if (string.IsNullOrWhiteSpace(value) ||
            value.Any(char.IsControl) ||
            value.Trim().Length > maxLength)
        {
            return false;
        }

        // Anche la forma normalizzata deve stare nel limite: una sequenza di caratteri che si
        // riduce a poco testo non deve poter scavalcare il vincolo della tabella.
        var normalizedLength = Normalize(value).Length;

        return normalizedLength > 0 && normalizedLength <= maxLength;
    }

    /// <summary>
    /// Normalizza il testo per il confronto: spazi esterni rimossi, sequenze interne di spazi
    /// ridotte a uno, segni diacritici scomposti e rimossi, minuscole invarianti. Serve a impedire
    /// che «Maestra», «MAESTRA», « maestra » e «maestrà» siano quattro nomi diversi.
    /// </summary>
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                // Uno spazio conta solo se qualcosa lo precede: gli spazi iniziali e quelli
                // ripetuti non devono sopravvivere alla normalizzazione.
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }
}
