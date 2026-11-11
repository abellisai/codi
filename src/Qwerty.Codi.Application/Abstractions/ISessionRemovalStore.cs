namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Rimozione dei **figli tecnici** di una sessione quando la sessione stessa viene eliminata
/// (§6.1, §7.1). Le chiavi esterne dello schema usano <c>ON DELETE RESTRICT</c>, quindi i figli si
/// cancellano esplicitamente e in un ordine che rispetta i riferimenti; il caso d'uso verifica
/// prima provenienza, revisione e stato, e cancella la riga di <c>lesson_session</c> con il
/// controllo di versione del repository comune.
///
/// L'ordine e le tabelle coinvolte sono una decisione di persistenza, non un dettaglio dell'handler:
/// restano qui, in un solo punto verificabile, invece di essere sparsi in nove repository. Le righe
/// rimosse non hanno valore autonomo — consegne, comandi, messaggi, stati, tentativi e credenziali
/// di quella sessione — mentre scuola, classe, lezione, esercizio e versioni non vengono toccati.
///
/// È un <see cref="IRepository"/> perché è così che la unità di lavoro lo risolve: la rimozione deve
/// avvenire nella stessa connessione e transazione del caso d'uso.
/// </summary>
public interface ISessionRemovalStore : IRepository
{
    /// <summary>
    /// Elimina i figli della sessione nella transazione del chiamante, senza cancellare la riga di
    /// <c>lesson_session</c>. Una sessione senza figli non produce alcuna scrittura.
    /// </summary>
    Task RemoveChildrenAsync(string sessionId, CancellationToken cancellationToken = default);
}
