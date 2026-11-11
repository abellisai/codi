using Qwerty.Codi.Infrastructure.Migrations;

namespace Qwerty.Codi.Infrastructure.Persistence;

/// <summary>
/// Verifica la prontezza del database senza modificarlo (§18.4): legge il modo di journal
/// effettivo e confronta lo schema con gli script distribuiti. La logica sta qui, fuori dal
/// controllo di salute, così è verificabile senza avviare un server.
/// </summary>
public sealed class DatabaseReadinessProbe(
    SqliteConnectionFactory connectionFactory,
    MigrationCatalog catalog,
    MigrationRunner migrationRunner)
{
    public async Task<DatabaseReadiness> CheckAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";

        var journalMode = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
            as string ?? string.Empty;

        var schema = await migrationRunner.InspectAsync(catalog, cancellationToken).ConfigureAwait(false);

        return new DatabaseReadiness(journalMode, schema);
    }
}
