using Qwerty.Codi.Application.Dto;
using System.Text.Json;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>Effetto di un comando che cambia lo stato desiderato, pronto per essere registrato.</summary>
internal sealed record DesiredStateCommandEffect(
    Guid ControllerAdminId,
    string CommandName,
    Guid? TargetParticipantId,
    DesiredStateDto DesiredState,
    IReadOnlyList<ParticipantStateEntity> Targets,
    DateTimeOffset NowUtc, string? PreviousClassDefaultJson = null);

/// <summary>
/// Registrazione di un comando che ha già cambiato lo stato desiderato (§12.1): il comando salvato
/// con la sua revisione, una consegna per destinatario e il messaggio in coda — nella **stessa**
/// transazione del cambiamento, così il processo può cadere dopo il commit e il dispatcher outbox
/// ritrova comunque il lavoro pendente (§12.2). La notifica al dispatcher avviene dopo il commit:
/// segnalare prima significherebbe consegnare un comando che una transazione potrebbe ancora
/// annullare.
///
/// Sta in un punto solo perché è la stessa sequenza in ogni comando di stato desiderato: replicarla
/// significherebbe poter dimenticare una delle tre scritture in uno di essi.
/// </summary>
internal static class DesiredStateCommandRegistration
{
    public static async Task<CommandAcceptedDto> RegisterAsync(
        IUnitOfWork work,
        IOutboxSignal outboxSignal,
        LessonSession session,
        DesiredStateCommandEffect effect,
        CancellationToken cancellationToken, bool commit = true)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(outboxSignal);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(effect);

        await ClassActivityState.UpdateAsync(work, session, effect, cancellationToken).ConfigureAwait(false);
        var command = new SessionCommandEntity
        {
            Id = Guid.NewGuid().ToString("D"),
            SessionId = session.Id.ToString("D"),
            IssuedByAdminId = effect.ControllerAdminId.ToString("D"),
            CommandName = effect.CommandName,
            Scope = effect.TargetParticipantId is null ? CommandScopes.Class : CommandScopes.Participant,
            TargetParticipantId = effect.TargetParticipantId?.ToString("D"),
            Revision = session.SessionRevision,
            PayloadJson = JsonSerializer.Serialize(effect.DesiredState, CodiJson.Payload),
            IssuedAtUtcMs = effect.NowUtc.ToUnixTimeMilliseconds(),
        };

        await work.Repo<ISessionCommandRepository>()
            .InsertAsync(command, cancellationToken)
            .ConfigureAwait(false);

        var deliveries = effect.Targets
            .Select(state => new CommandDeliveryEntity
            {
                CommandId = command.Id,
                ParticipantId = state.Id,
                EffectiveRevision = session.SessionRevision,
                Status = CommandDeliveryStates.Pending,
                RequiresRecoveryFlag = 0,
                SendAttempts = 0,
            })
            .ToArray();

        await work.Repo<ICommandDeliveryRepository>()
            .InsertAllAsync(deliveries, cancellationToken)
            .ConfigureAwait(false);

        var message = new CommandIssuedMessageDto(
            CommandIssuedMessageDto.CurrentProtocolVersion,
            Guid.Parse(command.Id),
            session.Id,
            session.SessionRevision,
            effect.NowUtc,
            effect.CommandName,
            effect.DesiredState)
        {
            DesiredStates = effect.Targets.ToDictionary(state => Guid.Parse(state.Id), DesiredStateProjection.Create),
        };

        await work.Repo<IOutboxMessageRepository>()
            .InsertAsync(
                new OutboxMessageEntity
                {
                    Id = Guid.NewGuid().ToString("D"),
                    SessionId = session.Id.ToString("D"),
                    MessageType = CommandIssuedMessageDto.MessageType,
                    PayloadJson = JsonSerializer.Serialize(message, CodiJson.Payload),
                    AvailableAtUtcMs = effect.NowUtc.ToUnixTimeMilliseconds(),
                    AttemptCount = 0,
                },
                cancellationToken)
            .ConfigureAwait(false);

        var response = new CommandAcceptedDto(
            Guid.Parse(command.Id),
            session.Id,
            session.SessionRevision,
            new DeliverySummaryDto(deliveries.Length, Applied: 0, Failed: 0),
            new CommandStatusReferenceDto(
                GetCommandStatusQuery.RequestName,
                new CommandStatusRequestDto(session.Id, Guid.Parse(command.Id))));
        if (commit)
        {
            await SessionCommandOperation.CompleteAsync(work, response, cancellationToken).ConfigureAwait(false);
            outboxSignal.Notify();
        }
        return response;
    }
}
