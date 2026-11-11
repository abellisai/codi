using Qwerty.Codi.Application.Dto;
using System.Text.Json;
using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Registra la conferma applicativa di un comando consegnato (§12.2). La conferma arriva dal client
/// che ha applicato lo stato, e riguarda **la sua** consegna: nessun partecipante può confermare al
/// posto di un altro.
///
/// Gli esiti di §12.2 hanno tre effetti diversi. <c>Applied</c> porta la consegna ad applicata;
/// <c>AppliedWithRecoveryRequired</c> la porta ad applicata **e** materializza la pausa di recupero
/// nello stato del partecipante, perché il client non è certo di ciò che ha eseguito (§12.3);
/// <c>RejectedStale</c> non tocca lo stato e segna la consegna come superata **solo** se il server
/// conferma che la revisione è obsoleta — altrimenti il client ha rifiutato qualcosa che il server
/// considera ancora corrente, e la consegna resta quella che era; <c>Failed</c> la porta a fallita.
///
/// Un esito negativo non cancella nulla e non fa tornare indietro una consegna già applicata:
/// ripetere la conferma di un comando applicato restituisce lo stato reale senza riscrivere.
/// </summary>
public sealed class AcknowledgeCommandHandler(
    IUnitOfWorkContext unitOfWorkContext,
    TimeProvider timeProvider, ISessionCoordinator coordinator) : IRequestHandler<AcknowledgeCommandCommand, CommandAcknowledgementDto>
{
    public async ValueTask<CommandAcknowledgementDto> Handle(
        AcknowledgeCommandCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.ParticipantId is not { } participantId)
        {
            throw CodiRequestException.AuthenticationRequired(AcknowledgeCommandCommand.RequestName);
        }

        EnsureSupportedProtocol(request.ProtocolVersion);
        EnsureKnownOutcome(request.Outcome);

        var nowUtc = timeProvider.GetUtcNow();
        var participantKey = participantId.ToString("D");

        Guid sessionId;
        await using (var read = await unitOfWorkContext.CreateAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            var row = await read.Repo<ISessionParticipantRepository>().GetAsync(participantKey, cancellationToken).ConfigureAwait(false)
                ?? throw CodiRequestException.ResourceNotFound();
            sessionId = Guid.Parse(row.SessionId);
        }
        await using var coordination = await coordinator.AcquireAsync(sessionId, cancellationToken).ConfigureAwait(false);
        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var participantRow = await work.Repo<ISessionParticipantRepository>()
            .GetAsync(participantKey, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var participant = participantRow.ToDomain();

        // L'epoca è un ordinamento, non un segreto (§12.5): una conferma che dichiara un'epoca
        // superata arriva da una pagina che ha perso l'operatività, e accettarla riabilita una
        // connessione che il server ha già sostituito.
        if (participant.ConnectionEpoch != request.ConnectionEpoch)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionStateConflict,
                "La connessione che ha confermato il comando non è più quella corrente.");
        }

        var commandRow = await work.Repo<ISessionCommandRepository>()
            .GetAsync(request.CommandId.ToString("D"), cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        if (!string.Equals(commandRow.SessionId, participant.SessionId.ToString("D"), StringComparison.Ordinal))
        {
            // Un comando di un'altra sessione non è una risorsa di questo partecipante, e il rifiuto
            // non dice se esista altrove (§10.7).
            throw CodiRequestException.ResourceNotFound();
        }

        var deliveries = work.Repo<ICommandDeliveryRepository>();
        var delivery = await FindDeliveryAsync(deliveries, request.CommandId, participantKey, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        if (delivery.EffectiveRevision != request.EffectiveRevision)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                "La conferma dichiara una revisione diversa da quella del comando consegnato.");
        }

        var state = await work.Repo<IParticipantStateRepository>()
            .GetAsync(participantKey, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var acknowledgedAtUtcMs = nowUtc.ToUnixTimeMilliseconds();

        // Lo stato restituito si decide qui: dopo il commit la unit of work non è più utilizzabile, e
        // una seconda lettura aprirebbe un'altra operazione per un dato che è già stato letto.
        string status;
        var requiresRecovery = false;

        switch (request.Outcome)
        {
            case CommandAcknowledgementOutcomes.Applied:
                status = await deliveries
                    .MarkAppliedAsync(request.CommandId, participantId, acknowledgedAtUtcMs, requiresRecovery: false, cancellationToken)
                    .ConfigureAwait(false)
                    ? CommandDeliveryStates.Applied
                    : delivery.Status;
                break;

            case CommandAcknowledgementOutcomes.AppliedWithRecoveryRequired:
                var applied = await deliveries
                    .MarkAppliedAsync(request.CommandId, participantId, acknowledgedAtUtcMs, requiresRecovery: true, cancellationToken)
                    .ConfigureAwait(false);
                status = applied ? CommandDeliveryStates.Applied : delivery.Status;
                requiresRecovery = applied || delivery.RequiresRecoveryFlag != 0;

                if (applied && state.EffectiveRevision == request.EffectiveRevision)
                {
                    await MaterializeRecoveryAsync(work, state, nowUtc, cancellationToken).ConfigureAwait(false);
                }

                break;

            case CommandAcknowledgementOutcomes.RejectedStale:
                status = await RejectStaleAsync(deliveries, request, participantId, delivery, state, cancellationToken)
                    .ConfigureAwait(false)
                    ? CommandDeliveryStates.Superseded
                    : delivery.Status;
                break;

            default:
                status = await deliveries
                    .MarkFailedAsync(request.CommandId, participantId, acknowledgedAtUtcMs, request.ErrorCode, cancellationToken)
                    .ConfigureAwait(false)
                    ? CommandDeliveryStates.Failed
                    : delivery.Status;
                break;
        }

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new CommandAcknowledgementDto(request.CommandId, status, requiresRecovery);
    }

    private static async Task<CommandDeliveryEntity?> FindDeliveryAsync(
        ICommandDeliveryRepository deliveries,
        Guid commandId,
        string participantKey,
        CancellationToken cancellationToken)
    {
        var rows = await deliveries.QueryByCommandAsync(commandId, cancellationToken).ConfigureAwait(false);

        return rows.SingleOrDefault(row => string.Equals(row.ParticipantId, participantKey, StringComparison.Ordinal));
    }

    /// <summary>
    /// Un client che non è certo di ciò che ha eseguito sospende il proprio lavoro (§12.3): lo stato
    /// del partecipante entra in pausa di recupero e l'interazione si chiude. Il docente sceglie poi
    /// se riprendere con un checkpoint o riavviare.
    /// </summary>
    private static async Task MaterializeRecoveryAsync(
        IUnitOfWork work,
        ParticipantStateEntity state,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        state.ResumeContextJson ??= JsonSerializer.Serialize(DesiredStateProjection.Create(state), CodiJson.Payload);
        state.RecoveryBlockedFlag = 1;
        state.InteractionEnabledFlag = 0;
        state.UpdatedAtUtcMs = nowUtc.ToUnixTimeMilliseconds();

        if (!await work.Repo<IParticipantStateRepository>().UpdateAsync(state, cancellationToken).ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }
    }

    /// <summary>
    /// Il client dichiara superato il comando. Il server non gli crede sulla parola: marca la
    /// consegna come superata solo se lo stato del partecipante è davvero avanti rispetto alla
    /// revisione del comando (§12.2). Altrimenti non scrive nulla: il client non ha applicato un
    /// comando che il server considera ancora corrente, e la consegna resta quella che era — sarà il
    /// tempo, non una dichiarazione, a dire che la conferma non è arrivata.
    /// </summary>
    private static async Task<bool> RejectStaleAsync(
        ICommandDeliveryRepository deliveries,
        AcknowledgeCommandCommand request,
        Guid participantId,
        CommandDeliveryEntity delivery,
        ParticipantStateEntity state,
        CancellationToken cancellationToken)
    {
        if (state.EffectiveRevision <= request.EffectiveRevision)
        {
            return false;
        }

        return await deliveries
            .MarkSupersededAsync(request.CommandId, participantId, delivery.Status, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void EnsureSupportedProtocol(int protocolVersion)
    {
        if (protocolVersion != ParticipantProtocol.CurrentVersion)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                $"Il protocollo {protocolVersion} non è supportato: il client deve aggiornarsi alla versione {ParticipantProtocol.CurrentVersion}.");
        }
    }

    /// <summary>
    /// Un esito fuori dall'elenco di §12.2 è un difetto del client, non una conferma da registrare:
    /// accettarlo come «fallito» nasconderebbe un client non allineato.
    /// </summary>
    private static void EnsureKnownOutcome(string outcome)
    {
        if (string.IsNullOrWhiteSpace(outcome) ||
            !Outcomes.Contains(outcome, StringComparer.Ordinal))
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                "L'esito della conferma non è fra quelli ammessi dal protocollo.");
        }
    }

    private static readonly string[] Outcomes =
    [
        CommandAcknowledgementOutcomes.Applied,
        CommandAcknowledgementOutcomes.AppliedWithRecoveryRequired,
        CommandAcknowledgementOutcomes.RejectedStale,
        CommandAcknowledgementOutcomes.Failed,
    ];
}
