using Qwerty.Codi.Infrastructure.Migrations;

namespace Qwerty.Codi.Infrastructure.Persistence;

/// <summary>
/// Prepara il database prima che il server accetti richieste (§16.1): crea la directory dati,
/// inizializza il provider RepoDB, attiva il journal WAL verificandone il modo effettivo (§8.1)
/// e applica le migrazioni numerate (§8.3). Non esiste un DDL di installazione separato: una
/// installazione pulita è l'applicazione in ordine di tutte le migrazioni, così il percorso
/// nuovo e quello di aggiornamento non possono divergere.
/// </summary>
public sealed class DatabaseBootstrap(
    SqliteConnectionFactory connectionFactory,
    SqliteDatabase database,
    MigrationCatalog catalog,
    MigrationRunner migrationRunner)
{
    /// <summary>
    /// Esegue la preparazione e restituisce journal effettivo e versione di schema applicata.
    /// Un guasto di migrazione o un journal diverso da WAL interrompe l'avvio: il server non
    /// deve accettare richieste su un database che non corrisponde al codice.
    /// </summary>
    public async Task<DatabasePreparation> PrepareAsync(CancellationToken cancellationToken)
    {
        EnsureDataDirectory();

        // Il provider RepoDB va inizializzato prima del primo accesso ai repository (§8.1).
        RepoDbSqliteBootstrap.EnsureInitialized();

        var journalMode = await connectionFactory
            .EnableWriteAheadLoggingAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Il database non usa il journal WAL: modo effettivo '{journalMode}'.");
        }

        var status = await migrationRunner
            .ApplyAsync(catalog, cancellationToken)
            .ConfigureAwait(false);

        return new DatabasePreparation(journalMode, status.CurrentVersion);
    }

    private void EnsureDataDirectory()
    {
        var directory = Path.GetDirectoryName(database.FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
