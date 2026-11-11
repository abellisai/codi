using Qwerty.Codi.Application.Dto;
using System.Text.Json;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Esito di un giro del dispatcher: quanti messaggi sono stati conclusi, quante consegne inviate e
/// quante superate. Non è «l'alunno ha applicato»: quello arriva con l'ACK.
/// </summary>
public sealed record OutboxDispatchResult(int MessagesProcessed, int DeliveriesSent, int DeliveriesSuperseded)
{
    /// <summary>
    /// Consegne inviate da troppo tempo e mai confermate (§12.2, attesa di cinque secondi). Non sono
    /// fallite: il docente le vede come «in attesa di conferma», e un ACK tardivo vale ancora.
    /// </summary>
    public int DeliveriesUnconfirmed { get; init; }
}

/// <summary>
/// Dispatcher outbox (§12.1–§12.2): legge i messaggi già decisi, risolve i destinatari dalle loro
/// consegne e li invia al trasporto, quindi registra l'esito. È distinto dal dispatcher CQRS di
/// §10.1, che instrada le richieste: qui si distribuisce ciò che è **già persistito**.
///
/// Ordine deliberato: la lettura, poi l'invio, poi la scrittura. Nessuna attesa di rete dentro una
/// transazione; se il processo cade fra l'invio e la scrittura, il messaggio resta da processare e
/// verrà inviato di nuovo — §12.2 ammette notifiche ripetute, purché `commandId` e revisione non
/// cambino. L'invio si conferma una sola volta: la consegna passa da `Pending` a `Sent` con un
/// aggiornamento condizionato, quindi due dispatcher non la contano due volte.
///
/// Prima di inviare si verifica che la revisione sia ancora pertinente: se la sessione è avanzata, il
/// comando è **superato** e le sue consegne non si applicano al nuovo stato (§12.2, §12.3).
///
/// Due ritentativi brevi riguardano soltanto connessioni operative senza conferma.
/// I guasti del trasporto rimandano il messaggio con attesa crescente fino a trenta secondi.
/// </summary>
public sealed class OutboxDispatcher(
    IUnitOfWorkContext unitOfWorkContext,
    ICommandDeliveryNotifier notifier,
    TimeProvider timeProvider, IParticipantConnectionRegistry? connections = null)
{
    /// <summary>Messaggi conclusi in un giro: un lotto piccolo non tiene in memoria una coda lunga.</summary>
    public const int BatchSize = 20;

    /// <summary>Consegne da evidenziare in un giro: il conteggio non cresce con l'aula.</summary>
    private const int OverdueBatchSize = 50;

    public async Task<OutboxDispatchResult> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        IReadOnlyList<OutboxMessageEntity> messages;
        await using (var read = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false))
        {
            messages = await read
                .Repo<IOutboxMessageRepository>()
                .QueryAvailableAsync(now.ToUnixTimeMilliseconds(), BatchSize, cancellationToken)
                .ConfigureAwait(false);
        }

        var processed = 0;
        var sent = 0;
        var superseded = 0;

        foreach (var message in messages)
        {
            var outcome = await DispatchAsync(message, now, cancellationToken).ConfigureAwait(false);
            if (outcome is null)
            {
                // Un altro dispatcher l'ha già concluso: non è un errore e non si invia di nuovo.
                continue;
            }

            processed++;
            sent += outcome.Value.Sent;
            superseded += outcome.Value.Superseded;
        }

        var unconfirmed = await MarkUnconfirmedAsync(now, cancellationToken).ConfigureAwait(false);

        return new OutboxDispatchResult(processed, sent, superseded) { DeliveriesUnconfirmed = unconfirmed };
    }

    /// <summary>
    /// Evidenzia le consegne inviate entro l'istante indicato e mai confermate (§12.2: l'attesa è di
    /// cinque secondi). È un giro indipendente dai messaggi, perché una consegna resta senza conferma
    /// anche quando il messaggio che l'ha prodotta è già concluso: è proprio il caso che il docente
    /// deve poter vedere invece di leggere «inviata» e crederla applicata.
    ///
    /// Non è una condanna: un ACK tardivo porta la consegna ad applicata (§12.2), quindi la transizione
    /// è condizionata allo stato ancora inviato e non tocca ciò che è già stato applicato.
    /// </summary>
    private async Task<int> MarkUnconfirmedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var threshold = now
            .Subtract(ParticipantProtocol.CommandAcknowledgementTimeout)
            .ToUnixTimeMilliseconds();

        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var deliveries = work.Repo<ICommandDeliveryRepository>();
        var overdue = await deliveries
            .QueryUnconfirmedAsync(threshold, OverdueBatchSize, cancellationToken)
            .ConfigureAwait(false);

        var marked = 0;
        foreach (var delivery in overdue)
        {
            if (await deliveries
                    .MarkUnconfirmedAsync(
                        Guid.Parse(delivery.CommandId),
                        Guid.Parse(delivery.ParticipantId),
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                marked++;
            }
        }

        if (marked > 0)
        {
            await work.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return marked;
    }

    /// <summary>
    /// Un messaggio: lettura, decisione, invio e registrazione. Restituisce <c>null</c> se un altro
    /// dispatcher ha concluso il messaggio per primo, o se il trasporto ha fallito: in entrambi i
    /// casi non c'è nulla da contare e il messaggio, se serve, verrà ritentato.
    /// </summary>
    private async Task<(int Sent, int Superseded)?> DispatchAsync(
        OutboxMessageEntity message,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!TryReadDocument(message, out var document))
        {
            // Un messaggio illeggibile è un difetto interno: si scarta con il motivo, senza
            // ritentarlo per sempre.
            await MarkProcessedAsync(message, now, CodiErrorCodes.InvalidPayload, cancellationToken)
                .ConfigureAwait(false);

            return (0, 0);
        }

        IReadOnlyList<CommandDeliveryEntity> deliveries;
        var obsolete = new HashSet<Guid>();
        await using (var read = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false))
        {
            deliveries = await read
                .Repo<ICommandDeliveryRepository>()
                .QueryByCommandAsync(document.CommandId, cancellationToken)
                .ConfigureAwait(false);

            foreach (var delivery in deliveries)
            {
                var state = await read.Repo<IParticipantStateRepository>().GetAsync(delivery.ParticipantId, cancellationToken).ConfigureAwait(false);
                if (state is null || state.EffectiveRevision > document.EffectiveRevision)
                    obsolete.Add(Guid.Parse(delivery.ParticipantId));
            }
        }

        var sentParticipants = new List<Guid>();

        {
            foreach (var delivery in deliveries.Where(row => row.Status == CommandDeliveryStates.Pending ||
                message.AttemptCount > 0 && row.Status == CommandDeliveryStates.Sent && row.SendAttempts < 3))
            {
                if (obsolete.Contains(Guid.Parse(delivery.ParticipantId))) continue;
                if (message.AttemptCount > 0 && connections?.Get(Guid.Parse(delivery.ParticipantId)) is not
                    { SnapshotApplied: true, Presence: not ParticipantPresence.NotOperational }) continue;
                try
                {
                    await notifier
                        .SendCommandIssuedAsync(Guid.Parse(delivery.ParticipantId), document, cancellationToken)
                        .ConfigureAwait(false);

                    sentParticipants.Add(Guid.Parse(delivery.ParticipantId));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Il trasporto ha rifiutato la consegna. Il messaggio resta in coda con
                    // attesa esponenziale, fino a trenta secondi fra tentativi.
                    await using var retry = await unitOfWorkContext.CreateAsync(useTransaction: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                    await retry.Repo<IOutboxMessageRepository>().RescheduleAsync(Guid.Parse(message.Id),
                        now.AddSeconds(Math.Min(30, Math.Pow(2, Math.Min(message.AttemptCount, 5)))).ToUnixTimeMilliseconds(),
                        checked(message.AttemptCount + 1), "TransportUnavailable", cancellationToken).ConfigureAwait(false);
                    await retry.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return null;
                }
            }
        }

        return await RecordAsync(message, document, deliveries, sentParticipants, obsolete, now, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Registra l'esito del tentativo: le consegne inviate passano a <c>Sent</c>, quelle di un
    /// comando superato a <c>Superseded</c>, e il messaggio risulta concluso. Tutto in una
    /// transazione, come le due scritture che descrivono lo stesso fatto.
    /// </summary>
    private async Task<(int Sent, int Superseded)?> RecordAsync(
        OutboxMessageEntity message,
        CommandIssuedMessageDto document,
        IReadOnlyList<CommandDeliveryEntity> deliveries,
        IReadOnlyList<Guid> sentParticipants,
        IReadOnlySet<Guid> obsolete,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var nowMs = now.ToUnixTimeMilliseconds();
        var sent = 0;
        var superseded = 0;

        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var commands = work.Repo<ICommandDeliveryRepository>();

        foreach (var delivery in deliveries)
        {
            var participantId = Guid.Parse(delivery.ParticipantId);

            if (obsolete.Contains(participantId) && delivery.Status is CommandDeliveryStates.Pending or CommandDeliveryStates.Sent or CommandDeliveryStates.Unconfirmed)
            {
                if (await commands
                    .MarkSupersededAsync(document.CommandId, participantId, delivery.Status, cancellationToken)
                    .ConfigureAwait(false))
                {
                    superseded++;
                }

                continue;
            }

            if (sentParticipants.Contains(participantId) &&
                await commands
                    .MarkSentAsync(document.CommandId, participantId, delivery.SentAtUtcMs ?? nowMs, delivery.SendAttempts, cancellationToken)
                    .ConfigureAwait(false))
            {
                sent++;
            }
        }

        var processed = await work
            .Repo<IOutboxMessageRepository>()
            .MarkProcessedAsync(
                Guid.Parse(message.Id),
                nowMs,
                checked(message.AttemptCount + 1),
                errorCode: null,
                cancellationToken)
            .ConfigureAwait(false);

        if (processed && message.AttemptCount < 2 && connections is not null &&
            deliveries.Any(row => sentParticipants.Contains(Guid.Parse(row.ParticipantId)) && row.SendAttempts < 2 &&
                connections.Get(Guid.Parse(row.ParticipantId)) is { SnapshotApplied: true, Presence: not ParticipantPresence.NotOperational }))
        {
            await work.Repo<IOutboxMessageRepository>().InsertAsync(new OutboxMessageEntity
            {
                Id = Guid.NewGuid().ToString("D"),
                SessionId = message.SessionId,
                MessageType = message.MessageType,
                PayloadJson = message.PayloadJson,
                AvailableAtUtcMs = now.AddSeconds(1).ToUnixTimeMilliseconds(),
                AttemptCount = message.AttemptCount + 1,
            }, cancellationToken).ConfigureAwait(false);
        }
        await work.CommitAsync(cancellationToken).ConfigureAwait(false);

        return processed ? (sent, superseded) : null;
    }

    private async Task MarkProcessedAsync(
        OutboxMessageEntity message,
        DateTimeOffset now,
        string errorCode,
        CancellationToken cancellationToken)
    {
        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await work
            .Repo<IOutboxMessageRepository>()
            .MarkProcessedAsync(
                Guid.Parse(message.Id),
                now.ToUnixTimeMilliseconds(),
                checked(message.AttemptCount + 1),
                errorCode,
                cancellationToken)
            .ConfigureAwait(false);

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Legge il documento del messaggio: un payload illeggibile o di tipo diverso è un difetto.</summary>
    private static bool TryReadDocument(OutboxMessageEntity message, out CommandIssuedMessageDto document)
    {
        document = null!;

        if (!string.Equals(message.MessageType, CommandIssuedMessageDto.MessageType, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var read = JsonSerializer.Deserialize<CommandIssuedMessageDto>(message.PayloadJson, CodiJson.Payload);

            if (read is null)
            {
                return false;
            }

            document = read;

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
