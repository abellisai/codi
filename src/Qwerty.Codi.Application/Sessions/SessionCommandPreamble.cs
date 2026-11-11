using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Le due operazioni che aprono ogni comando di regia (§6.4, §8.2, §12.1): caricare la sessione con
/// le verifiche di proprietà, revisione attesa e stato non terminale, e far avanzare la revisione
/// quando lo stato desiderato cambia. Stanno insieme perché sono la stessa sequenza in ogni comando:
/// riscriverle significa poterle far divergere, e la revisione è ciò su cui si ordinano le mutazioni.
/// </summary>
internal static class SessionCommandPreamble
{
    /// <summary>
    /// Sessione della credenziale verificata, alla revisione attesa e non terminata. Un altro
    /// amministratore riceve una risorsa assente, non un rifiuto che riveli l'esistenza della
    /// sessione; senza revisione attesa il comando non è applicabile (428 di §10.2).
    /// </summary>
    public static async Task<LessonSession> LoadSessionAsync(
        IUnitOfWork work,
        Guid sessionId,
        Guid controllerAdminId,
        long expectedSessionRevision,
        string endedMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        var stored = await work.Repo<ILessonSessionRepository>()
            .GetAsync(sessionId.ToString("D"), cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var session = stored.ToDomain();

        if (session.ControllerAdminId != controllerAdminId)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        if (session.SessionRevision != expectedSessionRevision)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionRevisionConflict,
                "La sessione è cambiata dopo la lettura: ricaricare lo stato e ripetere il comando con la revisione corrente.");
        }

        if (session.Status == SessionStatus.Ended)
        {
            throw new CodiRequestException(CodiErrorCodes.SessionStateConflict, endedMessage);
        }

        return session;
    }

    /// <summary>
    /// Avanza la revisione della sessione e la persiste nella stessa transazione (§12.1). La verifica
    /// della revisione attesa è già avvenuta in lettura, quindi qui il dominio la ripete soltanto.
    /// </summary>
    public static async Task AdvanceRevisionAsync(
        IUnitOfWork work,
        LessonSession session,
        long expectedSessionRevision,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(session);

        session.RegisterDesiredStateChange(expectedSessionRevision, nowUtc);

        if (!await work.Repo<ILessonSessionRepository>()
                .UpdateAsync(LessonSessionEntity.FromDomain(session), cancellationToken)
                .ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }
    }

    /// <summary>Scrive lo stato desiderato di un destinatario, pretendendo la riga che ha letto.</summary>
    public static async Task UpdateStateAsync(
        IUnitOfWork work,
        ParticipantStateEntity state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(state);

        if (!await work.Repo<IParticipantStateRepository>()
                .UpdateAsync(state, cancellationToken)
                .ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }
    }
}
