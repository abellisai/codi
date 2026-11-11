using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Stato delle consegne di un comando (Â§10.6): legge il comando, ne verifica la sessione e la
/// proprietÃ , poi restituisce il riepilogo e l'elenco per destinatario.
///
/// Un comando che non esiste, che appartiene a un'altra sessione o la cui sessione Ã¨ di un altro
/// amministratore Ã¨ indistinguibile da una risorsa assente: non si distingue Â«non esisteÂ» da Â«non Ã¨
/// tuoÂ». Il riepilogo ha le tre caselle di Â§10.6 â€” in attesa, applicate, fallite â€” mentre una
/// consegna superata resta soltanto nell'elenco.
/// </summary>
public sealed class GetCommandStatusHandler(IUnitOfWorkContext unitOfWorkContext)
    : IRequestHandler<GetCommandStatusQuery, CommandStatusDto>
{
    public async ValueTask<CommandStatusDto> Handle(
        GetCommandStatusQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } controllerAdminId)
        {
            throw CodiRequestException.AuthenticationRequired(GetCommandStatusQuery.RequestName);
        }

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var sessionRow = await work.Repo<ILessonSessionRepository>()
            .GetAsync(request.SessionId.ToString("D"), cancellationToken)
            .ConfigureAwait(false);

        if (sessionRow is null || sessionRow.ToDomain().ControllerAdminId != controllerAdminId)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var commandRow = await work.Repo<ISessionCommandRepository>()
            .GetAsync(request.CommandId.ToString("D"), cancellationToken)
            .ConfigureAwait(false);

        if (commandRow is null ||
            !string.Equals(commandRow.SessionId, request.SessionId.ToString("D"), StringComparison.Ordinal))
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var deliveryRows = await work.Repo<ICommandDeliveryRepository>()
            .QueryByCommandAsync(request.CommandId, cancellationToken)
            .ConfigureAwait(false);

        var deliveries = deliveryRows.Select(ToDto).ToArray();

        return new CommandStatusDto(
            Guid.Parse(commandRow.Id),
            Guid.Parse(commandRow.SessionId),
            commandRow.CommandName,
            commandRow.Revision,
            Summarize(deliveryRows),
            deliveries);
    }

    internal static CommandDeliveryDto ToDto(CommandDeliveryEntity delivery) => new(
        Guid.Parse(delivery.ParticipantId),
        delivery.Status,
        delivery.EffectiveRevision,
        delivery.RequiresRecoveryFlag != 0,
        checked((int)delivery.SendAttempts),
        ToInstant(delivery.SentAtUtcMs),
        ToInstant(delivery.AckAtUtcMs),
        ToInstant(delivery.AppliedAtUtcMs),
        delivery.ErrorCode);

    /// <summary>
    /// Le tre caselle di Â§10.6: in attesa comprende anche inviata e non confermata, perchÃ© in nessuno
    /// dei due casi il client ha confermato l'applicazione. Una consegna superata non Ã¨ conteggiata.
    /// </summary>
    internal static DeliverySummaryDto Summarize(IReadOnlyList<CommandDeliveryEntity> deliveries)
    {
        var pending = deliveries.Count(delivery => delivery.Status is
            CommandDeliveryStates.Pending or CommandDeliveryStates.Sent or CommandDeliveryStates.Unconfirmed);
        var applied = deliveries.Count(delivery => delivery.Status == CommandDeliveryStates.Applied);
        var failed = deliveries.Count(delivery => delivery.Status == CommandDeliveryStates.Failed);

        return new DeliverySummaryDto(pending, applied, failed);
    }

    private static DateTimeOffset? ToInstant(long? epochMs) =>
        epochMs is { } value ? DateTimeOffset.FromUnixTimeMilliseconds(value) : null;
}
