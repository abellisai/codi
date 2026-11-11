using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Esercizi ordinati di una lezione (§7.3). L'unicità di <c>(lesson_id, position)</c> è un
/// conflitto previsto soltanto per la sostituzione in blocco, che passa da posizioni temporanee:
/// fino ad allora il comportamento comune basta, quindi l'interfaccia non aggiunge metodi.
/// </summary>
public interface ILessonExerciseRepository : IRepository<LessonExerciseEntity, string>;
