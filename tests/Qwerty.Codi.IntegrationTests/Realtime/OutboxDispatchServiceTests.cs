using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Qwerty.Codi.Api.BackgroundJobs;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Sessions;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Realtime;

/// <summary>
/// Ciclo del dispatcher outbox in background (§12.1–§12.2): il servizio è la sola parte che
/// trasforma una coda persistita in consegne senza che nessuno lo chiami. I test verificano che
/// parta, che il segnale lo svegli consegnando il messaggio già in coda e che un giro fallito non
/// lo fermi — la coda è su disco, quindi il giro successivo la riprende.
/// </summary>
public sealed class OutboxDispatchServiceTests : IAsyncLifetime
{
    private OutboxScenario _scenario = null!;

    public async Task InitializeAsync() => _scenario = await OutboxScenario.CreateAsync();

    public async Task DisposeAsync() => await _scenario.DisposeAsync();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Ciclo_ConMessaggioInCoda_ConsegnaESegnaIlMessaggioConcluso()
    {
        // Arrange: un comando di regia ha messo in coda il messaggio con una consegna in attesa.
        var seed = await _scenario.StartedSessionAsync();
        var luca = await _scenario.JoinAsync(seed, "Luca");
        var accepted = await _scenario.ShowWaitingAsync(seed, expectedSessionRevision: 2L);
        var notifier = new RecordingCommandDeliveryNotifier();
        var signal = new ControllableOutboxSignal();
        var dispatcher = new OutboxDispatcher(
            _scenario.Host.Services.GetRequiredService<IUnitOfWorkContext>(),
            notifier,
            _scenario.Host.Services.GetRequiredService<TimeProvider>());
        var service = new OutboxDispatchService(dispatcher, signal, NullLogger);

        // Act: il ciclo parte da solo e non è richiamato dal test.
        await service.StartAsync(CancellationToken.None);

        try
        {
            // Assert: il servizio ha inviato il messaggio al destinatario e ha concluso la coda.
            var sent = await WaitForDeliveryAsync(notifier);
            Assert.Equal(luca.Joined.ParticipantId, sent.ParticipantId);
            Assert.Equal(accepted.CommandId, sent.Message.CommandId);

            // L'invio precede la scrittura dell'esito: la consegna si legge **dopo** che il ciclo ha
            // concluso la sua transazione, non nella finestra fra le due.
            await WaitForStatusAsync(accepted.CommandId, CommandDeliveryStates.Sent);
            Assert.Equal(
                1L,
                await _scenario.ScalarAsync<long>(
                    "SELECT COUNT(*) FROM outbox_message WHERE processed_at_utc_ms IS NOT NULL;"));

            // Il ciclo prosegue mettendosi in attesa del segnale: si aspetta che lo faccia, perché
            // osservare l'effetto non dice ancora che il giro sia concluso.
            await WaitForAsync(() => signal.WaitCalls >= 1, "Il ciclo deve attendere il segnale, non girare a vuoto.");
        }
        finally
        {
            await StopAsync(service);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Ciclo_ConGiroFallito_NonSiFerma()
    {
        // Arrange: la lettura della coda fallisce — è il guasto che non deve spegnere il servizio.
        var signal = new ControllableOutboxSignal();
        var logger = new RecordingLogger();
        var dispatcher = new OutboxDispatcher(
            new ThrowingUnitOfWorkContext(),
            new RecordingCommandDeliveryNotifier(),
            _scenario.Host.Services.GetRequiredService<TimeProvider>());
        var service = new OutboxDispatchService(dispatcher, signal, logger);

        // Act
        await service.StartAsync(CancellationToken.None);

        try
        {
            // Assert: il guasto è registrato e il ciclo ha chiesto di attendere di nuovo, cioè è vivo.
            await WaitForAsync(() => logger.Errors.Count >= 1, "Il guasto del giro deve essere registrato.");
            await WaitForAsync(() => signal.WaitCalls >= 2, "Dopo un giro fallito il ciclo deve proseguire.");
        }
        finally
        {
            await StopAsync(service);
        }
    }

    private static ILogger<OutboxDispatchService> NullLogger { get; } =
        LoggerFactory.Create(_ => { }).CreateLogger<OutboxDispatchService>();

    private static async Task<(Guid ParticipantId, CommandIssuedMessageDto Message)> WaitForDeliveryAsync(
        RecordingCommandDeliveryNotifier notifier)
    {
        await WaitForAsync(() => notifier.Sent.Count >= 1, "Il ciclo deve consegnare il messaggio in coda.");

        return notifier.Sent[0];
    }

    /// <summary>
    /// Attende che una consegna raggiunga lo stato atteso, rileggendolo dal database: il ciclo invia
    /// prima di scrivere l'esito, quindi la riga può non essere ancora aggiornata quando l'invio è
    /// osservabile.
    /// </summary>
    private async Task WaitForStatusAsync(Guid commandId, string expected)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var status = await _scenario.ScalarAsync<string>(
                $"SELECT status FROM command_delivery WHERE command_id = '{commandId:D}';");

            if (string.Equals(status, expected, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.Fail($"La consegna non ha raggiunto lo stato {expected}.");
    }

    /// <summary>
    /// Attesa di una condizione prodotta da un altro ciclo: il servizio non offre un evento da
    /// attendere, quindi si osserva l'effetto e si fallisce con un messaggio se non arriva.
    /// </summary>
    private static async Task WaitForAsync(Func<bool> condition, string failure)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.Fail(failure);
    }

    private static async Task StopAsync(OutboxDispatchService service)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await service.StopAsync(timeout.Token);
    }

    /// <summary>Segnale pilotato dal test: il primo giro prosegue, i successivi attendono l'annullamento.</summary>
    private sealed class ControllableOutboxSignal : IOutboxSignal
    {
        private int _waitCalls;

        public int WaitCalls => Volatile.Read(ref _waitCalls);

        public void Notify()
        {
            // Il segnale non trattiene il messaggio: la coda è su disco, qui interessa solo che il
            // ciclo si svegli. Un secondo segnale senza attesa in corso non deve fallire.
        }

        public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _waitCalls) == 1)
            {
                return true;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            return false;
        }
    }

    /// <summary>Contesto il cui uso fallisce: rappresenta un guasto di lettura della coda.</summary>
    private sealed class ThrowingUnitOfWorkContext : IUnitOfWorkContext
    {
        public Task<IUnitOfWork> CreateAsync(
            bool useTransaction = false,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("lettura della coda non disponibile");
    }

    private sealed class RecordingLogger : ILogger<OutboxDispatchService>
    {
        private readonly List<Exception> _errors = [];

        public IReadOnlyList<Exception> Errors => _errors;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error && exception is not null)
            {
                _errors.Add(exception);
            }
        }
    }
}
