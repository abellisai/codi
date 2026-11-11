using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Sessioni di lezione (§7.4). Non aggiunge nulla al CRUD comune: l'unicità della sessione non
/// terminata per classe è garantita dall'indice parziale di §7.6, e il caso d'uso verifica
/// l'esistenza con una query prima di inserire. L'interfaccia esiste perché è il nome con cui la
/// unit of work risolve il repository.
/// </summary>
public interface ILessonSessionRepository : IRepository<LessonSessionEntity, string>
{
    /// <summary>Completa il default nella transazione che ha già avanzato la versione della sessione.</summary>
    Task<bool> CompleteClassDefaultAsync(string id, long rowVersion, string json, CancellationToken cancellationToken = default);
}
