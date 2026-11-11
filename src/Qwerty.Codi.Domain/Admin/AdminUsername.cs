using Qwerty.Codi.Domain.Common;

namespace Qwerty.Codi.Domain.Admin;

/// <summary>
/// Nome utente amministrativo: ammissibilità e normalizzazione. La normalizzazione decide
/// l'unicità dell'accesso, quindi non dipende da come il nome è stato digitato: uno stesso nome
/// con maiuscole, spazi o accenti diversi è lo stesso amministratore (§7.2). Le regole di
/// normalizzazione sono condivise con gli altri nomi confrontabili (§7.2, <see cref="NameNormalization"/>).
/// </summary>
public static class AdminUsername
{
    /// <summary>Lunghezza massima accettata, allineata al vincolo della tabella <c>admin_user</c>.</summary>
    public const int MaxLength = 120;

    /// <summary>
    /// Vero se il nome è utilizzabile: non vuoto, senza caratteri di controllo e non più lungo
    /// del limite. La validazione sta nel dominio perché la regola è la stessa per ogni ingresso,
    /// non una proprietà del modulo che lo riceve.
    /// </summary>
    public static bool IsValid(string? username) => NameNormalization.IsUsable(username, MaxLength);

    /// <summary>
    /// Normalizza il nome per il confronto: spazi esterni rimossi, sequenze interne di spazi
    /// ridotte a uno, segni diacritici scomposti e rimossi, minuscole invarianti. Serve a
    /// impedire che «Maestra», «MAESTRA», « maestra » e «maestrà» siano quattro account diversi.
    /// </summary>
    public static string Normalize(string username) => NameNormalization.Normalize(username);
}
