namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Materiale assegnato al singolo alunno: quale versione sta lavorando e da dove arriva.
///
/// <see cref="EngineVersion"/> è la versione del motore fissata dall'assegnazione, non quella
/// dell'anagrafica: è quella che dice al docente **come leggere** il lavoro dell'alunno — un progetto
/// a blocchi oppure un verbale di risposta — e resta quella anche se la bozza dell'esercizio cambia
/// dopo la pubblicazione.
/// </summary>
public sealed record ParticipantAssignmentDto(
    Guid AssignmentId,
    string Scope,
    Guid? LessonExerciseId,
    Guid ExerciseVersionId,
    Guid ExerciseId,
    string ExerciseTitle,
    string EngineVersion);
