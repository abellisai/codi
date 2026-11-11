using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Consegne dei comandi (§7, §12.2). La chiave è composta — comando e partecipante — quindi il
/// repository non offre il CRUD per chiave singola: espone l'inserimento in blocco delle consegne di
/// un comando e le letture per comando. È comunque un <see cref="IRepository"/> perché è così che la
/// unità di lavoro lo risolve.
/// </summary>
public interface ICommandDeliveryRepository : IRepository
{
    /// <summary>Scrive le consegne di un comando nella transazione del chiamante.</summary>
    Task InsertAllAsync(
        IReadOnlyList<CommandDeliveryEntity> deliveries,
        CancellationToken cancellationToken = default);

    /// <summary>Le consegne di un comando, in ordine stabile per destinatario.</summary>
    Task<IReadOnlyList<CommandDeliveryEntity>> QueryByCommandAsync(
        Guid commandId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registra l'invio al trasporto di un destinatario: porta la consegna da <c>Pending</c> a
    /// <c>Sent</c> e conta il tentativo. Falso se la consegna non era più in attesa — un altro
    /// dispatcher l'ha già inviata — perché l'invio si conferma una volta sola.
    /// </summary>
    Task<bool> MarkSentAsync(
        Guid commandId,
        Guid participantId,
        long sentAtUtcMs,
        long currentSendAttempts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marca una consegna come superata da una revisione più recente (§12.2): la ricevuta resta, ma
    /// lo stato non torna indietro. La transizione è condizionata allo stato letto.
    /// </summary>
    Task<bool> MarkSupersededAsync(
        Guid commandId,
        Guid participantId,
        string currentStatus,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registra la conferma applicativa (§12.2): porta la consegna ad <c>Applied</c>, con la pausa di
    /// recupero quando il client dichiara di non essere certo dello stato eseguito. È ammessa da
    /// <c>Sent</c> e da <c>Unconfirmed</c> — una conferma tardiva vale — mentre una consegna già
    /// applicata non cambia: restituisce falso perché non c'è transizione da registrare.
    /// </summary>
    Task<bool> MarkAppliedAsync(
        Guid commandId,
        Guid participantId,
        long acknowledgedAtUtcMs,
        bool requiresRecovery,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registra un esito negativo (§12.2): la consegna diventa <c>Failed</c> con il codice riportato
    /// dal client. Come l'applicazione, è ammessa solo da una consegna effettivamente inviata.
    /// </summary>
    Task<bool> MarkFailedAsync(
        Guid commandId,
        Guid participantId,
        long acknowledgedAtUtcMs,
        string? errorCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Le consegne inviate entro l'istante indicato e mai confermate (§12.2: l'attesa è di cinque
    /// secondi). Una consegna non ancora inviata non è in ritardo: nessuno l'ha ricevuta.
    /// </summary>
    Task<IReadOnlyList<CommandDeliveryEntity>> QueryUnconfirmedAsync(
        long sentAtOrBeforeUtcMs,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marca la mancata conferma: <c>Sent</c> → <c>Unconfirmed</c>. È un'evidenza, non un rifiuto: un
    /// ACK tardivo può ancora portare la consegna ad <c>Applied</c> (§12.2).
    /// </summary>
    Task<bool> MarkUnconfirmedAsync(
        Guid commandId,
        Guid participantId,
        CancellationToken cancellationToken = default);
}
