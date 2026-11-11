using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;
using RepoDb;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

/// <summary>
/// Registro di idempotenza (§7.5). Non deriva dal repository comune perché la chiave è composta e
/// il CRUD generato è costruito su una chiave sola: la lettura usa la coppia e la scrittura
/// sostituisce il record esistente con cancellazione e inserimento, nella transazione del
/// chiamante. Il registro non si aggiorna in place: cambia solo quando un record è scaduto.
/// </summary>
internal sealed class IdempotencyRecordRepository(DbContext context) : IIdempotencyRecordRepository
{
    public async Task<IdempotencyRecordEntity?> FindAsync(
        string scopeKey,
        string requestKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestKey);
        cancellationToken.ThrowIfCancellationRequested();

        // Un predicato sull'entità mappata, con i nomi delle proprietà: la traduzione della colonna
        // la fa il mapping, non questa riga.
        var rows = await context.Connection
            .QueryAsync<IdempotencyRecordEntity>(
                row => row.ScopeKey == scopeKey && row.RequestKey == requestKey,
                transaction: context.Transaction!,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return rows.SingleOrDefault();
    }

    public async Task ReplaceAsync(
        IdempotencyRecordEntity record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        await context.Connection
            .DeleteAsync<IdempotencyRecordEntity>(
                row => row.ScopeKey == record.ScopeKey && row.RequestKey == record.RequestKey,
                transaction: context.Transaction!,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await context.Connection
            .InsertAsync(record, transaction: context.Transaction!, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
