using System.Linq.Expressions;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;
using RepoDb;
using RepoDb.Enumerations;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

/// <summary>
/// Operazioni comuni generate da RepoDB dal mapping dell'entità. La connessione e la transazione
/// sono prese dal contesto della UoW; il repository non ne possiede il ciclo di vita.
/// </summary>
// RepoDB accetta null per transaction/fields (come i suoi default), ma le annotazioni non lo dichiarano.
internal abstract class BaseRepository<TEntity, TIdentity>(DbContext context)
    : IRepository<TEntity, TIdentity> where TEntity : class, IPersistedEntity<TIdentity>
{
    public async Task<TEntity?> GetAsync(TIdentity id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await context.Connection.QueryAsync<TEntity>(id,
            transaction: context.Transaction!, cancellationToken: cancellationToken).ConfigureAwait(false);
        return rows.SingleOrDefault();
    }

    public async Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await context.Connection.QueryAllAsync<TEntity>(
            transaction: context.Transaction!, cancellationToken: cancellationToken).ConfigureAwait(false);
        return rows.ToArray();
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(
        Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await context.Connection.QueryAsync<TEntity>(predicate,
            transaction: context.Transaction!, cancellationToken: cancellationToken).ConfigureAwait(false);
        return rows.ToArray();
    }

    public async Task<bool> ExistsAsync(
        Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        cancellationToken.ThrowIfCancellationRequested();
        return await context.Connection.CountAsync<TEntity>(predicate,
            transaction: context.Transaction!, cancellationToken: cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<TIdentity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        cancellationToken.ThrowIfCancellationRequested();
        return await context.Connection.InsertAsync<TEntity, TIdentity>(entity,
            transaction: context.Transaction!, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default) =>
        UpdateFieldsAsync(entity, fields: null, extraPredicate: null, cancellationToken);

    /// <summary>Stesso controllo di versione anche per aggiornamenti parziali o condizionati.</summary>
    protected async Task<bool> UpdateFieldsAsync(
        TEntity entity, IEnumerable<Field>? fields, Expression<Func<TEntity, bool>>? extraPredicate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entity);
        cancellationToken.ThrowIfCancellationRequested();
        var expectedVersion = entity.RowVersion;
        var where = VersionCondition(entity, extraPredicate);
        entity.RowVersion = checked(expectedVersion + 1);
        var written = false;
        try
        {
            written = await context.Connection.UpdateAsync(entity, where, fields: fields!,
                transaction: context.Transaction!, cancellationToken: cancellationToken).ConfigureAwait(false) == 1;
            return written;
        }
        finally
        {
            if (!written) entity.RowVersion = expectedVersion;
        }
    }

    public async Task<bool> DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        cancellationToken.ThrowIfCancellationRequested();
        return await context.Connection.DeleteAsync<TEntity>(VersionCondition(entity, extraPredicate: null),
            transaction: context.Transaction!, cancellationToken: cancellationToken).ConfigureAwait(false) == 1;
    }

    private static QueryGroup VersionCondition(TEntity entity, Expression<Func<TEntity, bool>>? extraPredicate)
    {
        var condition = new QueryGroup(new[]
        {
            new QueryField(PropertyMappedNameCache.Get<TEntity>(nameof(entity.Id)), Operation.Equal, entity.Id!),
            new QueryField(PropertyMappedNameCache.Get<TEntity>(nameof(entity.RowVersion)), Operation.Equal, entity.RowVersion),
        });
        return extraPredicate is null ? condition : new QueryGroup(
            new[] { condition, QueryGroup.Parse(extraPredicate) }, Conjunction.And);
    }
}
