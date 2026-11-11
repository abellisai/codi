using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Stato desiderato dei partecipanti (§7.4): il CRUD comune, con la chiave primaria che è
/// l'identificativo del partecipante. L'interfaccia esiste perché è il nome con cui la unit of work
/// risolve il repository.
/// </summary>
public interface IParticipantStateRepository : IRepository<ParticipantStateEntity, string>;
