using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Bozze di autoraggio degli esercizi (§7.3). Non aggiunge nulla al CRUD comune: la chiave è
/// l'<c>exercise_id</c>, quindi la lettura di una bozza è <c>GetAsync</c> con quell'identificativo
/// e l'aggiornamento usa il controllo di versione già presente. L'interfaccia esiste perché è il
/// nome con cui la unit of work risolve il repository.
/// </summary>
public interface IExerciseDraftRepository : IRepository<ExerciseDraftEntity, string>;
