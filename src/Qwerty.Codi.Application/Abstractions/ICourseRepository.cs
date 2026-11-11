using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Corsi. Non aggiunge nulla al CRUD comune: il titolo non è una chiave, quindi un inserimento
/// non ha conflitti previsti e l'aggiornamento usa il controllo di versione già presente.
/// L'interfaccia esiste perché è il nome con cui la unit of work risolve il repository.
/// </summary>
public interface ICourseRepository : IRepository<CourseEntity, string>;
