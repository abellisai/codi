using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

/// <summary>
/// Esercizi di una lezione: il CRUD comune, con i filtri per lezione espressi dal chiamante
/// tramite <c>QueryAsync</c>, perché l'ordine e l'insieme da leggere dipendono dal caso d'uso.
/// </summary>
internal sealed class LessonExerciseRepository(DbContext context)
    : BaseRepository<LessonExerciseEntity, string>(context), ILessonExerciseRepository;
