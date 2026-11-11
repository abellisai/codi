using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Esito di un tentativo di scrittura nel thread: il messaggio conservato e se è stato effettivamente
/// creato. Una ripetizione con la stessa <see cref="Domain.Sessions.ParticipantMessage.ClientMessageKey"/>
/// restituisce lo stesso messaggio con <see cref="Created"/> falso, così il trasporto non avvisa due volte.
/// </summary>
public sealed record ParticipantMessageAppendResult(ParticipantMessageDto Message, bool Created);
