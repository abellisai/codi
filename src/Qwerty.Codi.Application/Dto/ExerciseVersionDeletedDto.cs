namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Esito di <c>DeleteExerciseVersion</c> (§10.4, §13.8): la versione indicata non esiste più e con
/// lei è sparita la sua riga compagna privata — soluzione di riferimento e note dell'autore. Il
/// numero resta nel DTO perché il chiamante possa aggiornare l'elenco senza rileggerlo.
/// </summary>
public sealed record ExerciseVersionDeletedDto(Guid ExerciseId, long VersionNumber);
