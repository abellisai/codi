using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

/// <summary>Il CRUD comune è tutto ciò che serve: un esercizio non ha chiavi naturali.</summary>
internal sealed class ExerciseRepository(DbContext context)
    : BaseRepository<ExerciseEntity, string>(context), IExerciseRepository;
