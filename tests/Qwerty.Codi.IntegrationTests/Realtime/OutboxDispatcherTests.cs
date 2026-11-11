using Qwerty.Codi.Application.Dto;
using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Lessons;
using Qwerty.Codi.Application.Organization;
using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.Domain.Sessions;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Realtime;

/// <summary>
/// Dispatcher outbox (§12.1–§12.2): legge i messaggi già decisi, risolve i destinatari dalle loro
/// consegne, invia e registra l'esito. I test coprono il percorso positivo, il comando **superato**
/// da una revisione più recente, l'assenza di destinatari, la coda vuota, il messaggio non ancora
/// disponibile, quello illeggibile e la ripetizione del giro.
///
/// Il trasporto è un doppio: l'applicazione non conosce SignalR, quindi il test verifica ciò che ha
/// deciso di inviare e a chi, non come arriva al browser.
/// </summary>
public sealed class OutboxDispatcherTests : IAsyncLifetime
{
    private OutboxScenario _scenario = null!;

    public async Task InitializeAsync() => _scenario = await OutboxScenario.CreateAsync();

    public async Task DisposeAsync() => await _scenario.DisposeAsync();

    /// <summary>Composizione reale dei casi d'uso, preparata dalla scena condivisa.</summary>
    private CodiUseCaseHost Host => _scenario.Host;

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispatch_ConMessaggioInCoda_ConsegnaAlDestinatarioESegnaLInvio()
    {
        // Arrange: un comando registrato ha messo in coda il messaggio e una consegna in attesa.
        var seed = await StartedSessionAsync();
        var luca = await JoinAsync(seed, "Luca");
        var accepted = await ShowWaitingAsync(seed, expectedSessionRevision: 2L);
        var notifier = new RecordingCommandDeliveryNotifier();

        // Act
        var result = await Dispatcher(notifier).DispatchPendingAsync(CancellationToken.None);

        // Assert: il messaggio è concluso, la consegna è inviata e il destinatario è quello giusto.
        Assert.Equal(new OutboxDispatchResult(MessagesProcessed: 1, DeliveriesSent: 1, DeliveriesSuperseded: 0), result);

        var sent = Assert.Single(notifier.Sent);
        Assert.Equal(luca.Joined.ParticipantId, sent.ParticipantId);
        Assert.Equal(accepted.CommandId, sent.Message.CommandId);
        Assert.Equal(seed.SessionId, sent.Message.SessionId);
        Assert.Equal(3, sent.Message.EffectiveRevision);
        Assert.Equal(ShowWaitingCommand.RequestName, sent.Message.CommandName);
        Assert.Equal(nameof(ParticipantView.Waiting), sent.Message.DesiredState.View);

        var delivery = await ColumnsAsync(
            "SELECT status, sent_at_utc_ms, send_attempts, applied_at_utc_ms FROM command_delivery WHERE command_id = $id;",
            accepted.CommandId);
        Assert.Equal(CommandDeliveryStates.Sent, delivery[0]);
        Assert.Equal(
            CodiUseCaseHost.Now.ToUnixTimeMilliseconds(),
            Convert.ToInt64(delivery[1], CultureInfo.InvariantCulture));
        Assert.Equal(1L, delivery[2]);
        Assert.IsType<DBNull>(delivery[3]);

        // Il tentativo di distribuzione è concluso: non è la conferma del client (ACK).
        Assert.Equal(
            1L,
            await ScalarAsync<long>(
                $"SELECT COUNT(*) FROM outbox_message WHERE processed_at_utc_ms IS NOT NULL AND attempt_count = 1;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispatch_ConRevisioneSuperata_MarcaLaConsegnaSuperataENonInvia()
    {
        // Arrange: dopo il comando la sessione è avanzata, quindi la sua revisione è superata e il
        // comando non si applica al nuovo stato (§12.2, §12.3).
        var seed = await StartedSessionAsync();
        await JoinAsync(seed, "Luca");
        var accepted = await ShowWaitingAsync(seed, expectedSessionRevision: 2L);
        await Host.CommandAsAsync<SessionDto>(
            PauseCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = 3L },
            RegiaContext());

        var notifier = new RecordingCommandDeliveryNotifier();

        // Act
        var result = await Dispatcher(notifier).DispatchPendingAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            new OutboxDispatchResult(MessagesProcessed: 2, DeliveriesSent: 1, DeliveriesSuperseded: 1),
            result);
        Assert.Equal("Pause", Assert.Single(notifier.Sent).Message.CommandName);
        Assert.Equal(
            CommandDeliveryStates.Superseded,
            await ScalarAsync<string>($"SELECT status FROM command_delivery WHERE command_id = '{accepted.CommandId:D}';"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispatch_SenzaDestinatari_ConcludeIlMessaggioSenzaInviare()
    {
        // Arrange: nessuno è entrato, quindi il comando non ha consegne.
        var seed = await StartedSessionAsync();
        await ShowWaitingAsync(seed, expectedSessionRevision: 2L);
        var notifier = new RecordingCommandDeliveryNotifier();

        // Act
        var result = await Dispatcher(notifier).DispatchPendingAsync(CancellationToken.None);

        // Assert
        Assert.Equal(new OutboxDispatchResult(1, 0, 0), result);
        Assert.Empty(notifier.Sent);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispatch_SenzaMessaggiDisponibili_NonFaNulla()
    {
        // Arrange: coda vuota.
        var notifier = new RecordingCommandDeliveryNotifier();

        // Act
        var result = await Dispatcher(notifier).DispatchPendingAsync(CancellationToken.None);

        // Assert
        Assert.Equal(new OutboxDispatchResult(0, 0, 0), result);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispatch_ConMessaggioNonAncoraDisponibile_NonLoProcessa()
    {
        // Arrange: la disponibilità è nel futuro: è un'attesa decisa, non un messaggio da inviare.
        var seed = await StartedSessionAsync();
        await JoinAsync(seed, "Luca");
        await ShowWaitingAsync(seed, expectedSessionRevision: 2L);
        var notifier = new RecordingCommandDeliveryNotifier();

        await ExecuteSqlAsync(
            $"UPDATE outbox_message SET available_at_utc_ms = {CodiUseCaseHost.Now.AddMinutes(1).ToUnixTimeMilliseconds()};");

        // Act
        var result = await Dispatcher(notifier).DispatchPendingAsync(CancellationToken.None);

        // Assert
        Assert.Equal(new OutboxDispatchResult(0, 0, 0), result);
        Assert.Empty(notifier.Sent);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT COUNT(*) FROM outbox_message WHERE processed_at_utc_ms IS NOT NULL;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispatch_ConMessaggioDiTipoSconosciuto_LoScartaConIlMotivo()
    {
        // Arrange: un tipo di messaggio che il dispatcher non conosce è un difetto interno, non un
        // messaggio da ritentare per sempre. La consegna resta in attesa e il motivo è registrato.
        var seed = await StartedSessionAsync();
        await JoinAsync(seed, "Luca");
        await ShowWaitingAsync(seed, expectedSessionRevision: 2L);
        var notifier = new RecordingCommandDeliveryNotifier();

        await ExecuteSqlAsync("UPDATE outbox_message SET message_type = 'EventoSconosciuto';");

        // Act
        var result = await Dispatcher(notifier).DispatchPendingAsync(CancellationToken.None);

        // Assert
        Assert.Equal(new OutboxDispatchResult(1, 0, 0), result);
        Assert.Empty(notifier.Sent);
        Assert.Equal(
            CodiErrorCodes.InvalidPayload,
            await ScalarAsync<string>(
                "SELECT last_error_code FROM outbox_message WHERE processed_at_utc_ms IS NOT NULL;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispatch_ConTrasportoCheFallisce_LasciaIlMessaggioInCoda()
    {
        // Arrange: il trasporto rifiuta la consegna; il messaggio resta da processare e verrà
        // ritentato al giro successivo.
        var seed = await StartedSessionAsync();
        await JoinAsync(seed, "Luca");
        var accepted = await ShowWaitingAsync(seed, expectedSessionRevision: 2L);
        var notifier = new RecordingCommandDeliveryNotifier { Failure = new InvalidOperationException("trasporto non disponibile") };

        // Act
        var result = await Dispatcher(notifier).DispatchPendingAsync(CancellationToken.None);

        // Assert
        Assert.Equal(new OutboxDispatchResult(0, 0, 0), result);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT COUNT(*) FROM outbox_message WHERE processed_at_utc_ms IS NOT NULL;"));
        Assert.Equal(
            CommandDeliveryStates.Pending,
            await ScalarAsync<string>($"SELECT status FROM command_delivery WHERE command_id = '{accepted.CommandId:D}';"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispatch_RipetutoConLaStessaCoda_NonInviaDueVolte()
    {
        // Arrange: un secondo giro non deve ritrovare ciò che il primo ha già concluso.
        var seed = await StartedSessionAsync();
        await JoinAsync(seed, "Luca");
        await ShowWaitingAsync(seed, expectedSessionRevision: 2L);
        var notifier = new RecordingCommandDeliveryNotifier();
        var dispatcher = Dispatcher(notifier);

        // Act
        var first = await dispatcher.DispatchPendingAsync(CancellationToken.None);
        var second = await dispatcher.DispatchPendingAsync(CancellationToken.None);

        // Assert
        Assert.Equal(new OutboxDispatchResult(1, 1, 0), first);
        Assert.Equal(new OutboxDispatchResult(0, 0, 0), second);
        Assert.Single(notifier.Sent);
    }

    private OutboxDispatcher Dispatcher(ICommandDeliveryNotifier notifier) =>
        new(
            Host.Services.GetRequiredService<IUnitOfWorkContext>(),
            notifier,
            Host.Services.GetRequiredService<TimeProvider>());

    private Task<CommandAcceptedDto> ShowWaitingAsync(Seed seed, long expectedSessionRevision) =>
        _scenario.ShowWaitingAsync(seed, expectedSessionRevision);

    private CodiRequestContext RegiaContext() => _scenario.RegiaContext();

    private Task<Seed> StartedSessionAsync() => _scenario.StartedSessionAsync();

    private Task<JoinSessionOutcome> JoinAsync(
        Seed seed,
        string displayName,
        string idempotencyKey = "chiave-di-prova") =>
        _scenario.JoinAsync(seed, displayName, idempotencyKey);

    /// <summary>Valori della prima riga, nell'ordine delle colonne selezionate.</summary>
    private async Task<object[]> ColumnsAsync(string sql, Guid id)
    {
        await using var connection = await Host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "$id", id.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "La riga attesa non esiste.");

        var values = new object[reader.FieldCount];
        reader.GetValues(values);

        return values;
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await Host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();

        return (T)Convert.ChangeType(value!, typeof(T), CultureInfo.InvariantCulture);
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = await Host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
