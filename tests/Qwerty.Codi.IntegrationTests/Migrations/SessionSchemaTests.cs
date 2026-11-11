using System.Data.Common;
using Microsoft.Data.Sqlite;
using Qwerty.Codi.Infrastructure.Migrations;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Migrations;

/// <summary>
/// Vincoli reali di <c>lesson_session</c> (§6.1, §7.4, §7.6) su un database temporaneo migrato:
/// stato ammesso, ingressi chiusi alla terminazione, coerenza fra stato e istanti, JSON valido,
/// contatori positivi, chiavi esterne con <c>ON DELETE RESTRICT</c> e indice unico parziale della
/// sessione non terminata per classe.
/// </summary>
public sealed class SessionSchemaTests : IAsyncLifetime
{
    /// <summary>Codice SQLITE_CONSTRAINT: il vincolo è stato violato, non è mancata una tabella.</summary>
    private const int ConstraintViolationCode = 19;

    /// <summary>Codice esteso SQLITE_CONSTRAINT_UNIQUE: la seconda riga collide sull'indice.</summary>
    private const int UniqueViolationCode = 2067;

    private const string SchoolId = "10000000-0000-0000-0000-000000000001";
    private const string ClassroomOneId = "20000000-0000-0000-0000-000000000001";
    private const string ClassroomTwoId = "20000000-0000-0000-0000-000000000002";
    private const string ClassroomThreeId = "20000000-0000-0000-0000-000000000003";
    private const string ClassroomFourId = "20000000-0000-0000-0000-000000000004";
    private const string CourseId = "30000000-0000-0000-0000-000000000001";
    private const string LessonId = "40000000-0000-0000-0000-000000000001";
    private const string AdminId = "50000000-0000-0000-0000-000000000001";

    private const string DefaultJson = "{\"view\":\"Waiting\"}";

    private const long NowMs = 1_775_000_000_000;

    private TempSqliteDatabase _database = null!;

    public async Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();

        var runner = new MigrationRunner(_database.Factory, new TestTimeProvider());
        await runner.ApplyAsync(
            MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly),
            CancellationToken.None);

        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO school (id, name, normalized_name, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($school, 'Scuola', 'SCUOLA', $now, $now);
            INSERT INTO classroom (id, school_id, name, normalized_name, school_year, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($classroom1, $school, '1ª A', '1A', '2026/2027', $now, $now),
                       ($classroom2, $school, '2ª A', '2A', '2026/2027', $now, $now),
                       ($classroom3, $school, '3ª A', '3A', '2026/2027', $now, $now),
                       ($classroom4, $school, '4ª A', '4A', '2026/2027', $now, $now);
            INSERT INTO course (id, title, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($course, 'Corso', $now, $now);
            INSERT INTO lesson (id, course_id, title, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($lesson, $course, 'Lezione', $now, $now);
            INSERT INTO admin_user (
                id, username, normalized_username, password_hash, security_stamp,
                is_active, failed_login_count, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($admin, 'Maestra', 'MAESTRA', 'hash-di-prova', 'stamp-di-prova', 1, 0, $now, $now);
            """;
        AddParameter(command, "$school", SchoolId);
        AddParameter(command, "$classroom1", ClassroomOneId);
        AddParameter(command, "$classroom2", ClassroomTwoId);
        AddParameter(command, "$classroom3", ClassroomThreeId);
        AddParameter(command, "$classroom4", ClassroomFourId);
        AddParameter(command, "$course", CourseId);
        AddParameter(command, "$lesson", LessonId);
        AddParameter(command, "$admin", AdminId);
        AddParameter(command, "$now", NowMs);
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync()
    {
        _database.Dispose();
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("Running")]
    [InlineData("lobby")]
    [InlineData("")]
    [Trait("Category", "Integration")]
    public async Task Insert_ConStatoNonRiconosciuto_ViolaIlVincolo(string status)
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            InsertSessionAsync(connection, id: "60000000-0000-0000-0000-000000000001", ClassroomOneId, status));

        Assert.Equal(ConstraintViolationCode, exception.SqliteErrorCode);
        Assert.Equal(0L, await CountSessionsAsync(connection));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Insert_ConCiascunoStatoValidoEIstantiCoerenti_Ammesso()
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        await InsertSessionAsync(connection, id: "60000000-0000-0000-0000-000000000001", ClassroomOneId, "Lobby");
        await InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000002", ClassroomTwoId, "Active",
            admissionsOpen: true, sessionRevision: 2, startedAtUtcMs: NowMs);
        await InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000003", ClassroomThreeId, "Paused",
            admissionsOpen: false, sessionRevision: 3, startedAtUtcMs: NowMs);
        await InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000004", ClassroomFourId, "Ended",
            admissionsOpen: false, sessionRevision: 4, startedAtUtcMs: NowMs, endedAtUtcMs: NowMs + 60_000);

        Assert.Equal(4L, await CountSessionsAsync(connection));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Insert_ConSessioneConclusaPrimaDiEssereAvviata_Ammesso()
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        // Una lezione istanziata e mai avviata si può concludere: `Lobby` → `Ended` non registra un
        // avvio, e il vincolo di §6.1 deve ammetterlo. La 0002 lo vietava per errore.
        await InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000001", ClassroomOneId, "Ended",
            admissionsOpen: false, sessionRevision: 2, startedAtUtcMs: null, endedAtUtcMs: NowMs + 60_000);

        Assert.Equal(1L, await CountSessionsAsync(connection));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Insert_ConIngressiApertiSuSessioneTerminata_ViolaIlVincolo()
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000001", ClassroomOneId, "Ended",
            admissionsOpen: true, sessionRevision: 2, startedAtUtcMs: NowMs, endedAtUtcMs: NowMs + 60_000));

        Assert.Equal(ConstraintViolationCode, exception.SqliteErrorCode);
        Assert.Equal(0L, await CountSessionsAsync(connection));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Insert_ConLobbyEAvvioRegistrato_ViolaIlVincolo()
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000001", ClassroomOneId, "Lobby",
            startedAtUtcMs: NowMs));

        Assert.Equal(ConstraintViolationCode, exception.SqliteErrorCode);
        Assert.Equal(0L, await CountSessionsAsync(connection));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Insert_ConSessioneAvviataSenzaAvvio_ViolaIlVincolo()
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000001", ClassroomOneId, "Active",
            admissionsOpen: true, sessionRevision: 2, startedAtUtcMs: null));

        Assert.Equal(ConstraintViolationCode, exception.SqliteErrorCode);
        Assert.Equal(0L, await CountSessionsAsync(connection));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Insert_ConSessioneTerminataSenzaTermine_ViolaIlVincolo()
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000001", ClassroomOneId, "Ended",
            sessionRevision: 2, startedAtUtcMs: NowMs, endedAtUtcMs: null));

        Assert.Equal(ConstraintViolationCode, exception.SqliteErrorCode);
        Assert.Equal(0L, await CountSessionsAsync(connection));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Insert_ConSessioneNonTerminataMaConTermine_ViolaIlVincolo()
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000001", ClassroomOneId, "Lobby",
            endedAtUtcMs: NowMs + 60_000));

        Assert.Equal(ConstraintViolationCode, exception.SqliteErrorCode);
        Assert.Equal(0L, await CountSessionsAsync(connection));
    }

    [Theory]
    [InlineData("non-json")]
    [InlineData("")]
    [Trait("Category", "Integration")]
    public async Task Insert_ConDefaultNonJson_ViolaIlVincolo(string classDefaultJson)
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000001", ClassroomOneId, "Lobby",
            classDefaultJson: classDefaultJson));

        Assert.Equal(ConstraintViolationCode, exception.SqliteErrorCode);
        Assert.Equal(0L, await CountSessionsAsync(connection));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Insert_ConRevisioneNonPositiva_ViolaIlVincolo()
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000001", ClassroomOneId, "Lobby",
            sessionRevision: 0));

        Assert.Equal(ConstraintViolationCode, exception.SqliteErrorCode);
        Assert.Equal(0L, await CountSessionsAsync(connection));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Insert_ConVersioneDiRigaNonPositiva_ViolaIlVincolo()
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000001", ClassroomOneId, "Lobby",
            rowVersion: 0));

        Assert.Equal(ConstraintViolationCode, exception.SqliteErrorCode);
        Assert.Equal(0L, await CountSessionsAsync(connection));
    }

    [Theory]
    [InlineData("lesson", LessonId)]
    [InlineData("classroom", ClassroomOneId)]
    [InlineData("admin_user", AdminId)]
    [Trait("Category", "Integration")]
    public async Task Delete_ConSessioneCheRiferisceLaRiga_ConservaIlGenitore(string table, string id)
    {
        // Arrange: la sessione riferisce lezione, classe e amministratore.
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000001", ClassroomOneId, "Lobby");

        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table} WHERE id = $id;";
        AddParameter(command, "$id", id);

        // Act
        var error = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());

        // Assert: SQLite segnala RESTRICT come vincolo generico/trigger; la riga resta.
        Assert.Equal(ConstraintViolationCode, error.SqliteErrorCode);
        await using var read = connection.CreateCommand();
        read.CommandText = $"SELECT COUNT(*) FROM {table} WHERE id = $id;";
        AddParameter(read, "$id", id);
        Assert.Equal(1L, await read.ExecuteScalarAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Insert_ConSecondaSessioneNonTerminataSullaStessaClasse_ViolaLIndiceParziale()
    {
        // Arrange
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000001", ClassroomOneId, "Lobby");

        // Act: la seconda sessione non terminata sulla stessa classe non entra.
        var error = await Assert.ThrowsAsync<SqliteException>(() => InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000002", ClassroomOneId, "Active",
            admissionsOpen: true, sessionRevision: 2, startedAtUtcMs: NowMs));

        // Assert
        Assert.Equal(UniqueViolationCode, error.SqliteExtendedErrorCode);
        Assert.Equal(1L, await CountSessionsAsync(connection));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Insert_ConTerminataEPoiNuovaSullaStessaClasse_Ammesso()
    {
        // Arrange: la sessione conclusa resta nello storico e non blocca la nuova.
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000001", ClassroomOneId, "Ended",
            sessionRevision: 2, startedAtUtcMs: NowMs, endedAtUtcMs: NowMs + 60_000);

        // Act
        await InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000002", ClassroomOneId, "Lobby");

        // Assert
        Assert.Equal(2L, await CountSessionsAsync(connection));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Insert_ConDueNonTerminateSuClassiDiverse_Ammesso()
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        await InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000001", ClassroomOneId, "Lobby");
        await InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000002", ClassroomTwoId, "Lobby");

        Assert.Equal(2L, await CountSessionsAsync(connection));
    }

    [Theory]
    [InlineData("1234567")]
    [InlineData("123456789")]
    [InlineData("1234567a")]
    [InlineData("01234567")]
    [InlineData("1234 678")]
    [Trait("Category", "Integration")]
    public async Task Insert_ConCodiceDiIngressoNonValido_ViolaIlVincolo(string joinCode)
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000001", ClassroomOneId, "Active",
            admissionsOpen: true, sessionRevision: 2, startedAtUtcMs: NowMs, joinCode: joinCode));

        Assert.Equal(ConstraintViolationCode, exception.SqliteErrorCode);
        Assert.Equal(0L, await CountSessionsAsync(connection));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Insert_ConCodiceGiaAssegnato_ViolaLElencoUnico()
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        await InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000001", ClassroomOneId, "Active",
            admissionsOpen: true, sessionRevision: 2, startedAtUtcMs: NowMs, joinCode: "12345678");

        var exception = await Assert.ThrowsAsync<SqliteException>(() => InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000002", ClassroomTwoId, "Active",
            admissionsOpen: true, sessionRevision: 2, startedAtUtcMs: NowMs, joinCode: "12345678"));

        Assert.Equal(ConstraintViolationCode, exception.SqliteErrorCode);
        Assert.Equal(UniqueViolationCode, exception.SqliteExtendedErrorCode);
        Assert.Equal(1L, await CountSessionsAsync(connection));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Insert_ConDueSessioniSenzaCodice_Ammesso()
    {
        // Il codice NULL non partecipa all'elenco unico: le sessioni non avviate, e quelle create
        // prima dell'introduzione del codice, non collidono fra loro.
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        await InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000001", ClassroomOneId, "Lobby");
        await InsertSessionAsync(
            connection, id: "60000000-0000-0000-0000-000000000002", ClassroomTwoId, "Active",
            admissionsOpen: true, sessionRevision: 2, startedAtUtcMs: NowMs);

        Assert.Equal(2L, await CountSessionsAsync(connection));
    }

    private static async Task InsertSessionAsync(
        DbConnection connection,
        string id,
        string classroomId,
        string status,
        bool admissionsOpen = false,
        long sessionRevision = 1,
        string classDefaultJson = DefaultJson,
        long? startedAtUtcMs = null,
        long? endedAtUtcMs = null,
        long rowVersion = 1,
        string? joinCode = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO lesson_session (
                id, lesson_id, classroom_id, controller_admin_id, status, admissions_open,
                session_revision, class_default_json, join_code, started_at_utc_ms, ended_at_utc_ms,
                created_at_utc_ms, updated_at_utc_ms, row_version)
            VALUES (
                $id, $lesson, $classroom, $admin, $status, $admissionsOpen,
                $sessionRevision, $classDefaultJson, $joinCode, $startedAtUtcMs, $endedAtUtcMs,
                $now, $now, $rowVersion);
            """;
        AddParameter(command, "$id", id);
        AddParameter(command, "$lesson", LessonId);
        AddParameter(command, "$classroom", classroomId);
        AddParameter(command, "$admin", AdminId);
        AddParameter(command, "$status", status);
        AddParameter(command, "$admissionsOpen", admissionsOpen ? 1 : 0);
        AddParameter(command, "$sessionRevision", sessionRevision);
        AddParameter(command, "$classDefaultJson", classDefaultJson);
        AddParameter(command, "$joinCode", (object?)joinCode ?? DBNull.Value);
        AddParameter(command, "$startedAtUtcMs", (object?)startedAtUtcMs ?? DBNull.Value);
        AddParameter(command, "$endedAtUtcMs", (object?)endedAtUtcMs ?? DBNull.Value);
        AddParameter(command, "$now", NowMs);
        AddParameter(command, "$rowVersion", rowVersion);

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<long> CountSessionsAsync(DbConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM lesson_session;";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
