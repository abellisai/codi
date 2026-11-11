using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Infrastructure.Realtime;

/// <summary>
/// Thread della chat conservato in memoria (§15.1): il testo scritto dai bambini non sopravvive al
/// riavvio del server e viene eliminato alla fine della sessione, per decisione dell'utente del
/// 13 ottobre 2026. Non c'è migrazione né repository: la memoria è il solo posto dove il thread esiste.
///
/// La capienza per partecipante è un limite del processo, non una scelta di comodo: dodici alunni con
/// un thread illimitato farebbero crescere la memoria senza che nessuno lo decida. Un messaggio oltre la
/// capienza viene rifiutato dal caso d'uso, non scartato in silenzio (§15.6).
///
/// Le chiavi dei messaggi sono conservate per la stessa durata del thread: una richiesta ripetuta con la
/// stessa chiave restituisce il messaggio già scritto invece di duplicarlo (§12.2). Con la chat non c'è
/// la tabella di idempotenza, perché il thread non è persistito.
/// </summary>
internal sealed class ParticipantMessageStore : IParticipantMessageStore
{
    /// <summary>Numero massimo di messaggi conservati per un partecipante.</summary>
    private const int ThreadCapacity = 200;

    /// <summary>Numero massimo di thread aperti: la capienza totale del processo.</summary>
    private const int MaxThreads = 64;

    private readonly Lock _gate = new();

    private readonly Dictionary<Guid, Thread> _threads = [];

    public int MaxMessagesPerParticipant => ThreadCapacity;

    public ParticipantMessageAppendResult? Append(ParticipantMessageDto message, string clientMessageKey)
    {
        lock (_gate)
        {
            if (!_threads.TryGetValue(message.ParticipantId, out var thread))
            {
                if (_threads.Count >= MaxThreads) return null;
                thread = new Thread(message.ParticipantId, message.SessionId);
                _threads[message.ParticipantId] = thread;
            }

            var existing = thread.Entries.FirstOrDefault(entry => entry.ClientMessageKey == clientMessageKey);
            if (existing is not null)
            {
                // La stessa chiave è la stessa richiesta ripetuta: si restituisce il messaggio già
                // conservato, senza scriverlo di nuovo e senza cambiare il conteggio dei non letti.
                return new ParticipantMessageAppendResult(existing.Message, Created: false);
            }

            if (thread.Entries.Count >= ThreadCapacity) return null;

            thread.Entries.Add(new Entry(clientMessageKey, message));
            if (message.Author == MessageAuthor.Participant.ToString()) thread.UnreadByTeacher++;

            return new ParticipantMessageAppendResult(message, Created: true);
        }
    }

    public ParticipantThreadDto Read(Guid participantId, bool markRead)
    {
        lock (_gate)
        {
            if (!_threads.TryGetValue(participantId, out var thread))
            {
                return new ParticipantThreadDto(participantId, [], 0);
            }

            // Il conteggio restituito è quello osservato al momento della lettura, cioè il numero che
            // la card mostrava: azzerarlo è l'effetto per la lettura successiva, non un valore da
            // nascondere alla lettura che lo ha prodotto.
            var observed = thread.UnreadByTeacher;
            if (markRead) thread.UnreadByTeacher = 0;

            return new ParticipantThreadDto(participantId, [.. thread.Entries.Select(entry => entry.Message)], observed);
        }
    }

    public int UnreadByTeacher(Guid participantId)
    {
        lock (_gate) return _threads.TryGetValue(participantId, out var thread) ? thread.UnreadByTeacher : 0;
    }

    public void Remove(Guid participantId)
    {
        lock (_gate) _threads.Remove(participantId);
    }

    public void ClearSession(Guid sessionId)
    {
        lock (_gate)
        {
            foreach (var thread in _threads.Values.Where(thread => thread.SessionId == sessionId).ToArray())
            {
                _threads.Remove(thread.ParticipantId);
            }
        }
    }

    private sealed record Entry(string ClientMessageKey, ParticipantMessageDto Message);

    /// <summary>Un thread: i messaggi in ordine di scrittura, le chiavi già viste e i non letti.</summary>
    private sealed class Thread(Guid participantId, Guid sessionId)
    {
        public Guid ParticipantId { get; } = participantId;
        public Guid SessionId { get; } = sessionId;
        public List<Entry> Entries { get; } = [];
        public int UnreadByTeacher { get; set; }
    }
}
