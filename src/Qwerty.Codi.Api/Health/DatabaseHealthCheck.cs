using Microsoft.Extensions.Diagnostics.HealthChecks;
using Qwerty.Codi.Infrastructure.Persistence;

namespace Qwerty.Codi.Api.Health;

/// <summary>
/// Readiness di §18.4: bootstrap, migrazioni e accesso allo storage disponibili. La risposta non
/// espone dettagli: le divergenze di schema restano nell'eccezione, che finisce nei log.
/// </summary>
internal sealed class DatabaseHealthCheck(DatabaseReadinessProbe readinessProbe) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var readiness = await readinessProbe.CheckAsync(cancellationToken).ConfigureAwait(false);

            if (!readiness.UsesWriteAheadLogging)
            {
                return HealthCheckResult.Unhealthy("Il database non usa il journal WAL.");
            }

            if (!readiness.Schema.IsUpToDate)
            {
                return HealthCheckResult.Unhealthy(
                    "Lo schema del database non corrisponde alle migrazioni distribuite.");
            }

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            // L'eccezione resta nei log del middleware di salute; il corpo della risposta no.
            return HealthCheckResult.Unhealthy("Accesso al database non riuscito.", exception);
        }
    }
}
