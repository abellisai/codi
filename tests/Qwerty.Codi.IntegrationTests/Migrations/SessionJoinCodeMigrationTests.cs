using System.Data.Common;
using Microsoft.Data.Sqlite;
using Qwerty.Codi.Infrastructure.Migrations;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Migrations;

/// <summary>
/// Aggiornamento reale allo schema 10 della migrazione `0010` (§8.3, §7.4): la colonna del codice di
/// ingresso si aggiunge a una tabella che esiste già, quindi la prova che conta non è lo schema
/// creato da zero — quello lo coprono gli altri test — ma un database fermo alla versione 9 con
/// dentro delle righe. Le sessioni precedenti devono restare leggibili con `join_code` nullo: sono
/// quelle create prima che il codice esistesse, e si raggiungono con la catena scuola → classe →
/// sessione.
/// </summary>
public sealed class SessionJoinCodeMigrationTests : IAsyncLifetime
{
    private const int ConstraintViolationCode = 19;

    private const int UniqueViolationCode = 2067;

    private const string SchoolId = "10000000-0000-0000-0000-000000000001";
    private const string ClassroomId = "20000000-0000-0000-0000-000000000001";
    private const string ClassroomTwoId = "20000000-0000-0000-0000-000000000002";
    private const string CourseId = "30000000-0000-0000-0000-000000000001";
    private const string LessonId = "40000000-0000-0000-0000-000000000001";
    private const string AdminId = "50000000-0000-0000-0000-000000000001";
    private const string SessionId = "60000000-0000-0000-0000-000000000001";

    private const string DefaultJson = "{\"view\":\"Waiting\"}";

    private const long NowMs = 1_775_000_000_000;

    private TempSqliteDatabase _database = null!;

    private MigrationCatalog _catalog = null!;

    public async Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();
        _catalog = MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly);

        // Il database nasce fermo alla versione precedente: è lo stato di un'installazione che
        // aggiorna l'applicazione senza ricreare i dati.
        var runner = new MigrationRunner(_database.Factory, new TestTimeProvider());
        var previous = new MigrationCatalog(_catalog.Scripts.Where(script => script.Version <= 9));
        await runner.ApplyAsync(previous, CancellationToken.None);

        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO school (id, name, normalized_name, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($school, 'Scuola', 'SCUOLA', $now, $now);
            INSERT INTO classroom (id, school_id, name, normalized_name, school_year, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($classroom1, $school, '1ª A', '1A', '2026/2027', $now, $now),
                       ($classroom2, $school, '2ª A', '2A', '2026/2027', $now, $now);
            INSERT INTO course (id, title, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($course, 'Corso', $now, $now);
            INSERT INTO lesson (id, course_id, title, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($lesson, $course, 'Lezione', $now, $now);
            INSERT INTO admin_user (
                id, username, normalized_username, password_hash, security_stamp,
                is_active, failed_login_count, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($admin, 'Maestra', 'MAESTRA', 'hash-di-prova', 'stamp-di-prova', 1, 0, $now, $now);
            INSERT INTO lesson_session (
                id, lesson_id, classroom_id, controller_admin_id, status, admissions_open,
                session_revision, class_default_json, started_at_utc_ms,
                created_at_utc_ms, updated_at_utc_ms, row_version)
                VALUES ($session, $lesson, $classroom1, $admin, 'Active', 1, 2, $defaultJson, $now,
                        $now, $now, 1);
            """;
        AddParameter(command, "$school", SchoolId);
        AddParameter(command, "$classroom1", ClassroomId);
        AddParameter(command, "$classroom2", ClassroomTwoId);
        AddParameter(command, "$course", CourseId);
        AddParameter(command, "$lesson", LessonId);
        AddParameter(command, "$admin", AdminId);
        AddParameter(command, "$session", SessionId);
        AddParameter(command, "$defaultJson", DefaultJson);
        AddParameter(command, "$now", NowMs);
        await command.ExecuteNonQueryAsync();

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
    public async Task ApplyAsync_DaSchemaNove_SchemaUndiciConLeColonneAggiunte()
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        // L'aggiornamento applica tutte le migrazioni mancanti, non soltanto l'ultima: il database
        // fermo alla 9 arriva alla versione più alta conosciuta da questa build.
        Assert.Equal(_catalog.LatestVersion, await SchemaVersionAsync(connection));
        Assert.Equal(1L, await CountAsync(connection, "SELECT COUNT(*) FROM lesson_session;"));
        Assert.True(await ColumnExistsAsync(connection, "lesson_session", "join_code"));
        Assert.True(await ColumnExistsAsync(connection, "exercise", "exercise_type"));

        // La sessione che esisteva prima dell'aggiornamento non ha codice: resta leggibile e si
        // raggiunge con la catena.
        Assert.True(await IsNullAsync(connection, "SELECT join_code FROM lesson_session WHERE id = $id;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ApplyAsync_DaSchemaNove_IlTipoDiEsercizioHaUnValoreEUnVincolo()
    {
        // Un esercizio che esisteva prima della colonna riceve il tipo di percorso: il registro dei
        // motori ne conosceva uno solo, e un valore diverso inventerebbe un tipo che nessuno ha
        // scelto. Il vincolo invece rifiuta un tipo che il prodotto non sa interpretare.
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        await InsertExerciseAsync(connection, "70000000-0000-0000-0000-000000000001");
        Assert.Equal(
            "GridPath",
            await ScalarTextAsync(
                connection,
                "SELECT exercise_type FROM exercise WHERE id = '70000000-0000-0000-0000-000000000001';"));

        var unknown = await Assert.ThrowsAsync<SqliteException>(
            () => SetExerciseTypeAsync(connection, "70000000-0000-0000-0000-000000000001", "ScratchProject"));
        Assert.Equal(ConstraintViolationCode, unknown.SqliteErrorCode);

        // Un tipo registrato è ammesso: il vincolo limita l'insieme, non il valore.
        await SetExerciseTypeAsync(connection, "70000000-0000-0000-0000-000000000001", "Quiz");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ApplyAsync_DaSchemaNove_IlCodiceRispettaFormatoEUnicita()
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        await SetJoinCodeAsync(connection, SessionId, "24681357");

        // Un codice malformato è rifiutato dal vincolo di formato.
        var malformed = await Assert.ThrowsAsync<SqliteException>(
            () => SetJoinCodeAsync(connection, SessionId, "2468135"));
        Assert.Equal(ConstraintViolationCode, malformed.SqliteErrorCode);

        var zeroLeading = await Assert.ThrowsAsync<SqliteException>(
            () => SetJoinCodeAsync(connection, SessionId, "02468135"));
        Assert.Equal(ConstraintViolationCode, zeroLeading.SqliteErrorCode);

        // Un codice già assegnato è rifiutato dall'indice unico.
        await InsertSessionAsync(connection, "60000000-0000-0000-0000-000000000002", ClassroomTwoId, "Active");
        var duplicate = await Assert.ThrowsAsync<SqliteException>(
            () => SetJoinCodeAsync(connection, "60000000-0000-0000-0000-000000000002", "24681357"));
        Assert.Equal(ConstraintViolationCode, duplicate.SqliteErrorCode);
        Assert.Equal(UniqueViolationCode, duplicate.SqliteExtendedErrorCode);
    }

    private static async Task SetJoinCodeAsync(DbConnection connection, string sessionId, string joinCode)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE lesson_session SET join_code = $joinCode WHERE id = $id;";
        AddParameter(command, "$joinCode", joinCode);
        AddParameter(command, "$id", sessionId);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    /// <summary>Inserisce un esercizio **senza dichiarare il tipo**: è la riga che esisteva prima.</summary>
    private static async Task InsertExerciseAsync(DbConnection connection, string id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO exercise (id, title, description, is_archived, created_at_utc_ms, updated_at_utc_ms, row_version)
                VALUES ($id, 'Esercizio precedente', '', 0, $now, $now, 1);
            """;
        AddParameter(command, "$id", id);
        AddParameter(command, "$now", NowMs);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task SetExerciseTypeAsync(DbConnection connection, string id, string exerciseType)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE exercise SET exercise_type = $type WHERE id = $id;";
        AddParameter(command, "$type", exerciseType);
        AddParameter(command, "$id", id);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<string> ScalarTextAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task InsertSessionAsync(
        DbConnection connection, string id, string classroomId, string status)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO lesson_session (
                id, lesson_id, classroom_id, controller_admin_id, status, admissions_open,
                session_revision, class_default_json, started_at_utc_ms,
                created_at_utc_ms, updated_at_utc_ms, row_version)
                VALUES ($id, $lesson, $classroom, $admin, $status, 1, 2, $defaultJson, $now, $now, $now, 1);
            """;
        AddParameter(command, "$id", id);
        AddParameter(command, "$lesson", LessonId);
        AddParameter(command, "$classroom", classroomId);
        AddParameter(command, "$admin", AdminId);
        AddParameter(command, "$status", status);
        AddParameter(command, "$defaultJson", DefaultJson);
        AddParameter(command, "$now", NowMs);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<long> SchemaVersionAsync(DbConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(version) FROM schema_migration;";

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> CountAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> ColumnExistsAsync(DbConnection connection, string table, string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $column;";
        AddParameter(command, "$column", column);

        return (long)(await command.ExecuteScalarAsync())! > 0;
    }

    private static async Task<bool> IsNullAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "$id", SessionId);

        return await command.ExecuteScalarAsync() is DBNull;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
