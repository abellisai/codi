using Qwerty.Codi.Application.Dto;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Chat del singolo alunno, conservata **in memoria** (§15.1). Non è un salvataggio: il thread non
/// sopravvive al riavvio del server e viene eliminato alla fine della sessione, per decisione
/// dell'utente del 13 ottobre 2026 sul testo scritto dai bambini.
///
/// La capienza è limitata per partecipante: una chat non deve poter far crescere la memoria del
/// processo. Un messaggio oltre la capienza viene rifiutato, non accodato a un elenco illimitato
/// (§15.6).
///
/// L'identificativo del thread è il partecipante, perché la chat è sempre fra il docente e un solo
/// alunno: non esiste una chat di classe.
/// </summary>
public interface IParticipantMessageStore
{
    /// <summary>
    /// Capienza massima dei messaggi conservati per un partecipante. Oltre questa cifra il messaggio
    /// viene rifiutato con un errore, non scartato in silenzio.
    /// </summary>
    int MaxMessagesPerParticipant { get; }

    /// <summary>
    /// Aggiunge un messaggio al thread. <paramref name="clientMessageKey"/> è la chiave dichiarata dal
    /// client: una richiesta ripetuta con la stessa chiave non duplica il messaggio, e restituisce
    /// quello già conservato (§12.2). Una chiave diversa è un messaggio nuovo.
    ///
    /// Il risultato segnala se il messaggio è stato effettivamente creato, così il trasporto avvisa
    /// solo la scrittura nuova e non la ripetizione. Restituisce <c>null</c> quando la capienza del
    /// thread è superata: il caso d'uso decide il rifiuto e il messaggio da mostrare, la cache non
    /// lancia eccezioni.
    /// </summary>
    ParticipantMessageAppendResult? Append(ParticipantMessageDto message, string clientMessageKey);

    /// <summary>
    /// Legge il thread in ordine di scrittura. <paramref name="markRead"/> azzera il conteggio dei
    /// non letti solo quando la lettura è quella del docente: la lettura dell'alunno non cambia ciò
    /// che il docente vede.
    /// </summary>
    ParticipantThreadDto Read(Guid participantId, bool markRead);

    /// <summary>Non letti dal docente, senza azzerarli: la card li mostra senza aprire il thread.</summary>
    int UnreadByTeacher(Guid participantId);

    /// <summary>Elimina il thread di un partecipante (uscita o esclusione).</summary>
    void Remove(Guid participantId);

    /// <summary>Elimina tutti i thread della sessione: avviene alla fine della sessione (§15.1).</summary>
    void ClearSession(Guid sessionId);
}
