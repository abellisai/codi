namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Il thread di un alunno letto da una delle due parti (§15.1). I messaggi sono in ordine di
/// scrittura; l'elenco vuoto è un'informazione e non un errore: la chat esiste, non ha ancora
/// messaggi.
///
/// <see cref="UnreadByTeacher"/> è il numero di messaggi scritti dall'alunno che il docente non ha
/// ancora letto. Si azzera con la lettura del thread, quindi la lettura del thread ha un effetto
/// osservabile sulla card. Per l'alunno il valore è sempre zero: il server non misura se un bambino
/// ha letto, e non finge di farlo.
/// </summary>
public sealed record ParticipantThreadDto(
    Guid ParticipantId,
    IReadOnlyList<ParticipantMessageDto> Messages,
    int UnreadByTeacher);
