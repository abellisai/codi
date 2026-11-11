using System.Data.Common;
using Microsoft.Data.Sqlite;
using Qwerty.Codi.Infrastructure.Migrations;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Migrations;

/// <summary>Vincoli reali della migrazione consolidata, su un file temporaneo per ogni caso.</summary>
public sealed class OrganizationExerciseSchemaTests : IAsyncLifetime
{
    private const string Id = "11111111-1111-1111-1111-111111111111";
    private const string MissingId = "22222222-2222-2222-2222-222222222222";
    private TempSqliteDatabase _database = null!;

    public async Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();
        var runner = new MigrationRunner(_database.Factory, new TestTimeProvider());
        await runner.ApplyAsync(MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly), CancellationToken.None);
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO school (id, name, normalized_name, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($id, 'Scuola', 'SCUOLA', 1000, 1000);
            INSERT INTO classroom (id, school_id, name, normalized_name, school_year, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($id, $id, 'Terza A', 'TERZA A', '2026/2027', 1000, 1000);
            INSERT INTO course (id, title, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($id, 'Corso', 1000, 1000);
            INSERT INTO lesson (id, course_id, title, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($id, $id, 'Lezione', 1000, 1000);
            INSERT INTO exercise (id, title, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($id, 'Esercizio', 1000, 1000);
            INSERT INTO lesson_exercise (id, lesson_id, exercise_id, position, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($id, $id, $id, 1, 1000, 1000);
            INSERT INTO admin_user (id, username, normalized_username, password_hash, security_stamp, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($id, 'Admin', 'ADMIN', 'hash', 'stamp', 1000, 1000);
            INSERT INTO lesson_session (id, lesson_id, classroom_id, controller_admin_id, status, admissions_open, session_revision, class_default_json, started_at_utc_ms, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($id, $id, $id, $id, 'Active', 1, 1, '{"view":"Waiting"}', 1000, 1000, 1000);
            INSERT INTO station (id, classroom_id, browser_key_hash, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($id, $id, 'chiave-browser', 1000, 1000);
            INSERT INTO session_participant (id, session_id, station_id, display_name, admission_status, connection_epoch, joined_at_utc_ms, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($id, $id, $id, 'Luca', 'Admitted', 0, 1000, 1000, 1000);
            INSERT INTO participant_state (participant_id, session_id, effective_revision, view, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($id, $id, 1, 'Waiting', 1000, 1000);
            INSERT INTO idempotency_record (scope_key, request_key, request_hash, response_code, response_json, expires_at_utc_ms)
                VALUES ('scope-di-prova', 'chiave-di-prova', 'impronta-di-prova', 200, '{}', 9999);
            """;
        AddParameter(command, "$id", Id);
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync()
    {
        _database.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Schema_ConMigrazioneConsolidata_ContieneSoloLeTabelleCorrenti()
    {
        // Arrange
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";

        // Act
        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        // Assert
        Assert.Equal(new[] { "activity_assignment", "admin_user", "attempt_finalization_grant", "classroom", "command_delivery", "course", "credential_session", "exercise", "exercise_attempt", "exercise_draft", "exercise_version", "exercise_version_private", "idempotency_record", "lesson", "lesson_exercise", "lesson_session", "outbox_message", "participant_state", "schema_migration", "school", "session_command", "session_participant", "station" }, tables);
    }

    [Theory]
    [InlineData("school", "is_archived", 2)]
    [InlineData("classroom", "is_archived", -1)]
    [InlineData("course", "is_archived", 2)]
    [InlineData("lesson", "is_archived", 2)]
    [InlineData("exercise", "is_archived", 2)]
    [InlineData("school", "row_version", 0)]
    [InlineData("classroom", "row_version", 0)]
    [InlineData("course", "row_version", 0)]
    [InlineData("lesson", "row_version", 0)]
    [InlineData("exercise", "row_version", 0)]
    [InlineData("lesson_exercise", "row_version", 0)]
    [InlineData("lesson_exercise", "position", 0)]
    [InlineData("school", "name", " ")]
    [InlineData("school", "normalized_name", "")]
    [InlineData("classroom", "name", " ")]
    [InlineData("classroom", "normalized_name", "")]
    [InlineData("classroom", "school_year", "2026-2027")]
    [InlineData("classroom", "school_year", "AAAA/BBBB")]
    [InlineData("course", "title", " ")]
    [InlineData("lesson", "title", " ")]
    [InlineData("exercise", "title", " ")]
    [InlineData("station", "row_version", 0)]
    [InlineData("station", "browser_key_hash", " ")]
    [InlineData("session_participant", "row_version", 0)]
    [InlineData("session_participant", "admission_status", "Ammesso")]
    [InlineData("session_participant", "connection_epoch", -1)]
    [InlineData("session_participant", "display_name", "")]
    [Trait("Category", "Integration")]
    public async Task Update_ConValoreFuoriVincolo_RifiutaESalvaIlValorePrecedente(string table, string column, object invalidValue)
    {
        // Arrange: nomi SQL da un elenco chiuso del test; i valori sono parametrizzati.
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        var previous = await ReadAsync(connection, table, column);
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {table} SET {column} = $value WHERE id = $id;";
        AddParameter(command, "$value", invalidValue);
        AddParameter(command, "$id", Id);

        // Act
        var error = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());

        // Assert: SQLITE_CONSTRAINT_CHECK, non un errore di sintassi o tabella assente.
        Assert.Equal(275, error.SqliteExtendedErrorCode);
        Assert.Equal(previous, await ReadAsync(connection, table, column));
    }

    [Theory]
    [InlineData("school", "name", 120)]
    [InlineData("school", "normalized_name", 120)]
    [InlineData("classroom", "name", 120)]
    [InlineData("classroom", "normalized_name", 120)]
    [InlineData("course", "title", 160)]
    [InlineData("lesson", "title", 160)]
    [InlineData("exercise", "title", 160)]
    [InlineData("course", "description", 2000)]
    [InlineData("lesson", "description", 2000)]
    [InlineData("exercise", "description", 2000)]
    // Il limite sui grafemi lo verifica l'applicazione (§7.4): il CHECK accetta fino a 120
    // caratteri, che è la rete di sicurezza per un nome di più punti di codice per grafema.
    [InlineData("session_participant", "display_name", 120)]
    [Trait("Category", "Integration")]
    public async Task Update_AlLimiteEOltre_AccettaIlConfineERifiutaIlCarattereInPiu(string table, string column, int limit)
    {
        // Arrange
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {table} SET {column} = $value WHERE id = $id;";
        AddParameter(command, "$id", Id);
        AddParameter(command, "$value", new string('a', limit));
        await command.ExecuteNonQueryAsync();

        // Act
        command.Parameters["$value"].Value = new string('a', limit + 1);
        var error = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());

        // Assert
        Assert.Equal(275, error.SqliteExtendedErrorCode);
        Assert.Equal(new string('a', limit), await ReadAsync(connection, table, column));
    }

    [Theory]
    [InlineData("classroom", "school_id")]
    [InlineData("lesson", "course_id")]
    [InlineData("lesson_exercise", "lesson_id")]
    [InlineData("lesson_exercise", "exercise_id")]
    [InlineData("station", "classroom_id")]
    [InlineData("session_participant", "session_id")]
    [InlineData("session_participant", "station_id")]
    [Trait("Category", "Integration")]
    public async Task Update_ConRiferimentoInesistente_RifiutaESalvaIlCollegamento(string table, string column)
    {
        // Arrange
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {table} SET {column} = $missing WHERE id = $id;";
        AddParameter(command, "$missing", MissingId);
        AddParameter(command, "$id", Id);

        // Act
        var error = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());

        // Assert
        Assert.Equal(787, error.SqliteExtendedErrorCode);
        Assert.Equal(Id, await ReadAsync(connection, table, column));
    }

    [Theory]
    [InlineData("school")]
    [InlineData("course")]
    [InlineData("lesson")]
    [InlineData("exercise")]
    [InlineData("lesson_session")]
    [InlineData("station")]
    [Trait("Category", "Integration")]
    public async Task Delete_ConFiglioEsistente_ConservaIlGenitore(string table)
    {
        // Arrange
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table} WHERE id = $id;";
        AddParameter(command, "$id", Id);

        // Act
        var error = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());

        // Assert: SQLite segnala RESTRICT come vincolo generico/trigger.
        Assert.Equal(19, error.SqliteErrorCode);
        Assert.Equal(Id, await ReadAsync(connection, table, "id"));
    }

    [Theory]
    [InlineData("school", "id, name, normalized_name, created_at_utc_ms, updated_at_utc_ms", "$new, 'Altra', 'SCUOLA', 1000, 1000")]
    [InlineData("classroom", "id, school_id, name, normalized_name, school_year, created_at_utc_ms, updated_at_utc_ms", "$new, $id, 'Terza A', 'TERZA A', '2026/2027', 1000, 1000")]
    [InlineData("lesson_exercise", "id, lesson_id, exercise_id, position, created_at_utc_ms, updated_at_utc_ms", "$new, $id, $id, 1, 1000, 1000")]
    // La chiave del browser identifica il PC: la stessa chiave non entra due volte nel registro.
    [InlineData("station", "id, browser_key_hash, created_at_utc_ms, updated_at_utc_ms", "$new, 'chiave-browser', 1000, 1000")]
    [Trait("Category", "Integration")]
    public async Task Insert_ConChiaveUnicaDuplicata_NonInserisceLaSecondaRiga(string table, string columns, string values)
    {
        // Arrange
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {table} ({columns}) VALUES ({values});";
        AddParameter(command, "$id", Id);
        AddParameter(command, "$new", MissingId);

        // Act
        var error = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());

        // Assert
        Assert.Equal(2067, error.SqliteExtendedErrorCode);
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Insert_ConNomeDuplicatoNellaStessaSessione_AccettaIlSecondoLuca()
    {
        // Arrange: il nome appartiene alla partecipazione e non identifica nessuno (§5.2), quindi
        // due «Luca» nella stessa sessione sono una situazione normale e non una violazione.
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO session_participant (id, session_id, display_name, admission_status, connection_epoch, joined_at_utc_ms, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($new, $id, 'Luca', 'Admitted', 0, 2000, 2000, 2000);
            """;
        AddParameter(command, "$id", Id);
        AddParameter(command, "$new", MissingId);

        // Act
        await command.ExecuteNonQueryAsync();

        // Assert
        command.CommandText = "SELECT COUNT(*) FROM session_participant WHERE display_name = 'Luca';";
        Assert.Equal(2L, await command.ExecuteScalarAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Update_ConEsclusioneSenzaIstante_RifiutaLaRiga()
    {
        // Arrange: ammissione e istante di esclusione sono coerenti o niente (§7.4).
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE session_participant SET admission_status = 'Excluded' WHERE id = $id;";
        AddParameter(command, "$id", Id);

        // Act
        var error = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());

        // Assert
        Assert.Equal(275, error.SqliteExtendedErrorCode);
        Assert.Equal("Admitted", await ReadAsync(connection, "session_participant", "admission_status"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Update_ConStatoDelPartecipanteIncoerente_RifiutaLaRiga()
    {
        // Arrange: lo stato desiderato è uno per partecipante e riferisce la sessione della sua
        // partecipazione; la vista è un valore di §6.2 e non un testo qualunque.
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE participant_state SET view = 'Attesa' WHERE participant_id = $id;";
        AddParameter(command, "$id", Id);

        // Act
        var viewError = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());

        // Assert
        Assert.Equal(275, viewError.SqliteExtendedErrorCode);

        command.CommandText = "UPDATE participant_state SET session_id = $missing WHERE participant_id = $id;";
        command.Parameters.Clear();
        AddParameter(command, "$id", Id);
        AddParameter(command, "$missing", MissingId);

        var sessionError = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());

        // La sessione dello stato deve essere quella della partecipazione: la chiave esterna
        // composta rifiuta una sessione esistente ma diversa da quella della partecipazione.
        Assert.Equal(787, sessionError.SqliteExtendedErrorCode);
        Assert.Equal(Id, await ReadAsync(connection, "participant_state", "session_id", "participant_id"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Insert_ConChiaveDiIdempotenzaDuplicata_EConRispostaNonJson_Rifiuta()
    {
        // Arrange: la chiave del registro è la coppia (scope, chiave della richiesta) e la risposta
        // registrata è JSON, perché deve poter essere riletta (§7.5).
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO idempotency_record (scope_key, request_key, request_hash, response_code, response_json, expires_at_utc_ms)
                VALUES ('scope-di-prova', 'chiave-di-prova', 'altra-impronta', 200, '{}', 9999);
            """;

        // Act
        var duplicate = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());

        // Assert: la coppia è la chiave primaria della tabella, quindi il rifiuto è quello della
        // chiave primaria (1555) e non quello di un indice univoco (2067).
        Assert.Equal(1555, duplicate.SqliteExtendedErrorCode);

        command.CommandText = "UPDATE idempotency_record SET response_json = 'non-json' WHERE scope_key = 'scope-di-prova';";
        var invalidJson = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal(275, invalidJson.SqliteExtendedErrorCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Update_ConPosizioneTemporaneaNegativa_ConsenteIlRiordino()
    {
        // Arrange
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE lesson_exercise SET position = -1 WHERE id = $id;";
        AddParameter(command, "$id", Id);

        // Act
        await command.ExecuteNonQueryAsync();

        // Assert
        Assert.Equal(-1L, await ReadAsync(connection, "lesson_exercise", "position"));
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static async Task<object?> ReadAsync(DbConnection connection, string table, string column, string keyColumn = "id")
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM {table} WHERE {keyColumn} = $id;";
        AddParameter(command, "$id", Id);
        return await command.ExecuteScalarAsync();
    }
}
