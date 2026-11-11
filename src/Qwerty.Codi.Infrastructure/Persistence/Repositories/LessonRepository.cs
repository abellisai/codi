using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

/// <summary>Il CRUD comune è tutto ciò che serve: una lezione non ha chiavi naturali.</summary>
internal sealed class LessonRepository(DbContext context)
    : BaseRepository<LessonEntity, string>(context), ILessonRepository;
