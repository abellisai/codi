using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Sessions;

namespace Qwerty.Codi.Api.BackgroundJobs;

/// <summary>
/// Ciclo del dispatcher outbox (§12.1–§12.2): attende il segnale del comando appena registrato, con
/// una scadenza breve come rete di sicurezza — una coda rimasta piena dopo un errore non deve restare
/// ferma in attesa di un segnale che non arriverà — e conclude i messaggi disponibili.
///
/// Il ciclo non muore per un giro fallito: la coda è persistita, quindi il giro successivo la
/// riprende. Il caso d'uso non conosce questo servizio: segnala e basta.
/// </summary>
internal sealed class OutboxDispatchService(
    OutboxDispatcher dispatcher,
    IOutboxSignal signal,
    ILogger<OutboxDispatchService> logger) : BackgroundService
{
    /// <summary>Attesa massima senza segnale: è la rete di sicurezza del ciclo, non una frequenza.</summary>
    private static readonly TimeSpan IdleWait = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await dispatcher.DispatchPendingAsync(stoppingToken).ConfigureAwait(false);

                if (result.MessagesProcessed > 0)
                {
                    logger.LogInformation(
                        "Outbox: {Messages} messaggi conclusi, {Sent} consegne inviate, {Superseded} superate.",
                        result.MessagesProcessed,
                        result.DeliveriesSent,
                        result.DeliveriesSuperseded);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox: il giro di distribuzione è fallito.");
            }

            try
            {
                await signal.WaitAsync(IdleWait, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
