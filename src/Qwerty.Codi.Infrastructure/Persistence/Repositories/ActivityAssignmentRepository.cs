using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

/// <summary>
/// Assegnazioni di esercizi (§7.3). Non aggiunge nulla al CRUD comune: la riga non si aggiorna e
/// l'elenco per sessione è un filtro sul campo <c>session_id</c>.
/// </summary>
internal sealed class ActivityAssignmentRepository(DbContext context)
    : BaseRepository<ActivityAssignmentEntity, string>(context), IActivityAssignmentRepository;
