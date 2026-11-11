using System.Linq.Expressions;
using Qwerty.Codi.Application.Abstractions;

namespace Qwerty.Codi.IntegrationTests;

/// <summary>Inserisce conflitti o guasti senza sostituire le connessioni e transazioni reali.</summary>
internal sealed class DecoratingUnitOfWorkContext(
    IUnitOfWorkContext inner, Func<IRepository, IRepository> decorate,
    Func<CancellationToken, Task>? beforeFirstWrite = null) : IUnitOfWorkContext
{
    private int _writeStarted;

    public async Task<IUnitOfWork> CreateAsync(bool useTransaction = false, CancellationToken cancellationToken = default)
    {
        if (useTransaction && beforeFirstWrite is not null && Interlocked.Exchange(ref _writeStarted, 1) == 0)
            await beforeFirstWrite(cancellationToken);
        return new Work(await inner.CreateAsync(useTransaction, cancellationToken), decorate);
    }

    private sealed class Work(IUnitOfWork innerWork, Func<IRepository, IRepository> decorateRepository) : IUnitOfWork
    {
        private readonly Dictionary<Type, IRepository> _repositories = new();
        public TRepository Repo<TRepository>() where TRepository : class, IRepository
        {
            if (!_repositories.TryGetValue(typeof(TRepository), out var repository))
            {
                repository = decorateRepository(innerWork.Repo<TRepository>());
                _repositories.Add(typeof(TRepository), repository);
            }
            return (TRepository)repository;
        }
        public Task CommitAsync(CancellationToken cancellationToken = default) => innerWork.CommitAsync(cancellationToken);
        public Task RollbackAsync(CancellationToken cancellationToken = default) => innerWork.RollbackAsync(cancellationToken);
        public ValueTask DisposeAsync() => innerWork.DisposeAsync();
    }
}

internal abstract class RepositoryTestDecorator<TEntity, TIdentity>(IRepository<TEntity, TIdentity> inner)
    : IRepository<TEntity, TIdentity> where TEntity : class, IPersistedEntity<TIdentity>
{
    public Task<TEntity?> GetAsync(TIdentity id, CancellationToken cancellationToken = default) => inner.GetAsync(id, cancellationToken);
    public Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default) => inner.GetAllAsync(cancellationToken);
    public virtual Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => inner.QueryAsync(predicate, cancellationToken);
    public Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => inner.ExistsAsync(predicate, cancellationToken);
    public virtual Task<TIdentity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default) => inner.InsertAsync(entity, cancellationToken);
    public Task<bool> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default) => inner.UpdateAsync(entity, cancellationToken);
    public Task<bool> DeleteAsync(TEntity entity, CancellationToken cancellationToken = default) => inner.DeleteAsync(entity, cancellationToken);
}
