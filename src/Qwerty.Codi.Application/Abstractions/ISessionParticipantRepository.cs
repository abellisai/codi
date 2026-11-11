using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Partecipazioni a una sessione (§7.4). Non aggiunge nulla al CRUD comune: nomi uguali sono
/// ammessi, quindi non c'è un'unicità da rispettare oltre alla chiave primaria, e l'elenco per
/// sessione è un <c>QueryAsync</c> sul campo <c>session_id</c>. L'interfaccia esiste perché è il
/// nome con cui la unit of work risolve il repository.
/// </summary>
public interface ISessionParticipantRepository : IRepository<SessionParticipantEntity, string>;
