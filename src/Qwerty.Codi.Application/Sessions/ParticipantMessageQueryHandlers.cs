using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Thread dell'alunno letto dal docente (§8, §15.1). La proprietà della sessione si verifica come
/// nelle altre letture di regia, e la lettura azzera il conteggio dei non letti: è l'unico effetto
/// osservabile della chat, perché «non letti» significa appunto «non ancora letti dal docente».
/// </summary>
public sealed class GetParticipantMessagesHandler(
    IUnitOfWorkContext unitOfWorkContext,
    IParticipantMessageStore messages)
    : IRequestHandler<GetParticipantMessagesQuery, ParticipantThreadDto>
{
    public async ValueTask<ParticipantThreadDto> Handle(
        GetParticipantMessagesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } controllerAdminId)
        {
            throw CodiRequestException.AuthenticationRequired(GetParticipantMessagesQuery.RequestName);
        }

        await using var work = await unitOfWorkContext.CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await SessionGridGuard
            .LoadOwnedSessionAsync(work, request.SessionId, controllerAdminId, cancellationToken)
            .ConfigureAwait(false);

        var participant = await SessionParticipantRegiaRules
            .LoadParticipantAsync(work, request.SessionId, request.ParticipantId, cancellationToken)
            .ConfigureAwait(false);

        if (!participant.IsAdmitted)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        return messages.Read(request.ParticipantId, markRead: true);
    }
}

/// <summary>
/// Thread letto dall'alunno (§8, §15.1): solo il proprio, perché l'identità arriva dalla credenziale
/// verificata (§9.3). Il conteggio dei non letti non si azzera: il server non misura se un bambino ha
/// letto, e mostrerebbe un numero che non ha misurato.
/// </summary>
public sealed class GetOwnMessagesHandler(IParticipantMessageStore messages)
    : IRequestHandler<GetOwnMessagesQuery, ParticipantThreadDto>
{
    public async ValueTask<ParticipantThreadDto> Handle(
        GetOwnMessagesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var participantId = request.Context.ParticipantId
            ?? throw CodiRequestException.AuthenticationRequired(GetOwnMessagesQuery.RequestName);

        // Il thread è lo stesso, ma il conteggio dei non letti è una misura dello stato di lettura del
        // docente: per l'alunno non ha significato, quindi la sua vista riporta zero.
        var thread = messages.Read(participantId, markRead: false);
        return thread with { UnreadByTeacher = 0 };
    }
}
