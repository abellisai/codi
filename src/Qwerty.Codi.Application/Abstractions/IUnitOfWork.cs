namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Connessione dedicata e repository condivisi per una singola operazione. Nessun uso parallelo.
/// Dispose senza commit annulla la transazione; dopo commit o rollback non è più utilizzabile.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    TRepository Repo<TRepository>() where TRepository : class, IRepository;
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>Factory senza stato: ogni creazione apre uno scope isolato, anche dentro lo stesso handler.</summary>
public interface IUnitOfWorkContext
{
    Task<IUnitOfWork> CreateAsync(
        bool useTransaction = false, CancellationToken cancellationToken = default);
}
