using System.Security.Cryptography;
using System.Text;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Impronte dell'ingresso idempotente (§9.4, §7.5). Sono due, con due compiti diversi: lo **scope**
/// attribuisce la richiesta a un tentativo di ingresso senza conservarne il cookie, e l'impronta
/// del **contenuto** distingue una ripetizione — che recupera l'esito — da un riuso della stessa
/// chiave con dati diversi, che è un conflitto.
///
/// Entrambe sono pure e deterministiche: la stessa richiesta produce la stessa impronta, e la
/// ripetizione non dipende dall'ordine delle proprietà JSON.
/// </summary>
public static class JoinIdempotency
{
    /// <summary>
    /// Impronta dello scope a partire dal valore del cookie di ammissione. Il registro conserva
    /// questa impronta e mai il cookie (§7.5): chi legge il database non ottiene un ticket.
    /// </summary>
    public static string ScopeKey(string admissionScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(admissionScope);

        return Hash(admissionScope.Trim());
    }

    /// <summary>
    /// Impronta del contenuto della richiesta: gli identificativi della catena scelta, il nome
    /// normalizzato e la postazione quando c'è. Cambiare uno di questi valori con la stessa chiave
    /// non è la stessa richiesta ripetuta.
    /// </summary>
    public static string RequestHash(
        Guid schoolId,
        Guid classroomId,
        Guid sessionId,
        string displayName,
        string? browserKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var canonical = string.Join(
            '\u001f',
            schoolId.ToString("D"),
            classroomId.ToString("D"),
            sessionId.ToString("D"),
            displayName.Trim(),
            browserKey?.Trim() ?? string.Empty);

        return Hash(canonical);
    }

    /// <summary>
    /// Impronta del contenuto della richiesta quando l'ingresso dichiara il **codice** invece della
    /// catena (§9.4): il codice, il nome normalizzato e la postazione. Il codice identifica una sola
    /// sessione, quindi la ripetizione con la stessa chiave recupera la stessa partecipazione; la
    /// stessa chiave con un codice diverso è un conflitto, come lo è con una catena diversa.
    /// </summary>
    public static string RequestHashByJoinCode(
        string joinCode,
        string displayName,
        string? browserKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(joinCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var canonical = string.Join(
            '\u001f',
            "join-code",
            joinCode.Trim(),
            displayName.Trim(),
            browserKey?.Trim() ?? string.Empty);

        return Hash(canonical);
    }

    /// <summary>
    /// Impronta di un valore che serve solo a contare i tentativi — lo scope di ammissione o
    /// l'indirizzo di rete — così il registro dei tentativi non conserva né il ticket né l'indirizzo.
    /// </summary>
    public static string AttemptKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return Hash(value.Trim());
    }

    /// <summary>Impronta della chiave della postazione: la chiave in chiaro non si conserva.</summary>
    public static string StationKey(string browserKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(browserKey);

        return Hash(browserKey.Trim());
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
