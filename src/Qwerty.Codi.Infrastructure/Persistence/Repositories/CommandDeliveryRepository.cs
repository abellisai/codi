using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;
using RepoDb;
using RepoDb.Enumerations;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

/// <summary>
/// Consegne dei comandi (§7, §12.2). Non deriva dal repository comune perché la chiave è composta —
/// comando e partecipante — e il CRUD generato è costruito su una chiave sola: le consegne di un
/// comando si inseriscono in blocco e si leggono con un predicato sulle colonne mappate. Ogni
/// scrittura resta nella transazione del chiamante.
/// </summary>
internal sealed class CommandDeliveryRepository(DbContext context) : ICommandDeliveryRepository
{
    public async Task InsertAllAsync(
        IReadOnlyList<CommandDeliveryEntity> deliveries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deliveries);
        cancellationToken.ThrowIfCancellationRequested();

        if (deliveries.Count == 0)
        {
            // Un comando senza destinatari è legittimo — non c'è nessuno a cui consegnarlo — e non
            // deve produrre una chiamata al database.
            return;
        }

        await context.Connection
            .InsertAllAsync(deliveries, transaction: context.Transaction!, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CommandDeliveryEntity>> QueryByCommandAsync(
        Guid commandId,
        CancellationToken cancellationToken = default)
    {
        if (commandId == Guid.Empty)
        {
            throw new ArgumentException("L'identificativo del comando è obbligatorio.", nameof(commandId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Predicato sull'entità mappata: la traduzione della colonna la fa il mapping, non questa riga.
        var command = commandId.ToString("D");
        var rows = await context.Connection
            .QueryAsync<CommandDeliveryEntity>(
                row => row.CommandId == command,
                transaction: context.Transaction!,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.OrderBy(row => row.ParticipantId, StringComparer.Ordinal)];
    }

    public async Task<bool> MarkSentAsync(
        Guid commandId,
        Guid participantId,
        long sentAtUtcMs,
        long currentSendAttempts,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Il contatore dei tentativi si porta con sé dal valore letto: la transizione è condizionata
        // allo stato ancora in attesa, così un secondo dispatcher non invia due volte.
        var row = new CommandDeliveryEntity
        {
            CommandId = commandId.ToString("D"),
            ParticipantId = participantId.ToString("D"),
            Status = CommandDeliveryStates.Sent,
            SentAtUtcMs = sentAtUtcMs,
            SendAttempts = checked(currentSendAttempts + 1),
        };

        return await UpdateFieldsAsync(
                row,
                Field.Parse<CommandDeliveryEntity>(entity => new
                {
                    entity.Status,
                    entity.SentAtUtcMs,
                    entity.SendAttempts,
                }),
                new QueryGroup(new QueryField[]
                {
                    new(PropertyMappedNameCache.Get<CommandDeliveryEntity>(nameof(CommandDeliveryEntity.CommandId)), Operation.Equal, row.CommandId),
                    new(PropertyMappedNameCache.Get<CommandDeliveryEntity>(nameof(CommandDeliveryEntity.ParticipantId)), Operation.Equal, row.ParticipantId),
                    new(PropertyMappedNameCache.Get<CommandDeliveryEntity>(nameof(CommandDeliveryEntity.Status)), Operation.In, new[] { CommandDeliveryStates.Pending, CommandDeliveryStates.Sent }),
                    new(PropertyMappedNameCache.Get<CommandDeliveryEntity>(nameof(CommandDeliveryEntity.SendAttempts)), Operation.Equal, currentSendAttempts),
                }),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> MarkSupersededAsync(
        Guid commandId,
        Guid participantId,
        string currentStatus,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentStatus);
        cancellationToken.ThrowIfCancellationRequested();

        var row = new CommandDeliveryEntity
        {
            CommandId = commandId.ToString("D"),
            ParticipantId = participantId.ToString("D"),
            Status = CommandDeliveryStates.Superseded,
        };

        // La transizione è condizionata allo stato letto: una consegna già applicata non torna indietro.
        return await UpdateFieldsAsync(
                row,
                Field.Parse<CommandDeliveryEntity>(entity => new { entity.Status }),
                Condition(row.CommandId, row.ParticipantId, currentStatus),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Condizione per coppia e stato: la chiave è composta, quindi la riga si individua con entrambe
    /// le colonne, e lo stato atteso rende la scrittura una conferma di ciò che era stato letto.
    /// </summary>
    private static QueryGroup Condition(string commandId, string participantId, string status) =>
        new(
        [
            new QueryField(
                PropertyMappedNameCache.Get<CommandDeliveryEntity>(nameof(CommandDeliveryEntity.CommandId)),
                Operation.Equal,
                commandId),
            new QueryField(
                PropertyMappedNameCache.Get<CommandDeliveryEntity>(nameof(CommandDeliveryEntity.ParticipantId)),
                Operation.Equal,
                participantId),
            new QueryField(
                PropertyMappedNameCache.Get<CommandDeliveryEntity>(nameof(CommandDeliveryEntity.Status)),
                Operation.Equal,
                status),
        ]);

    /// <summary>
    /// Condizione per coppia e stato **in un insieme**: una conferma è valida sia per una consegna
    /// appena inviata sia per una la cui conferma è arrivata tardi, e in entrambi i casi la scrittura
    /// deve trovare la riga nello stato letto e non in un altro.
    /// </summary>
    private static QueryGroup Condition(
        string commandId,
        Guid participantId,
        IReadOnlyList<string> statuses) =>
        new(
        [
            new QueryField(
                PropertyMappedNameCache.Get<CommandDeliveryEntity>(nameof(CommandDeliveryEntity.CommandId)),
                Operation.Equal,
                commandId),
            new QueryField(
                PropertyMappedNameCache.Get<CommandDeliveryEntity>(nameof(CommandDeliveryEntity.ParticipantId)),
                Operation.Equal,
                participantId.ToString("D")),
            new QueryField(
                PropertyMappedNameCache.Get<CommandDeliveryEntity>(nameof(CommandDeliveryEntity.Status)),
                Operation.In,
                statuses),
        ]);

    /// <summary>Stati da cui una conferma del client è ammessa: inviata, oppure inviata e in ritardo (§12.2).</summary>
    private static readonly string[] AwaitingAcknowledgement =
        [CommandDeliveryStates.Pending, CommandDeliveryStates.Sent, CommandDeliveryStates.Unconfirmed];

    public async Task<bool> MarkAppliedAsync(
        Guid commandId,
        Guid participantId,
        long acknowledgedAtUtcMs,
        bool requiresRecovery,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var row = new CommandDeliveryEntity
        {
            CommandId = commandId.ToString("D"),
            ParticipantId = participantId.ToString("D"),
            Status = CommandDeliveryStates.Applied,
            AckAtUtcMs = acknowledgedAtUtcMs,
            AppliedAtUtcMs = acknowledgedAtUtcMs,
            RequiresRecoveryFlag = requiresRecovery ? 1 : 0,
        };

        return await UpdateFieldsAsync(
                row,
                Field.Parse<CommandDeliveryEntity>(entity => new
                {
                    entity.Status,
                    entity.AckAtUtcMs,
                    entity.AppliedAtUtcMs,
                    entity.RequiresRecoveryFlag,
                }),
                Condition(row.CommandId, participantId, AwaitingAcknowledgement),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> MarkFailedAsync(
        Guid commandId,
        Guid participantId,
        long acknowledgedAtUtcMs,
        string? errorCode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var row = new CommandDeliveryEntity
        {
            CommandId = commandId.ToString("D"),
            ParticipantId = participantId.ToString("D"),
            Status = CommandDeliveryStates.Failed,
            AckAtUtcMs = acknowledgedAtUtcMs,
            ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? null : errorCode,
        };

        return await UpdateFieldsAsync(
                row,
                Field.Parse<CommandDeliveryEntity>(entity => new { entity.Status, entity.AckAtUtcMs, entity.ErrorCode }),
                Condition(row.CommandId, participantId, AwaitingAcknowledgement),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CommandDeliveryEntity>> QueryUnconfirmedAsync(
        long sentAtOrBeforeUtcMs,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Il limite deve essere positivo.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var rows = await context.Connection
            .QueryAsync<CommandDeliveryEntity>(
                row => row.Status == CommandDeliveryStates.Sent && row.SentAtUtcMs <= sentAtOrBeforeUtcMs,
                transaction: context.Transaction!,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.OrderBy(row => row.SentAtUtcMs ?? 0).ThenBy(row => row.CommandId, StringComparer.Ordinal).Take(limit)];
    }

    public async Task<bool> MarkUnconfirmedAsync(
        Guid commandId,
        Guid participantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var row = new CommandDeliveryEntity
        {
            CommandId = commandId.ToString("D"),
            ParticipantId = participantId.ToString("D"),
            Status = CommandDeliveryStates.Unconfirmed,
        };

        // Solo una consegna inviata può diventare non confermata: una ancora in attesa non è in
        // ritardo, e una già applicata non torna indietro.
        return await UpdateFieldsAsync(
                row,
                Field.Parse<CommandDeliveryEntity>(entity => new { entity.Status }),
                Condition(row.CommandId, row.ParticipantId, CommandDeliveryStates.Sent),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> UpdateFieldsAsync(
        CommandDeliveryEntity row,
        IEnumerable<Field> fields,
        QueryGroup condition,
        CancellationToken cancellationToken)
    {
        var written = await context.Connection
            .UpdateAsync(
                row,
                where: condition,
                fields: fields,
                transaction: context.Transaction!,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return written == 1;
    }
}
