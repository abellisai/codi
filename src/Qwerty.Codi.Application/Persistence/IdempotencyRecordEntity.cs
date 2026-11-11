namespace Qwerty.Codi.Application.Persistence;

/// <summary>
/// Entità di persistenza di <c>idempotency_record</c> (§7.5): il risultato di una richiesta
/// ripetibile, registrato per evitare che la stessa richiesta produca due volte lo stesso effetto.
///
/// Non implementa <c>IPersistedEntity</c> perché la chiave della tabella è **composta** — impronta
/// dello scope e chiave della richiesta — e il CRUD comune è costruito su una chiave sola. Il
/// registro si legge e si scrive per coppia, con i due metodi espliciti del repository.
///
/// Non contiene segreti: <see cref="ResponseJson"/> porta i riferimenti alle entità create e mai la
/// marca di sicurezza di una credenziale (§7.5).
/// </summary>
public sealed class IdempotencyRecordEntity
{
    /// <summary>Impronta dello scope dell'ingresso: è il cookie di ammissione trasformato, non il cookie.</summary>
    public string ScopeKey { get; set; } = string.Empty;

    /// <summary>Chiave dichiarata dal client per questa richiesta.</summary>
    public string RequestKey { get; set; } = string.Empty;

    /// <summary>Impronta del contenuto: la stessa chiave con un contenuto diverso è un conflitto.</summary>
    public string RequestHash { get; set; } = string.Empty;

    /// <summary>Stato HTTP della risposta registrata.</summary>
    public int ResponseCode { get; set; }

    /// <summary>Risposta registrata, senza segreti.</summary>
    public string ResponseJson { get; set; } = string.Empty;

    /// <summary>Scadenza del record: dopo questo istante la chiave è di nuovo utilizzabile.</summary>
    public long ExpiresAtUtcMs { get; set; }
}
