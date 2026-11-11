namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Contesto del partecipante: chi è, in quale sessione, con quale vista e con quale stato della
/// sessione. Le stringhe sono i valori del dominio, così il client non traduce codici numerici.
///
/// I tre nomi — scuola, classe, lezione — servono alla pagina dell'alunno per dire **dove si trova**
/// (0.40): il bambino arriva da un codice di otto cifre e deve poter riconoscere la sua classe e la
/// sua lezione senza chiederlo al maestro. Il titolo dell'esercizio non è qui: arriva dalla consegna,
/// che esiste solo quando il docente ha assegnato un'attività.
/// </summary>
public sealed record ParticipantContextDto(
    Guid ParticipantId,
    Guid SessionId,
    string DisplayName,
    string AdmissionStatus,
    string View,
    string SessionStatus,
    string SchoolName,
    string ClassroomName,
    string LessonTitle);
