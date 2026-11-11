using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

/// <summary>
/// Tentativi degli alunni (§7.3). Il CRUD comune copre lettura, inserimento e aggiornamento con la
/// versione di riga; la lettura per coppia — partecipante e assegnazione — si aggiunge perché è la
/// forma con cui il player trova il proprio lavoro e con cui la regia verifica che non ne esista già
/// uno. Il predicato passa dal filtro del repository comune, così la traduzione delle colonne resta
/// nel mapping e non in questa classe.
/// </summary>
internal sealed class ExerciseAttemptRepository(DbContext context)
    : BaseRepository<ExerciseAttemptEntity, string>(context), IExerciseAttemptRepository
{
    public async Task<IReadOnlyList<ExerciseAttemptEntity>> QueryByAssignmentAndParticipantAsync(
        Guid assignmentId,
        Guid participantId,
        CancellationToken cancellationToken = default)
    {
        if (assignmentId == Guid.Empty || participantId == Guid.Empty)
        {
            throw new ArgumentException("Assegnazione e partecipante sono obbligatori.", nameof(assignmentId));
        }

        var assignment = assignmentId.ToString("D");
        var participant = participantId.ToString("D");

        var rows = await QueryAsync(
                row => row.AssignmentId == assignment && row.ParticipantId == participant,
                cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.OrderBy(row => row.AttemptNumber)];
    }
}
