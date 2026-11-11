using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Qwerty.Codi.Infrastructure.Persistence;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Persistence;

/// <summary>
/// Verifica le impostazioni di connessione di §8.1, lo smaltimento della connessione nei
/// percorsi di errore e di cancellazione (§20.6) e il rollback al dispose senza commit
/// (§20.6). I test che aprono un database usano un file temporaneo dedicato, eliminato alla
/// fine (§20.3); quelli con connessione fittizia non toccano il disco.
/// </summary>
public sealed class SqliteConnectionFactoryTests : IAsyncLifetime
{
    private const string CreateTableSql = """
        CREATE TABLE prototype_row (
            id                TEXT    NOT NULL PRIMARY KEY,
            created_at_utc_ms INTEGER NOT NULL,
            note              TEXT    NULL
        );
        """;

    private const int BusyTimeoutMs = 5000;

    private static readonly SqliteDatabase UnusedDatabase = new(
        Path.Combine(Path.GetTempPath(), "codi-unused.db"),
        BusyTimeoutMs);

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"codi-t0-connection-{Guid.NewGuid():N}.db");

    private SqliteConnectionFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new SqliteConnectionFactory(new SqliteDatabase(_databasePath, BusyTimeoutMs));
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        foreach (var path in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        return Task.CompletedTask;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ApriAsync_ApplicaForeignKeysSincronizzazioneETimeout()
    {
        await using var connection = await _factory.OpenAsync(CancellationToken.None);

        // Valori attesi dal DDL di §8.1: FULL corrisponde al livello 2 di SQLite.
        Assert.Equal(1L, await ReadInt64Async(connection, "PRAGMA foreign_keys;"));
        Assert.Equal(2L, await ReadInt64Async(connection, "PRAGMA synchronous;"));
        Assert.Equal(5000L, await ReadInt64Async(connection, "PRAGMA busy_timeout;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AttivazioneWAL_RiportaIlModoEffettivoLettoDalDatabase()
    {
        var reported = await _factory.EnableWriteAheadLoggingAsync(CancellationToken.None);

        Assert.Equal("wal", reported);

        await using var connection = await _factory.OpenAsync(CancellationToken.None);
        Assert.Equal("wal", await ReadTextAsync(connection, "PRAGMA journal_mode;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ApriAsync_ConAttesaConfigurata_ApplicaIlValoreDellaConfigurazione()
    {
        // §8.1: l'attesa su database occupato viene dalla configurazione, non da una costante.
        const int configuredTimeoutMs = 2500;
        var factory = new SqliteConnectionFactory(new SqliteDatabase(_databasePath, configuredTimeoutMs));

        await using var connection = await factory.OpenAsync(CancellationToken.None);

        Assert.Equal(
            (long)configuredTimeoutMs,
            await ReadInt64Async(connection, "PRAGMA busy_timeout;"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ApriAsync_QuandoApplicazionePragmaFallisce_SmaltisceLaConnessione()
    {
        var connection = new StubConnection();
        var factory = new SqliteConnectionFactory(UnusedDatabase, () => connection);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.OpenAsync(CancellationToken.None));

        Assert.True(
            connection.WasDisposed,
            "La factory deve smaltire la connessione che non ha trasferito al chiamante.");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ApriAsync_QuandoAperturaAnnullata_SmaltisceLaConnessione()
    {
        var connection = new CancelOnOpenConnection();
        var factory = new SqliteConnectionFactory(UnusedDatabase, () => connection);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => factory.OpenAsync(CancellationToken.None));

        Assert.True(
            connection.WasDisposed,
            "La factory deve smaltire la connessione che non ha trasferito al chiamante.");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Transazione_AlDisposeSenzaCommit_NonLasciaScritture()
    {
        await using (var setup = await _factory.OpenAsync(CancellationToken.None))
        {
            await using var create = setup.CreateCommand();
            create.CommandText = CreateTableSql;
            await create.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await using var connection = await _factory.OpenAsync(CancellationToken.None);

        await using (var transaction = await connection.BeginTransactionAsync(CancellationToken.None))
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                "INSERT INTO prototype_row (id, created_at_utc_ms, note) VALUES ($id, 1, 'mai confermata');";

            var id = insert.CreateParameter();
            id.ParameterName = "$id";
            id.Value = Guid.NewGuid().ToString("D");
            insert.Parameters.Add(id);

            await insert.ExecuteNonQueryAsync(CancellationToken.None);
            // Nessun commit: il dispose della transazione deve annullare (§8.2).
        }

        Assert.Equal(0L, await ReadInt64Async(connection, "SELECT COUNT(*) FROM prototype_row;"));
    }

    private static async Task<long> ReadInt64Async(DbConnection connection, string sql)
    {
        var value = await ReadScalarAsync(connection, sql);
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task<string?> ReadTextAsync(DbConnection connection, string sql)
    {
        var value = await ReadScalarAsync(connection, sql);
        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static async Task<object?> ReadScalarAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(CancellationToken.None);
    }

    private const string PragmaFailure =
        "Applicazione dei PRAGMA non riuscita (connessione fittizia di test).";

    /// <summary>
    /// Connessione fittizia che si apre ma rifiuta i PRAGMA: prova lo smaltimento nel percorso
    /// di errore senza dipendere da un guasto reale del database. Non è sigillata perché il
    /// percorso di cancellazione la specializza.
    /// </summary>
    private class StubConnection : DbConnection
    {
        public bool WasDisposed { get; private set; }

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => string.Empty;

        public override string DataSource => string.Empty;

        public override string ServerVersion => string.Empty;

        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        public override void Close()
        {
        }

        public override void Open()
        {
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => new FailingPragmaCommand();

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Connessione fittizia la cui apertura viene annullata: prova lo smaltimento nel percorso
    /// di cancellazione senza un'attesa reale (§20.3).
    /// </summary>
    private sealed class CancelOnOpenConnection : StubConnection
    {
        public override Task OpenAsync(CancellationToken cancellationToken) =>
            Task.FromException(new OperationCanceledException(cancellationToken));
    }

    private sealed class FailingPragmaCommand : DbCommand
    {
        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; }

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection { get; set; }

        protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override void Prepare()
        {
        }

        public override int ExecuteNonQuery() => throw new InvalidOperationException(PragmaFailure);

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) =>
            Task.FromException<int>(new InvalidOperationException(PragmaFailure));

        public override object ExecuteScalar() => throw new InvalidOperationException(PragmaFailure);

        protected override DbParameter CreateDbParameter() => throw new NotSupportedException();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            throw new NotSupportedException();
    }
}
