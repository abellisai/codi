using Qwerty.Codi.Application.Abstractions;

namespace Qwerty.Codi.Infrastructure.Realtime;

/// <summary>
/// Coordinatore delle sessioni in memoria (§8.2). Un semaforo per sessione, creato alla prima
/// richiesta e **rimosso quando nessuno lo tiene**: un registro che cresce a ogni lezione mai
/// rilasciata sarebbe una perdita che nessuno nota, perché una sessione dura un'ora e nessuno guarda
/// la memoria dopo un trimestre.
///
/// Il conteggio dei detentori è la ragione per cui l'ingresso e il rilascio passano dallo stesso
/// lucchetto: due richieste che arrivano mentre l'ultimo detentore rilascia devono finire nello
/// stesso semaforo o in due semafori diversi, mai a metà strada.
/// </summary>
internal sealed class SessionCoordinator : ISessionCoordinator
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Entry> _sessions = [];

    public async Task<IAsyncDisposable> AcquireAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Il coordinamento è per sessione: l'identificativo è obbligatorio.", nameof(sessionId));
        }

        var entry = Locked(() =>
        {
            if (!_sessions.TryGetValue(sessionId, out var existing))
            {
                existing = new Entry();
                _sessions[sessionId] = existing;
            }

            existing.Holders++;

            return existing;
        });

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Un'attesa annullata non deve lasciare un detentore contato: nessuno rilascerà per lei.
            Release(sessionId, entry, acquired: false);

            throw;
        }

        return new Lease(this, sessionId, entry);
    }

    private void Release(Guid sessionId, Entry entry, bool acquired)
    {
        Locked(() =>
        {
            if (acquired)
            {
                entry.Semaphore.Release();
            }

            entry.Holders--;

            // Nessun detentore e nessuno in attesa: la voce si può togliere. Chi arriva dopo crea un
            // semaforo nuovo, che è equivalente perché non c'è nessuno da coordinare.
            if (entry.Holders == 0)
            {
                _sessions.Remove(sessionId);
                entry.Semaphore.Dispose();
            }
        });
    }

    private T Locked<T>(Func<T> operation)
    {
        lock (_gate)
        {
            return operation();
        }
    }

    private void Locked(Action operation)
    {
        lock (_gate)
        {
            operation();
        }
    }

    /// <summary>Permesso di coordinamento: rilasciarlo due volte non farebbe danni ma è un difetto, quindi si rilascia una volta.</summary>
    private sealed class Lease(SessionCoordinator owner, Guid sessionId, Entry entry) : IAsyncDisposable
    {
        private bool _released;

        public ValueTask DisposeAsync()
        {
            if (_released)
            {
                return ValueTask.CompletedTask;
            }

            _released = true;
            owner.Release(sessionId, entry, acquired: true);

            return ValueTask.CompletedTask;
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(initialCount: 1, maxCount: 1);

        /// <summary>Detentori più attese in corso: serve a sapere quando la voce è inutile.</summary>
        public int Holders { get; set; }
    }
}
