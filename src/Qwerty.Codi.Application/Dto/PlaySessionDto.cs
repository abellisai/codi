namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Sessione in play come la vede la scoperta pubblica (§10.3): la lezione determina il corso, e la
/// risposta espone corso, lezione e gli identificativi della catena scuola → classe → sessione,
/// cioè abbastanza per mostrare «quali lezioni sono in play» e per inviare poi il join.
///
/// Nessun dato dei partecipanti e nessun identificativo di partecipante: la scoperta precede il
/// join, quindi chi è collegato non deve essere rivelato. Non compaiono revisioni né versioni di
/// riga: sono contatori della regia, non della scelta.
/// </summary>
public sealed record PlaySessionDto(
    Guid SchoolId,
    Guid ClassroomId,
    Guid SessionId,
    Guid LessonId,
    Guid CourseId,
    string CourseTitle,
    string LessonTitle);
