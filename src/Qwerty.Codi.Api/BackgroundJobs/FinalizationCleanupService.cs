using Qwerty.Codi.Application.Sessions;

namespace Qwerty.Codi.Api.BackgroundJobs;

internal sealed class FinalizationCleanupService(IServiceScopeFactory scopes, TimeProvider time,
    ILogger<FinalizationCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<FinalizationCleanup>().ExpireAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is not OperationCanceledException)
                {
                    logger.LogError(failure, "Revoca delle credenziali terminali non completata.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
