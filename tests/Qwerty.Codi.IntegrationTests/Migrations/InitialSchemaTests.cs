using System.Data.Common;
using Microsoft.Data.Sqlite;
using Qwerty.Codi.Infrastructure.Migrations;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Migrations;

/// <summary>
/// Vincoli della prima migrazione (§7.1, §7.2): unicità del nome normalizzato, stati booleani,
/// coerenza fra tipo di credenziale e identità, chiave esterna e scadenza successiva
/// all'emissione. Ogni test parte da un database temporaneo appena migrato (§20.3).
/// </summary>
public sealed class InitialSchemaTests : IAsyncLifetime
{
    private const long IssuedAtUtcMs = 1_775_000_000_000;

    private const long ExpiresAtUtcMs = IssuedAtUtcMs + 3_600_000;

    /// <summary>Codice SQLITE_CONSTRAINT: il vincolo è stato violato, non è mancata una tabella.</summary>
    private const int ConstraintViolationCode = 19;

    private TempSqliteDatabase _database = null!;

    public async Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();

        var runner = new MigrationRunner(_database.Factory, new TestTimeProvider());
        await runner.ApplyAsync(
            MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly),
            CancellationToken.None);
    }

    public Task DisposeAsync()
    {
        _database.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminUser_ConNomeNormalizzatoRipetuto_ViolaUnicita()
    {
        // Arrange
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await InsertAdminUserAsync(connection, Guid.NewGuid(), "Maestra", "maestra");

        // Act
        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            InsertAdminUserAsync(connection, Guid.NewGuid(), "MAESTRA", "maestra"));

        // Assert
        Assert.Equal(ConstraintViolationCode, exception.SqliteErrorCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminUser_ConStatoNonBooleano_ViolaIlVincolo()
    {
        // Arrange
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        // Act
        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            InsertAdminUserAsync(connection, Guid.NewGuid(), "Maestra", "maestra", isActive: 2));

        // Assert
        Assert.Equal(ConstraintViolationCode, exception.SqliteErrorCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CredentialSession_ConAmministratoreInesistente_ViolaLaChiaveEsterna()
    {
        // Arrange
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        var existingAdminId = Guid.NewGuid();
        await InsertAdminUserAsync(connection, existingAdminId, "Maestra", "maestra");

        // Act
        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            ExecuteSessionInsertAsync(connection, "Admin", Guid.NewGuid().ToString("D"), null));

        // Assert
        Assert.Equal(ConstraintViolationCode, exception.SqliteErrorCode);

        // Controprova: con un amministratore esistente la stessa riga è ammessa, quindi il
        // vincolo non rifiuta ogni inserimento.
        await ExecuteSessionInsertAsync(connection, "Admin", existingAdminId.ToString("D"), null);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CredentialSession_ConTipoEIdentitaIncoerenti_ViolaIlVincolo()
    {
        // Arrange
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        var adminId = Guid.NewGuid();
        await InsertAdminUserAsync(connection, adminId, "Maestra", "maestra");

        // Act: tipo amministratore senza amministratore, poi tipo amministratore con entrambe
        // le identità, infine tipo partecipante senza partecipante.
        var senzaAmministratore = await Assert.ThrowsAsync<SqliteException>(() =>
            ExecuteSessionInsertAsync(connection, "Admin", null, null));
        var conEntrambeLeIdentita = await Assert.ThrowsAsync<SqliteException>(() =>
            ExecuteSessionInsertAsync(connection, "Admin", adminId.ToString("D"), Guid.NewGuid().ToString("D")));
        var senzaPartecipante = await Assert.ThrowsAsync<SqliteException>(() =>
            ExecuteSessionInsertAsync(connection, "Participant", null, null));

        // Assert
        Assert.Equal(ConstraintViolationCode, senzaAmministratore.SqliteErrorCode);
        Assert.Equal(ConstraintViolationCode, conEntrambeLeIdentita.SqliteErrorCode);
        Assert.Equal(ConstraintViolationCode, senzaPartecipante.SqliteErrorCode);

        // Controprova: la credenziale di partecipante con la sua identità è ammessa.
        await ExecuteSessionInsertAsync(connection, "Participant", null, Guid.NewGuid().ToString("D"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CredentialSession_ConScadenzaNonSuccessivaAllEmissione_ViolaIlVincolo()
    {
        // Arrange
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        var participantId = Guid.NewGuid().ToString("D");

        // Act
        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            ExecuteSessionInsertAsync(
                connection,
                "Participant",
                null,
                participantId,
                expiresAtUtcMs: IssuedAtUtcMs));

        // Assert
        Assert.Equal(ConstraintViolationCode, exception.SqliteErrorCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CredentialSession_ConPrincipalKindSconosciuto_ViolaIlVincolo()
    {
        // Arrange
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);

        // Act
        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            ExecuteSessionInsertAsync(connection, "Teacher", null, Guid.NewGuid().ToString("D")));

        // Assert
        Assert.Equal(ConstraintViolationCode, exception.SqliteErrorCode);
    }

    private static async Task InsertAdminUserAsync(
        DbConnection connection,
        Guid id,
        string username,
        string normalizedUsername,
        int isActive = 1)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO admin_user (
                id, username, normalized_username, password_hash, security_stamp,
                is_active, failed_login_count, created_at_utc_ms, updated_at_utc_ms)
            VALUES (
                $id, $username, $normalizedUsername, $passwordHash, $securityStamp,
                $isActive, 0, $now, $now);
            """;

        AddParameter(command, "$id", id.ToString("D"));
        AddParameter(command, "$username", username);
        AddParameter(command, "$normalizedUsername", normalizedUsername);
        AddParameter(command, "$passwordHash", "AQAAAAIAAAA-hash-di-prova");
        AddParameter(command, "$securityStamp", Guid.NewGuid().ToString("D"));
        AddParameter(command, "$isActive", isActive);
        AddParameter(command, "$now", IssuedAtUtcMs);

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task ExecuteSessionInsertAsync(
        DbConnection connection,
        string principalKind,
        string? adminUserId,
        string? participantId,
        long expiresAtUtcMs = ExpiresAtUtcMs)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO credential_session (
                id, principal_kind, admin_user_id, participant_id, security_stamp,
                issued_at_utc_ms, expires_at_utc_ms, created_at_utc_ms, updated_at_utc_ms)
            VALUES (
                $id, $principalKind, $adminUserId, $participantId, $securityStamp,
                $issuedAtUtcMs, $expiresAtUtcMs, $issuedAtUtcMs, $issuedAtUtcMs);
            """;

        AddParameter(command, "$id", Guid.NewGuid().ToString("D"));
        AddParameter(command, "$principalKind", principalKind);
        AddParameter(command, "$adminUserId", adminUserId);
        AddParameter(command, "$participantId", participantId);
        AddParameter(command, "$securityStamp", Guid.NewGuid().ToString("D"));
        AddParameter(command, "$issuedAtUtcMs", IssuedAtUtcMs);
        AddParameter(command, "$expiresAtUtcMs", expiresAtUtcMs);

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
