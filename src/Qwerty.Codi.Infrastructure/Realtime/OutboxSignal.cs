using Qwerty.Codi.Application.Abstractions;

namespace Qwerty.Codi.Infrastructure.Realtime;

/// <summary>
/// Segnale in-process del dispatcher outbox (§12.1): il comando appena registrato lo sveglia, invece
/// di aspettare che il dispatcher trovi il messaggio al giro successivo. Vive in memoria perché è un
/// suggerimento di prontezza, non uno stato: la verità resta la coda persistita, che il dispatcher
/// legge comunque a ogni giro.
/// </summary>
internal sealed class OutboxSignal : IOutboxSignal
{
    private readonly SemaphoreSlim _pending = new(initialCount: 0, maxCount: 1);

    public void Notify()
    {
        try
        {
            _pending.Release();
        }
        catch (SemaphoreFullException)
        {
            // C'è già un segnale in attesa: questo non aggiunge nulla, perché il dispatcher legge la
            // coda intera e non un messaggio per segnale.
        }
    }

    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        await _pending.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
}
