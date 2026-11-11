using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Messaggio del docente verso un alunno (§8, §15.1). Nessuna transazione: il thread vive in memoria
/// e non si persiste, quindi qui non c'è revisione da controllare né idempotenza da scrivere nel
/// database. La chiave del client serve solo a non duplicare un messaggio rienviato.
///
/// Il caso d'uso verifica le tre condizioni che il trasporto non può decidere da solo: la sessione
/// appartiene al docente che scrive, il partecipante appartiene a quella sessione, e la sessione non
/// è terminata (dopo il termine il thread viene eliminato, §15.1).
/// </summary>
public sealed class PostTeacherMessageHandler(
    IUnitOfWorkContext unitOfWorkContext,
    IParticipantMessageStore messages,
    IParticipantMessageNotifier notifier,
    TimeProvider timeProvider)
    : IRequestHandler<PostTeacherMessageCommand, ParticipantMessageDto>
{
    public async ValueTask<ParticipantMessageDto> Handle(
        PostTeacherMessageCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } controllerAdminId)
        {
            throw CodiRequestException.AuthenticationRequired(PostTeacherMessageCommand.RequestName);
        }

        var text = ChatRules.Validate(request.Text);
        var key = ChatRules.ValidateKey(request.ClientMessageKey);

        await using var work = await unitOfWorkContext.CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var session = await SessionGridGuard
            .LoadOwnedSessionAsync(work, request.SessionId, controllerAdminId, cancellationToken)
            .ConfigureAwait(false);

        if (session.Status == LessonSessionEntity.EndedStatusText)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionStateConflict,
                "La sessione è terminata: la chat con gli alunni non è più aperta.");
        }

        var participant = await SessionParticipantRegiaRules
            .LoadParticipantAsync(work, request.SessionId, request.ParticipantId, cancellationToken)
            .ConfigureAwait(false);

        if (!participant.IsAdmitted)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionStateConflict,
                "Il partecipante è escluso: la chat con lui non è più aperta.");
        }

        var message = new ParticipantMessageDto(
            Guid.NewGuid(),
            request.SessionId,
            request.ParticipantId,
            MessageAuthor.Teacher.ToString(),
            text,
            timeProvider.GetUtcNow());

        var stored = messages.Append(message, key) ?? throw ChatRules.ThreadFull();

        // Solo la scrittura nuova arriva al destinatario: una ripetizione con la stessa chiave è la stessa
        // richiesta rienviata, non un messaggio da mostrare due volte. Il docente scrive verso l'alunno.
        if (stored.Created)
        {
            await notifier.SendToParticipantAsync(stored.Message, cancellationToken).ConfigureAwait(false);
        }

        return stored.Message;
    }
}

/// <summary>
/// Messaggio dell'alunno verso il docente (§8, §15.1). L'identità e la sessione vengono dalla
/// credenziale e dal registro delle connessioni, non dal payload (§9.3).
///
/// La chat segue la pagina: durante la pausa docente o l'attesa i controlli degli alunni sono
/// disabilitati (§14.3), e la decisione dell'utente del 13 ottobre 2026 è che la chat si disabiliti
/// insieme a loro. Il campo disabilitato nella UI non basta: il server rifiuta comunque la scrittura,
/// perché un client non allineato non deve poter scrivere mentre la pagina è ferma.
/// </summary>
public sealed class PostParticipantMessageHandler(
    IUnitOfWorkContext unitOfWorkContext,
    IParticipantConnectionRegistry connections,
    IParticipantMessageStore messages,
    IParticipantMessageNotifier notifier,
    TimeProvider timeProvider)
    : IRequestHandler<PostParticipantMessageCommand, ParticipantMessageDto>
{
    public async ValueTask<ParticipantMessageDto> Handle(
        PostParticipantMessageCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = PostParticipantMessageCommand.RequestName;
        var participantId = request.Context.ParticipantId
            ?? throw CodiRequestException.AuthenticationRequired(name);

        var text = ChatRules.Validate(request.Text);
        var key = ChatRules.ValidateKey(request.ClientMessageKey);

        var connection = connections.Get(participantId)
            ?? throw CodiRequestException.ResourceNotFound();

        await using var work = await unitOfWorkContext.CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var participant = await SessionParticipantRegiaRules
            .LoadParticipantAsync(work, connection.SessionId, participantId, cancellationToken)
            .ConfigureAwait(false);

        // Qui la sessione non è quella di un docente: è quella della credenziale del partecipante,
        // risolta dal trasporto. Il controllo di proprietà di §6.4 non si applica, e non c'è un
        // amministratore da confrontare.
        var sessionRow = await work.Repo<ILessonSessionRepository>()
            .GetAsync(connection.SessionId.ToString("D"), cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var session = sessionRow.ToDomain();

        // La sessione terminata non ammette più messaggi: il thread viene eliminato (§15.1).
        if (session.Status == SessionStatus.Ended)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionStateConflict,
                "La lezione è terminata: non si può più scrivere alla chat.");
        }

        var state = await work.Repo<IParticipantStateRepository>()
            .GetAsync(participantId.ToString("D"), cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        if (state.View == ParticipantStateEntity.WaitingViewText || state.TeacherPausedFlag != 0)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionStateConflict,
                "La chat è ferma insieme alla pagina: il maestro deve riprendere il lavoro.");
        }

        var message = new ParticipantMessageDto(
            Guid.NewGuid(),
            connection.SessionId,
            participantId,
            MessageAuthor.Participant.ToString(),
            text,
            timeProvider.GetUtcNow());

        var stored = messages.Append(message, key) ?? throw ChatRules.ThreadFull();

        // Il messaggio dell'alunno va al docente che osserva la sessione. Anche qui solo la scrittura
        // nuova viene avvisata.
        if (stored.Created)
        {
            await notifier.SendToTeachersAsync(stored.Message, cancellationToken).ConfigureAwait(false);
        }

        return stored.Message;
    }
}

/// <summary>
/// Regole condivise dai due messaggi: le stesse condizioni di forma del testo e della chiave, e lo
/// stesso rifiuto quando il thread è pieno. Un messaggio non è un comando di regia, quindi non ha
/// revisione né consegne: la sola idempotenza è la chiave del client, conservata in memoria.
/// </summary>
internal static class ChatRules
{
    /// <summary>Lunghezza massima della chiave dichiarata dal client, come le altre chiavi di §12.2.</summary>
    private const int MaxClientKeyLength = 128;

    public static string Validate(string? value)
    {
        if (!ParticipantMessageText.IsUsable(value))
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                $"Il messaggio deve avere da 1 a {ParticipantMessageText.MaxGraphemes} caratteri e nessun carattere di controllo.");
        }

        return ParticipantMessageText.Trim(value!);
    }

    public static string ValidateKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxClientKeyLength)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                "Manca la chiave del messaggio dichiarata dal client.");
        }

        return value.Trim();
    }

    public static CodiRequestException ThreadFull() =>
        new(CodiErrorCodes.MessageThreadFull,
            "Il thread di questo alunno è pieno: il messaggio non è stato conservato.");
}
