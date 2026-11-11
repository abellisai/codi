using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Lezioni (§7.3). Non aggiunge nulla al CRUD comune: il titolo non è una chiave e una lezione
/// non ha conflitti previsti in inserimento. L'interfaccia esiste perché è il nome con cui la
/// unit of work risolve il repository.
/// </summary>
public interface ILessonRepository : IRepository<LessonEntity, string>;
