using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Coda dei messaggi da distribuire (§7, §12.2). Non deriva dal CRUD comune: <c>outbox_message</c>
/// non ha una <c>row_version</c>, quindi il completamento non si esprime con la concorrenza
/// ottimistica ma con un aggiornamento condizionato su «non ancora processato». Le operazioni sono
/// quelle del dispatcher: leggere i messaggi disponibili e segnare il tentativo concluso.
/// </summary>
public interface IOutboxMessageRepository : IRepository
{
    Task SupersedeSessionAsync(string sessionId, long nowUtcMs, CancellationToken cancellationToken = default);
    Task RescheduleAsync(Guid id, long availableAtUtcMs, long attemptCount, string errorCode,
        CancellationToken cancellationToken = default);
    /// <summary>Scrive il messaggio nella transazione del comando che l'ha prodotto.</summary>
    Task InsertAsync(OutboxMessageEntity message, CancellationToken cancellationToken = default);

    /// <summary>
    /// I messaggi distribuibili: non processati e con disponibilità già raggiunta, dal più vecchio.
    /// Il limite è la dimensione del lotto, per non tenere in memoria una coda lunga.
    /// </summary>
    Task<IReadOnlyList<OutboxMessageEntity>> QueryAvailableAsync(
        long availableAtOrBeforeUtcMs,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Segna il tentativo di distribuzione come concluso (§12.2): non è la conferma del client, che
    /// arriva con l'ACK. Restituisce falso se un altro dispatcher l'ha già processato.
    /// </summary>
    Task<bool> MarkProcessedAsync(
        Guid id,
        long processedAtUtcMs,
        long attemptCount,
        string? errorCode,
        CancellationToken cancellationToken = default);
}
