using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;
using RepoDb;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

/// <summary>
/// Coda dei messaggi da distribuire (§7, §12.2). Non deriva dal repository comune perché
/// <c>outbox_message</c> non ha una <c>row_version</c>: il completamento si esprime con un
/// aggiornamento condizionato su «non ancora processato», che ottiene lo stesso risultato della
/// concorrenza ottimistica — due dispatcher non processano lo stesso messaggio — senza una colonna
/// di versione da confrontare. Ogni scrittura resta nella transazione del chiamante.
/// </summary>
internal sealed class OutboxMessageRepository(DbContext context) : IOutboxMessageRepository
{
    public async Task SupersedeSessionAsync(string sessionId, long nowUtcMs, CancellationToken cancellationToken = default)
    {
        await context.Connection.UpdateAsync(new OutboxMessageEntity
        { ProcessedAtUtcMs = nowUtcMs, LastErrorCode = "StartupRecovery" },
            where: row => row.SessionId == sessionId && row.ProcessedAtUtcMs == null,
            fields: Field.Parse<OutboxMessageEntity>(row => new { row.ProcessedAtUtcMs, row.LastErrorCode }),
            transaction: context.Transaction!, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
    public async Task RescheduleAsync(Guid id, long availableAtUtcMs, long attemptCount, string errorCode,
        CancellationToken cancellationToken = default)
    {
        var row = new OutboxMessageEntity
        {
            Id = id.ToString("D"),
            AvailableAtUtcMs = availableAtUtcMs,
            AttemptCount = attemptCount,
            LastErrorCode = errorCode
        };
        await context.Connection.UpdateAsync(row,
            where: entity => entity.Id == row.Id && entity.ProcessedAtUtcMs == null,
            fields: Field.Parse<OutboxMessageEntity>(entity => new { entity.AvailableAtUtcMs, entity.AttemptCount, entity.LastErrorCode }),
            transaction: context.Transaction!, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
    public async Task InsertAsync(OutboxMessageEntity message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        await context.Connection
            .InsertAsync(message, transaction: context.Transaction!, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OutboxMessageEntity>> QueryAvailableAsync(
        long availableAtOrBeforeUtcMs,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit), limit, "La dimensione del lotto deve essere positiva.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Predicato sull'entità mappata: la traduzione delle colonne la fa il mapping, non questa riga.
        var rows = await context.Connection
            .QueryAsync<OutboxMessageEntity>(
                row => row.ProcessedAtUtcMs == null && row.AvailableAtUtcMs <= availableAtOrBeforeUtcMs,
                transaction: context.Transaction!,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows
                .OrderBy(row => row.AvailableAtUtcMs)
                .ThenBy(row => row.Id, StringComparer.Ordinal)
                .Take(limit),
        ];
    }

    public async Task<bool> MarkProcessedAsync(
        Guid id,
        long processedAtUtcMs,
        long attemptCount,
        string? errorCode,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("L'identificativo del messaggio è obbligatorio.", nameof(id));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var row = new OutboxMessageEntity
        {
            Id = id.ToString("D"),
            ProcessedAtUtcMs = processedAtUtcMs,
            AttemptCount = attemptCount,
            LastErrorCode = errorCode,
        };

        var fields = Field.Parse<OutboxMessageEntity>(entity => new
        {
            entity.ProcessedAtUtcMs,
            entity.AttemptCount,
            entity.LastErrorCode,
        });

        // Solo il primo tentativo segna il messaggio: la condizione è su «non ancora processato».
        var written = await context.Connection
            .UpdateAsync(
                row,
                where: entity => entity.Id == row.Id && entity.ProcessedAtUtcMs == null,
                fields: fields,
                transaction: context.Transaction!,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return written == 1;
    }
}
