using System.Data.Common;

namespace Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;

/// <summary>Contesto interno condiviso da tutti i repository dello scope della UoW.</summary>
internal sealed class DbContext(SqliteConnectionFactory connectionFactory) : IAsyncDisposable
{
    private DbConnection? _connection;
    private DbTransaction? _transaction;
    private bool _completed;
    private bool _disposed;

    public DbConnection Connection
    {
        get
        {
            EnsureActive();
            return _connection ?? throw new InvalidOperationException("La unit of work non è stata aperta.");
        }
    }

    public DbTransaction? Transaction
    {
        get { EnsureActive(); return _transaction; }
    }

    public async Task OpenAsync(bool useTransaction, CancellationToken cancellationToken)
    {
        EnsureActive();
        _connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (useTransaction)
        {
            // Microsoft.Data.Sqlite usa BEGIN IMMEDIATE per la transazione non differita:
            // il writer si acquisisce prima delle letture che decidono le scritture.
            _transaction = await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        if (_transaction is not null)
            await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        if (_transaction is not null)
            await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    public void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
            throw new InvalidOperationException("La unit of work è già stata completata.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            // Il dispose della transazione non confermata esegue rollback, anche dopo
            // cancellazione: non si passa il token già annullato alla pulizia.
            if (_transaction is not null)
                await _transaction.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            if (_connection is not null)
                await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
