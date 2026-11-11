using System.Data.Common;
using Qwerty.Codi.Infrastructure.Persistence;

namespace Qwerty.Codi.Infrastructure.Migrations;

/// <summary>
/// Applica e ispeziona le migrazioni SQL numerate (§8.3). Ogni migrazione è atomica: lo script e
/// la registrazione della versione stanno nella stessa transazione, così un'interruzione non
/// lascia uno schema applicato senza la sua versione, né viceversa.
/// </summary>
/// <remarks>
/// La migrazione usa ADO.NET diretto e non i repository: lo schema è un prerequisito di ogni
/// repository e la sua applicazione non deve dipendere dal mapping degli oggetti di dominio.
/// </remarks>
public sealed class MigrationRunner(SqliteConnectionFactory connectionFactory, TimeProvider timeProvider)
{
    private const string SchemaMigrationTableSql = """
        CREATE TABLE IF NOT EXISTS schema_migration (
            version             INTEGER NOT NULL PRIMARY KEY,
            name                TEXT    NOT NULL,
            checksum            TEXT    NOT NULL,
            applied_at_utc_ms   INTEGER NOT NULL,
            application_version TEXT    NOT NULL
        );
        """;

    /// <summary>
    /// Applica le migrazioni mancanti e restituisce lo stato finale. Se il database presenta
    /// divergenze non scrive nulla e solleva <see cref="SchemaMigrationException"/>: uno schema
    /// che non corrisponde agli script non si aggiusta applicando le versioni successive.
    /// </summary>
    public async Task<MigrationStatus> ApplyAsync(
        MigrationCatalog catalog,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        await using (var connection = await connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            await EnsureSchemaMigrationTableAsync(connection, cancellationToken).ConfigureAwait(false);

            var applied = await ReadAppliedChecksumsAsync(connection, cancellationToken).ConfigureAwait(false);
            var divergences = FindDivergences(catalog, applied);
            if (divergences.Count > 0)
            {
                throw new SchemaMigrationException(
                    $"Schema del database non allineato alle migrazioni: {string.Join(" ", divergences)}");
            }

            foreach (var script in catalog.Scripts.Where(script => !applied.ContainsKey(script.Version)))
            {
                // Punto di cancellazione cooperativo fra due migrazioni: il provider SQLite non
                // garantisce I/O realmente annullabile, quindi l'annullamento si verifica dove è
                // deterministico, cioè fra due transazioni già atomiche (§8.3).
                cancellationToken.ThrowIfCancellationRequested();

                await ApplyScriptAsync(connection, script, cancellationToken).ConfigureAwait(false);
            }
        }

        // Lo stato finale si legge dopo aver chiuso la connessione di scrittura: è la stessa
        // lettura che farà la prontezza, quindi la migrazione non può dichiararsi riuscita
        // sulla base di ciò che ha appena scritto in memoria.
        return await InspectAsync(catalog, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Legge lo stato senza modificare nulla. È il controllo di prontezza: un database non
    /// ancora migrato è «in attesa», uno con impronte o versioni estranee è «divergente».
    /// </summary>
    public async Task<MigrationStatus> InspectAsync(
        MigrationCatalog catalog,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        await using var connection = await connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await SchemaMigrationTableExistsAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            // Nessuna tabella di registro: installazione mai migrata, non schema sconosciuto.
            return new MigrationStatus(
                CurrentVersion: 0,
                ExpectedVersion: catalog.LatestVersion,
                PendingVersions: [.. catalog.Scripts.Select(script => script.Version)],
                Divergences: []);
        }

        var applied = await ReadAppliedChecksumsAsync(connection, cancellationToken).ConfigureAwait(false);
        var currentVersion = applied.Count == 0 ? 0 : applied.Keys.Max();

        return new MigrationStatus(
            CurrentVersion: currentVersion,
            ExpectedVersion: catalog.LatestVersion,
            PendingVersions: [.. catalog.Scripts
                .Where(script => !applied.ContainsKey(script.Version))
                .Select(script => script.Version)],
            Divergences: FindDivergences(catalog, applied));
    }

    private static async Task EnsureSchemaMigrationTableAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = SchemaMigrationTableSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> SchemaMigrationTableExistsAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'schema_migration';";

        var count = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(count, System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    private static async Task<Dictionary<int, string>> ReadAppliedChecksumsAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        var applied = new Dictionary<int, string>();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT version, checksum FROM schema_migration ORDER BY version;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            applied[reader.GetInt32(0)] = reader.GetString(1);
        }

        return applied;
    }

    /// <summary>
    /// Confronta quanto registrato nel database con gli script distribuiti. Il criterio di
    /// integrità è l'impronta (§8.3); il nome è descrittivo e non partecipa al confronto.
    /// </summary>
    private static List<string> FindDivergences(
        MigrationCatalog catalog,
        IReadOnlyDictionary<int, string> applied)
    {
        var divergences = new List<string>();
        var currentVersion = applied.Count == 0 ? 0 : applied.Keys.Max();

        for (var version = 1; version <= currentVersion; version++)
        {
            if (!applied.ContainsKey(version))
            {
                divergences.Add(
                    $"manca la versione {version} pur essendo applicate versioni successive;");
            }
        }

        foreach (var version in applied.Keys.OrderBy(version => version))
        {
            var script = catalog.Find(version);
            if (script is null)
            {
                divergences.Add($"la versione {version} è sconosciuta a questa versione dell'applicazione;");
                continue;
            }

            if (!string.Equals(script.Checksum, applied[version], StringComparison.Ordinal))
            {
                divergences.Add(
                    $"la versione {version} non corrisponde allo script distribuito (attesa impronta {script.Checksum}, trovata {applied[version]});");
            }
        }

        return divergences;
    }

    private async Task ApplyScriptAsync(
        DbConnection connection,
        MigrationScript script,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await using (var scriptCommand = connection.CreateCommand())
            {
                scriptCommand.Transaction = transaction;
                scriptCommand.CommandText = script.Sql;
                await scriptCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var recordCommand = connection.CreateCommand())
            {
                recordCommand.Transaction = transaction;
                recordCommand.CommandText = """
                    INSERT INTO schema_migration (version, name, checksum, applied_at_utc_ms, application_version)
                    VALUES ($version, $name, $checksum, $appliedAtUtcMs, $applicationVersion);
                    """;

                AddParameter(recordCommand, "$version", script.Version);
                AddParameter(recordCommand, "$name", script.Name);
                AddParameter(recordCommand, "$checksum", script.Checksum);
                AddParameter(
                    recordCommand,
                    "$appliedAtUtcMs",
                    timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
                AddParameter(recordCommand, "$applicationVersion", GetApplicationVersion());

                await recordCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // L'annullamento esplicito non è obbligatorio — il dispose senza commit annulla — ma
            // rende immediato il rilascio del lock di scrittura prima di risalire l'errore.
            await RollbackQuietlyAsync(transaction).ConfigureAwait(false);

            throw new SchemaMigrationException(
                $"La migrazione {script.Version} ({script.Name}) non è stata applicata.",
                exception);
        }
    }

    private static async Task RollbackQuietlyAsync(DbTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Intenzionalmente ignorata: l'eccezione da propagare è quella che ha causato
            // l'annullamento, non un secondo errore durante l'annullamento.
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string GetApplicationVersion() =>
        typeof(MigrationRunner).Assembly.GetName().Version?.ToString() ?? "unknown";
}
