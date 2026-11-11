namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Coordinatore delle sessioni (§8.2): un lock asincrono **per sessione**, acquisito sempre prima
/// della transazione. Due comandi sulla stessa lezione si mettono in fila; due lezioni diverse non si
/// aspettano. Senza, due comandi concorrenti leggerebbero la stessa revisione e il secondo fallirebbe
/// con un conflitto evitabile — o, peggio, scriverebbe due volte lo stesso stato.
///
/// Vive in memoria perché protegge un processo, non un database: la specifica dichiara che una
/// distribuzione su più istanze richiederebbe una decisione architetturale nuova, non che questo
/// coordinatore vada replicato.
/// </summary>
public interface ISessionCoordinator
{
    /// <summary>
    /// Acquisisce il coordinamento della sessione e restituisce il permesso di rilasciarlo. L'attesa
    /// è cancellabile: una richiesta annullata mentre aspetta non deve trattenere nulla.
    /// </summary>
    Task<IAsyncDisposable> AcquireAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
