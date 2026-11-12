using System.Data.Common;
using Microsoft.Data.Sqlite;
using Qwerty.Codi.Infrastructure.Migrations;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Migrations;

/// <summary>
/// Aggiornamento reale dello schema 13 della migrazione <c>0013</c> (§8.3, QA/B3): la colonna
/// <c>reference_hash</c> si aggiunge a <c>exercise_version</c>, una tabella che esiste già e che è
/// immutabile per trigger. La prova che conta non è lo schema creato da zero — quello lo coprono
/// gli altri test — ma un database fermo alla versione 12 con versioni pubblicate e soluzioni
/// private dentro: l'aggiornamento deve preservarle, lasciare null l'impronta storica e mantenere
/// il trigger di immutabilità in vigore.
/// </summary>
public sealed class VersionReferenceHashMigrationTests : IAsyncLifetime
{
    private const int ConstraintViolationCode = 19;

    private const int ExpectedPreviousVersion = 12;

    private const string ExerciseId = "80000000-0000-0000-0000-000000000001";
    private const string AdminId = "50000000-0000-0000-0000-000000000001";
    private const string LegacyVersionId = "90000000-0000-0000-0000-000000000001";

    private const string DefinitionJson = "{\"schemaVersion\":1,\"exerciseType\":\"GridPath\",\"engineVersion\":\"gridpath-1\",\"title\":\"T\"}";
    private const string WorkspaceJson = "{\"blocks\":{\"languageVersion\":0,\"blocks\":[]}}";
    private const string SolutionJson = "{\"blocks\":{\"languageVersion\":0,\"blocks\":[{\"type\":\"aula_start\",\"id\":\"s\"}]}}";

    private const long NowMs = 1_778_000_000_000;

    private TempSqliteDatabase _database = null!;
    private MigrationCatalog _catalog = null!;

    public async Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();
        _catalog = MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly);

        // Il database nasce fermo alla versione precedente: è lo stato di un'installazione QA che
        // aggiorna l'applicazione senza ricreare i dati.
        var runner = new MigrationRunner(_database.Factory, new TestTimeProvider());
        var previous = new MigrationCatalog(_catalog.Scripts.Where(script => script.Version <= ExpectedPreviousVersion));
        await runner.ApplyAsync(previous, CancellationToken.None);

        await using (var connection = await _database.Factory.OpenAsync(CancellationToken.None))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO admin_user (
                    id, username, normalized_username, password_hash, security_stamp,
                    is_active, failed_login_count, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($admin, 'Maestra', 'MAESTRA', 'hash-di-prova', 'stamp-di-prova', 1, 0, $now, $now);
                INSERT INTO exercise (id, exercise_type, title, created_at_utc_ms, updated_at_utc_ms, row_version)
                VALUES ($exercise, 'GridPath', 'Prova', $now, $now, 1);
                INSERT INTO exercise_version (
                    id, exercise_id, version_number, definition_json, initial_workspace_json,
                    engine_version, schema_version, content_hash, published_by_admin_id, created_at_utc_ms)
                VALUES ($version, $exercise, 1, $definition, $workspace, 'gridpath-1', '1',
                        'a-di-legacy', $admin, $now);
                INSERT INTO exercise_version_private (exercise_version_id, reference_solution_json, author_notes)
                VALUES ($version, $solution, 'nota storica');
                """;
            AddParameter(command, "$admin", AdminId);
            AddParameter(command, "$exercise", ExerciseId);
            AddParameter(command, "$version", LegacyVersionId);
            AddParameter(command, "$definition", DefinitionJson);
            AddParameter(command, "$workspace", WorkspaceJson);
            AddParameter(command, "$solution", SolutionJson);
            AddParameter(command, "$now", NowMs);
            await command.ExecuteNonQueryAsync();
        }

        // Act: l'aggiornamento applica la sola migrazione nuova.
        await runner.ApplyAsync(_catalog, CancellationToken.None);
    }

    public Task DisposeAsync()
    {
        _database.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ApplyAsync_DaSchemaDodici_LaVersioneHaLaColonneDiRiferimento()
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        Assert.Equal(_catalog.LatestVersion, await SchemaVersionAsync(connection));
        Assert.True(await ColumnExistsAsync(connection, "exercise_version", "reference_hash"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ApplyAsync_ConVersioneStorica_PreservaRigheEDaImprontaNulla()
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        // La versione pubblicata prima della migrazione non aveva impronta di riferimento: la
        // migrazione non inventa dati, la riga resta com'era e l'impronta storica è nulla.
        Assert.Equal(1L, await CountAsync(connection, "SELECT COUNT(*) FROM exercise_version;"));
        Assert.Equal(1L, await CountAsync(connection, "SELECT COUNT(*) FROM exercise_version_private;"));
        Assert.True(await IsNullAsync(
            connection, "SELECT reference_hash FROM exercise_version WHERE id = $id;"));

        // I contenuti esistenti restano leggibili e identici.
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT v.content_hash, v.definition_json, p.reference_solution_json, p.author_notes
            FROM exercise_version v
            JOIN exercise_version_private p ON p.exercise_version_id = v.id
            WHERE v.id = $id;
            """;
        AddParameter(command, "$id", LegacyVersionId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal("a-di-legacy", reader.GetString(0));
        Assert.Equal(DefinitionJson, reader.GetString(1));
        Assert.Equal(SolutionJson, reader.GetString(2));
        Assert.Equal("nota storica", reader.GetString(3));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ApplyAsync_DopoLAggiornamento_LImmutabilitaRestaInVigore()
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();

        // Il trigger di 0004 sopravvive all'ALTER TABLE: la migrazione non è una finestra per
        // aggiornare le versioni pubblicate, e nessun riempimento successivo può farlo da SQL.
        command.CommandText = "UPDATE exercise_version SET reference_hash = $hash WHERE id = $id;";
        AddParameter(command, "$hash", new string('a', 64));
        AddParameter(command, "$id", LegacyVersionId);

        var failure = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync(CancellationToken.None));
        Assert.Equal(ConstraintViolationCode, failure.SqliteErrorCode);
        Assert.Contains("immutabile", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ApplyAsync_DopoLAggiornamento_UnInserimentoSenzaImprontaRestaLeggibile()
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        // La colonna è nullable: una scrittura che non la nomina (la stessa usata dai test di
        // assegnazione) resta valida e legge null, non un fallimento.
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO exercise_version (
                    id, exercise_id, version_number, definition_json, initial_workspace_json,
                    engine_version, schema_version, content_hash, published_by_admin_id, created_at_utc_ms)
                VALUES ($id, $exercise, 2, $definition, $workspace, 'gridpath-1', '1',
                        'b-di-prova', $admin, $now);
                """;
            AddParameter(insert, "$id", "90000000-0000-0000-0000-000000000002");
            AddParameter(insert, "$exercise", ExerciseId);
            AddParameter(insert, "$definition", DefinitionJson);
            AddParameter(insert, "$workspace", WorkspaceJson);
            AddParameter(insert, "$admin", AdminId);
            AddParameter(insert, "$now", NowMs);
            await insert.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await using var check = connection.CreateCommand();
        check.CommandText = "SELECT reference_hash FROM exercise_version WHERE version_number = 2;";
        var scalar = await check.ExecuteScalarAsync(CancellationToken.None);
        Assert.True(scalar is null || scalar is DBNull, $"valore restituito: {scalar?.GetType().Name} '{scalar}'");
    }

    private static async Task<long> SchemaVersionAsync(DbConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(version) FROM schema_migration;";
        return Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken.None));
    }

    private static async Task<long> CountAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken.None));
    }

    private static async Task<bool> ColumnExistsAsync(DbConnection connection, string table, string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM pragma_table_info($table) WHERE name = $column;
            """;
        AddParameter(command, "$table", table);
        AddParameter(command, "$column", column);
        return Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken.None)) > 0;
    }

    private static async Task<bool> IsNullAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "$id", LegacyVersionId);
        var scalar = await command.ExecuteScalarAsync(CancellationToken.None);
        return scalar is null || scalar is DBNull;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
