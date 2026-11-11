using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Registro di idempotenza (§7.5). La chiave è composta — impronta dello scope e chiave della
/// richiesta — quindi il repository non offre il CRUD per chiave singola: espone la lettura della
/// coppia e la scrittura che sostituisce un record scaduto. È comunque un <see cref="IRepository"/>
/// perché è così che la unit of work lo risolve.
/// </summary>
public interface IIdempotencyRecordRepository : IRepository
{
    /// <summary>Legge il risultato registrato per la coppia, se esiste.</summary>
    Task<IdempotencyRecordEntity?> FindAsync(
        string scopeKey,
        string requestKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registra il risultato, sostituendo un record già presente per la stessa coppia: la chiave
    /// scaduta torna utilizzabile, e la sostituzione avviene nella transazione del chiamante.
    /// </summary>
    Task ReplaceAsync(IdempotencyRecordEntity record, CancellationToken cancellationToken = default);
}
