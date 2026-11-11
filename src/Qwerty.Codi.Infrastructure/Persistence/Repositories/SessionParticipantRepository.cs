using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

/// <summary>
/// Il CRUD comune è tutto ciò che serve: nomi uguali sono ammessi (§5.2), quindi non c'è
/// un'unicità da rispettare oltre alla chiave primaria, e l'elenco delle partecipazioni di una
/// sessione è un <c>QueryAsync</c> su <c>session_id</c>.
/// </summary>
internal sealed class SessionParticipantRepository(DbContext context)
    : BaseRepository<SessionParticipantEntity, string>(context), ISessionParticipantRepository;
