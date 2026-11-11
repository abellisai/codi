using System.Data.Common;
using Qwerty.Codi.Infrastructure.Persistence;
using RepoDb;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Persistence;

/// <summary>
/// Prototipo di T0 (§21.1): verifica che .NET 10 più SQLite e RepoDB rispettino le convenzioni
/// fisiche di §7.1 e il comportamento transazionale di §8.2. Ogni test usa un database
/// temporaneo dedicato e lo elimina alla fine (§20.3).
/// </summary>
public sealed class SqlitePersistencePrototypeTests : IAsyncLifetime
{
    private const string CreateTableSql = """
        CREATE TABLE prototype_row (
            id                TEXT    NOT NULL PRIMARY KEY,
            created_at_utc_ms INTEGER NOT NULL,
            kind              TEXT    NOT NULL CHECK (kind IN ('Text', 'Image', 'Demo')),
            note              TEXT    NULL,
            payload_json      TEXT    NOT NULL CHECK (json_valid(payload_json)),
            row_version       INTEGER NOT NULL DEFAULT 1 CHECK (row_version >= 1)
        );
        """;

    private const long CreatedAtUtcMs = 1_775_000_000_000;

    private const int BusyTimeoutMs = 5000;

    private const string PayloadJson = """{"blocks":[{"type":"grid_move"}]}""";

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"codi-t0-{Guid.NewGuid():N}.db");

    private SqliteConnectionFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new SqliteConnectionFactory(new SqliteDatabase(_databasePath, BusyTimeoutMs));
        await _factory.EnableWriteAheadLoggingAsync(CancellationToken.None);

        await using var connection = await _factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = CreateTableSql;
        await command.ExecuteNonQueryAsync(CancellationToken.None);
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
    public async Task Rilettura_DopoSalvataggio_ConservaUuidDataTestoNullEJson()
    {
        // Arrange
        var id = Guid.NewGuid();

        await using var connection = await _factory.OpenAsync(CancellationToken.None);

        // Act
        await using (var insert = CreateCommand(
            connection,
            """
            INSERT INTO prototype_row (id, created_at_utc_ms, kind, note, payload_json, row_version)
            VALUES ($id, $createdAtUtcMs, $kind, $note, $payloadJson, 1);
            """,
            ("$id", id.ToString("D")),
            ("$createdAtUtcMs", CreatedAtUtcMs),
            ("$kind", "Demo"),
            ("$note", null),
            ("$payloadJson", PayloadJson)))
        {
            await insert.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await using var select = CreateCommand(
            connection,
            """
            SELECT id, created_at_utc_ms, kind, note, payload_json, row_version
            FROM prototype_row
            WHERE id = $id;
            """,
            ("$id", id.ToString("D")));

        await using var reader = await select.ExecuteReaderAsync(CancellationToken.None);

        // Assert
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(id.ToString("D"), reader.GetString(0));
        Assert.Equal(CreatedAtUtcMs, reader.GetInt64(1));
        Assert.Equal("Demo", reader.GetString(2));
        Assert.True(await reader.IsDBNullAsync(3, CancellationToken.None));
        Assert.Equal(PayloadJson, reader.GetString(4));
        Assert.Equal(1L, reader.GetInt64(5));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Transazione_SenzaCommit_NonLasciaScritture()
    {
        // Arrange
        var id = Guid.NewGuid();

        await using var connection = await _factory.OpenAsync(CancellationToken.None);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken.None);

        // Act
        await using (var insert = CreateCommand(
            connection,
            """
            INSERT INTO prototype_row (id, created_at_utc_ms, kind, note, payload_json, row_version)
            VALUES ($id, $createdAtUtcMs, 'Demo', NULL, $payloadJson, 1);
            """,
            ("$id", id.ToString("D")),
            ("$createdAtUtcMs", CreatedAtUtcMs),
            ("$payloadJson", PayloadJson)))
        {
            insert.Transaction = transaction;
            await insert.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await transaction.RollbackAsync(CancellationToken.None);

        // Assert
        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM prototype_row;";
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync(CancellationToken.None))!);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Aggiornamento_ConRowVersionNonCorrente_NonModificaLaRiga()
    {
        // Arrange
        var id = Guid.NewGuid();

        await using var connection = await _factory.OpenAsync(CancellationToken.None);
        await InsertDemoRowAsync(connection, id);

        // Act
        var primoAggiornamento = await UpdateNoteAsync(connection, id, expectedVersion: 1, nextVersion: 2);
        var aggiornamentoObsoleto = await UpdateNoteAsync(connection, id, expectedVersion: 1, nextVersion: 2);

        // Assert
        Assert.Equal(1, primoAggiornamento);
        Assert.Equal(0, aggiornamentoObsoleto);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LetturaConRepoDb_MappaColonneSnakeCaseSuProprietaPascalCase()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Il provider RepoDB adottato va inizializzato una sola volta prima del primo accesso
        // (§8.1, D-01). La seconda chiamata verifica che l'operazione sia idempotente.
        RepoDbSqliteBootstrap.EnsureInitialized();
        RepoDbSqliteBootstrap.EnsureInitialized();

        await using var connection = await _factory.OpenAsync(CancellationToken.None);
        await InsertDemoRowAsync(connection, id);

        // Act
        var righe = await connection.ExecuteQueryAsync<PrototypeRow>(
            """
            SELECT id                AS Id,
                   created_at_utc_ms AS CreatedAtUtcMs,
                   kind              AS Kind,
                   note              AS Note,
                   payload_json      AS PayloadJson,
                   row_version       AS RowVersion
            FROM prototype_row;
            """);

        // Assert
        var riga = Assert.Single(righe);
        Assert.Equal(id.ToString("D"), riga.Id);
        Assert.Equal(CreatedAtUtcMs, riga.CreatedAtUtcMs);
        Assert.Equal("Demo", riga.Kind);
        Assert.Null(riga.Note);
        Assert.Equal(PayloadJson, riga.PayloadJson);
        Assert.Equal(1L, riga.RowVersion);
    }

    private static async Task InsertDemoRowAsync(DbConnection connection, Guid id)
    {
        await using var insert = CreateCommand(
            connection,
            """
            INSERT INTO prototype_row (id, created_at_utc_ms, kind, note, payload_json, row_version)
            VALUES ($id, $createdAtUtcMs, 'Demo', NULL, $payloadJson, 1);
            """,
            ("$id", id.ToString("D")),
            ("$createdAtUtcMs", CreatedAtUtcMs),
            ("$payloadJson", PayloadJson));
        await insert.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<int> UpdateNoteAsync(
        DbConnection connection,
        Guid id,
        long expectedVersion,
        long nextVersion)
    {
        await using var update = CreateCommand(
            connection,
            """
            UPDATE prototype_row
            SET note = 'saved', row_version = $nextVersion
            WHERE id = $id AND row_version = $expectedVersion;
            """,
            ("$id", id.ToString("D")),
            ("$expectedVersion", expectedVersion),
            ("$nextVersion", nextVersion));
        return await update.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static DbCommand CreateCommand(
        DbConnection connection,
        string commandText,
        params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = commandText;

        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        return command;
    }
}
