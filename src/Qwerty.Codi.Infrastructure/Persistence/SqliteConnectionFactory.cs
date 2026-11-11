using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Qwerty.Codi.Infrastructure.Persistence;

/// <summary>
/// Apre una connessione SQLite dedicata per unità di lavoro e applica le impostazioni di
/// connessione di §8.1. La connessione è di chi la apre e non va mai condivisa tra richieste,
/// hub o thread (§8.1, §20.6).
/// </summary>
public sealed class SqliteConnectionFactory
{
    private const string WriteAheadLoggingPragma = "PRAGMA journal_mode = WAL;";

    private readonly Func<DbConnection> _createConnection;

    private readonly string _connectionPragmas;

    /// <summary>
    /// Crea la factory sul provider SQLite predefinito, <c>Microsoft.Data.Sqlite</c>, sulla
    /// posizione e sull'attesa configurate in §18.2.
    /// </summary>
    public SqliteConnectionFactory(SqliteDatabase database)
        : this(database, CreateSqliteConnection(database))
    {
    }

    /// <summary>
    /// Crea la factory su un provider fornito dal chiamante. È il punto di sostituzione del
    /// provider e consente di verificare lo smaltimento nei percorsi di errore senza un
    /// database reale (§8.1, §20.6).
    /// </summary>
    public SqliteConnectionFactory(SqliteDatabase database, Func<DbConnection> createConnection)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(createConnection);

        _createConnection = createConnection;
        _connectionPragmas =
            $"PRAGMA foreign_keys = ON; PRAGMA synchronous = FULL; PRAGMA busy_timeout = {database.BusyTimeoutMs};";
    }

    /// <summary>
    /// Apre una connessione con chiavi esterne, sincronizzazione e timeout assicurati.
    /// Se l'apertura o l'applicazione dei PRAGMA fallisce, la connessione non viene
    /// trasferita al chiamante e viene smaltita qui (§20.6).
    /// </summary>
    public async Task<DbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = _createConnection();

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = _connectionPragmas;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            return connection;
        }
        catch
        {
            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Intenzionalmente ignorata: l'eccezione propagata deve restare quella di
                // apertura o di applicazione dei PRAGMA, che è la causa reale del guasto.
            }

            throw;
        }
    }

    /// <summary>
    /// Attiva il journal WAL una sola volta per file di database e restituisce il modo
    /// effettivo letto dal database, che va verificato invece di presunto (§8.1). Appartiene
    /// al bootstrap controllato, non alle singole connessioni.
    /// </summary>
    public async Task<string> EnableWriteAheadLoggingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = WriteAheadLoggingPragma;

        var effectiveMode = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return effectiveMode as string ?? string.Empty;
    }

    /// <summary>
    /// Costruisce la stringa di connessione dalle impostazioni risolte: chiavi esterne attive
    /// anche nella stringa (§20.6) e pooling disattivato, perché ogni unità di lavoro usa una
    /// connessione dedicata e perché il file non deve restare tenuto da connessioni inattive
    /// durante backup e ripristino (§18.5).
    /// </summary>
    private static string BuildConnectionString(SqliteDatabase database) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = database.FilePath,
            ForeignKeys = true,
            Pooling = false,
        }.ToString();

    private static Func<DbConnection> CreateSqliteConnection(SqliteDatabase database)
    {
        var connectionString = BuildConnectionString(database);
        return () => new SqliteConnection(connectionString);
    }
}
