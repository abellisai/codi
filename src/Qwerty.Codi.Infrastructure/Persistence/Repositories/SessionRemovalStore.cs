using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;
using RepoDb;
using RepoDb.Enumerations;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

/// <summary>
/// Rimozione dei figli tecnici di una sessione (§6.1, §7.1). Le chiavi esterne sono
/// <c>ON DELETE RESTRICT</c>, quindi l'ordine conta: si cancellano prima i riferimenti e poi ciò
/// che essi riferiscono. Tutte le scritture restano nella transazione del chiamante e l'ultima riga
/// — quella di <c>lesson_session</c> — non è di questo store: la cancella il caso d'uso con il
/// controllo di versione.
/// </summary>
internal sealed class SessionRemovalStore(DbContext context) : ISessionRemovalStore
{
    public async Task RemoveChildrenAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        cancellationToken.ThrowIfCancellationRequested();

        // Le tre letture che servono a costruire gli elenchi delle chiavi composte. Una sessione
        // eliminabile è terminata o mai avviata: i suoi figli sono poche decine di righe.
        var participants = await QueryAsync<SessionParticipantEntity>(
            row => row.SessionId == sessionId, cancellationToken).ConfigureAwait(false);
        var commands = await QueryAsync<SessionCommandEntity>(
            row => row.SessionId == sessionId, cancellationToken).ConfigureAwait(false);
        var attempts = await QueryAsync<ExerciseAttemptEntity>(
            row => row.SessionId == sessionId, cancellationToken).ConfigureAwait(false);

        // 1. I permessi conclusivi riferiscono tentativo e credenziale: prima di entrambi.
        if (attempts.Count > 0)
        {
            await DeleteInAsync<AttemptFinalizationGrantEntity>(
                nameof(AttemptFinalizationGrantEntity.AttemptId),
                [.. attempts.Select(attempt => attempt.Id)],
                cancellationToken).ConfigureAwait(false);
        }

        // 2. Tentativi e stati desiderati: appartengono alla partecipazione e all'assegnazione.
        await DeleteAsync<ExerciseAttemptEntity>(
            row => row.SessionId == sessionId, cancellationToken).ConfigureAwait(false);
        await DeleteAsync<ParticipantStateEntity>(
            row => row.SessionId == sessionId, cancellationToken).ConfigureAwait(false);

        // 3. Le consegne riferiscono comando e partecipante, quindi precedono entrambi.
        if (commands.Count > 0)
        {
            await DeleteInAsync<CommandDeliveryEntity>(
                nameof(CommandDeliveryEntity.CommandId),
                [.. commands.Select(command => command.Id)],
                cancellationToken).ConfigureAwait(false);
        }

        // 4. Comandi, messaggi in coda e assegnazioni della sessione.
        await DeleteAsync<SessionCommandEntity>(
            row => row.SessionId == sessionId, cancellationToken).ConfigureAwait(false);
        await DeleteAsync<OutboxMessageEntity>(
            row => row.SessionId == sessionId, cancellationToken).ConfigureAwait(false);
        await DeleteAsync<ActivityAssignmentEntity>(
            row => row.SessionId == sessionId, cancellationToken).ConfigureAwait(false);

        // 5. Le credenziali dei partecipanti: la tabella non ha chiave esterna verso la
        //    partecipazione (0005), quindi si tolgono per identificativo, una riga per alunno.
        foreach (var participant in participants)
        {
            await DeleteAsync<CredentialSessionEntity>(
                row => row.ParticipantId == participant.Id, cancellationToken).ConfigureAwait(false);
        }

        // 6. Infine la partecipazione, che è il riferimento di tutto il resto.
        await DeleteAsync<SessionParticipantEntity>(
            row => row.SessionId == sessionId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<TEntity>> QueryAsync<TEntity>(
        System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken)
        where TEntity : class =>
        (await context.Connection
            .QueryAsync<TEntity>(predicate, transaction: context.Transaction!, cancellationToken: cancellationToken)
            .ConfigureAwait(false))
        .ToArray();

    private Task<int> DeleteAsync<TEntity>(
        System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken)
        where TEntity : class =>
        context.Connection.DeleteAsync<TEntity>(
            predicate, transaction: context.Transaction!, cancellationToken: cancellationToken);

    /// <summary>Eliminazione per elenco di valori: una sola istruzione al posto di una per riga.</summary>
    private Task<int> DeleteInAsync<TEntity>(
        string propertyName,
        IReadOnlyList<string> values,
        CancellationToken cancellationToken)
        where TEntity : class =>
        context.Connection.DeleteAsync<TEntity>(
            new QueryGroup(new QueryField(
                PropertyMappedNameCache.Get<TEntity>(propertyName), Operation.In, values)),
            transaction: context.Transaction!,
            cancellationToken: cancellationToken);
}
