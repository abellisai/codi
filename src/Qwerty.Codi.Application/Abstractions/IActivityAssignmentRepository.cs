using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Assegnazioni di esercizi a una sessione (§7.3, §6.5). La riga non si aggiorna: una nuova
/// assegnazione è una riga nuova, che lascia intatto il materiale già dato agli alunni. L'elenco per
/// sessione è un <c>QueryAsync</c> sul campo <c>session_id</c>.
/// </summary>
public interface IActivityAssignmentRepository : IRepository<ActivityAssignmentEntity, string>;

/// <summary>
/// Tentativi degli alunni (§7.3). Oltre al CRUD serve la lettura per coppia — partecipante e
/// assegnazione — che è come il player trova il proprio lavoro e come il comando di assegnazione
/// verifica di non crearne due.
/// </summary>
public interface IExerciseAttemptRepository : IRepository<ExerciseAttemptEntity, string>
{
    /// <summary>I tentativi di un partecipante su un'assegnazione, in ordine di numero.</summary>
    Task<IReadOnlyList<ExerciseAttemptEntity>> QueryByAssignmentAndParticipantAsync(
        Guid assignmentId,
        Guid participantId,
        CancellationToken cancellationToken = default);
}
