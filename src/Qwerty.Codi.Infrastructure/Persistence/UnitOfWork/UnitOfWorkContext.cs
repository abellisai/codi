using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Abstractions;

namespace Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;

internal sealed class UnitOfWorkContext(IServiceScopeFactory scopeFactory) : IUnitOfWorkContext
{
    public async Task<IUnitOfWork> CreateAsync(
        bool useTransaction = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var context = scope.ServiceProvider.GetRequiredService<DbContext>();
            await context.OpenAsync(useTransaction, cancellationToken).ConfigureAwait(false);
            return new UnitOfWork(scope, context);
        }
        catch
        {
            // Nessuna proprietà trasferita al chiamante: liberare anche se fallisce BEGIN.
            try { await scope.DisposeAsync().ConfigureAwait(false); }
            catch { /* Conservare l'errore di apertura, non quello secondario di pulizia. */ }
            throw;
        }
    }
}

internal sealed class UnitOfWork(AsyncServiceScope scope, DbContext context) : IUnitOfWork
{
    public TRepository Repo<TRepository>() where TRepository : class, IRepository
    {
        context.EnsureActive();
        return scope.ServiceProvider.GetRequiredService<TRepository>();
    }

    public Task CommitAsync(CancellationToken cancellationToken = default) => context.CommitAsync(cancellationToken);
    public Task RollbackAsync(CancellationToken cancellationToken = default) => context.RollbackAsync(cancellationToken);
    public ValueTask DisposeAsync() => scope.DisposeAsync();
}
