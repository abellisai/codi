using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

internal sealed class AttemptFinalizationGrantRepository(DbContext context)
    : BaseRepository<AttemptFinalizationGrantEntity, string>(context), IAttemptFinalizationGrantRepository;
