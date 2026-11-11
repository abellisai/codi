using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

/// <summary>
/// Bozze di autoraggio degli esercizi (§7.3): il CRUD comune, con il controllo di <c>row_version</c>
/// della bozza ereditato da <see cref="BaseRepository{TEntity, TIdentity}"/>. La chiave primaria è
/// l'<c>exercise_id</c>, quindi <c>GetAsync</c> riceve quell'identificativo.
/// </summary>
internal sealed class ExerciseDraftRepository(DbContext context)
    : BaseRepository<ExerciseDraftEntity, string>(context), IExerciseDraftRepository;
