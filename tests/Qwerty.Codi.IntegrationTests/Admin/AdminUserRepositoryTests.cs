using Qwerty.Codi.Application.Abstractions;
using System.Data.Common;
using Qwerty.Codi.Domain.Admin;
using Qwerty.Codi.Infrastructure.Migrations;
using Qwerty.Codi.Infrastructure.Persistence;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Admin;

/// <summary>
/// Repository degli account amministrativi su un database temporaneo migrato (§7.2, §20.3):
/// andata e ritorno dell'entità, unicità del nome normalizzato, condizione del bootstrap e
/// ricostruzione che non si fida dei dati letti.
/// </summary>
public sealed class AdminUserRepositoryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private const string Hash = "AQAAAAIAAAA-impronta-di-prova";

    private TempSqliteDatabase _database = null!;

    private IAdminUserRepository _repository = null!;
    private IUnitOfWork _work = null!;

    public async Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();

        var runner = new MigrationRunner(_database.Factory, new TestTimeProvider());
        await runner.ApplyAsync(
            MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly),
            CancellationToken.None);

        _work = await _database.Units.CreateAsync();
        _repository = _work.Repo<IAdminUserRepository>();
    }

    public async Task DisposeAsync()
    {
        await _work.DisposeAsync();
        _database.Dispose();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TryInsertAsync_ConAccountNuovo_SiRileggeUguale()
    {
        // Arrange
        var user = AdminUser.Create("Maestra", Hash, Now);

        // Act
        var inserted = await _repository.TryInsertAsync(user, CancellationToken.None);

        // Assert
        Assert.True(inserted);

        var read = await _repository.ReadAdminAsync("maestra", CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(user.Id, read.Id);
        Assert.Equal("Maestra", read.Username);
        Assert.Equal("maestra", read.NormalizedUsername);
        Assert.Equal(Hash, read.PasswordHash);
        Assert.Equal(user.SecurityStamp, read.SecurityStamp);
        Assert.True(read.IsActive);
        Assert.Equal(Now, read.CreatedAtUtc);
        Assert.Equal(1, read.RowVersion);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TryInsertAsync_ConNomeNormalizzatoGiaInUso_RifiutaSenzaDuplicare()
    {
        // Arrange
        var first = AdminUser.Create("Maestra", Hash, Now);
        Assert.True(await _repository.TryInsertAsync(first, CancellationToken.None));

        // Act: stessa normalizzazione, scrittura diversa. Il vincolo unico è sul nome
        // normalizzato, quindi il secondo account non deve nascere.
        var second = AdminUser.Create("  MAESTRÀ  ", Hash, Now);
        var inserted = await _repository.TryInsertAsync(second, CancellationToken.None);

        // Assert
        Assert.False(inserted);

        var found = await _repository.ReadAdminAsync("maestra", CancellationToken.None);
        Assert.Equal(first.Id, found!.Id);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ReadAdminAsync_ConNomeAssente_RestituisceNull()
    {
        var found = await _repository.ReadAdminAsync("sconosciuto", CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExistsActiveAsync_SenzaAccountPrimaEPoiConAccount_SegueLoStato()
    {
        Assert.False(await _repository.ExistsAsync(entity => entity.IsActiveFlag == 1, CancellationToken.None));

        Assert.True(await _repository.TryInsertAsync(
            AdminUser.Create("Maestra", Hash, Now),
            CancellationToken.None));

        Assert.True(await _repository.ExistsAsync(entity => entity.IsActiveFlag == 1, CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ReadAdminAsync_ConRigaIncoerente_RifiutaLaRicostruzione()
    {
        // Arrange: la riga rispetta i vincoli della tabella ma è incoerente per il dominio,
        // perché il nome normalizzato non corrisponde al nome. Se la lettura la accettasse, una
        // scrittura esterna potrebbe introdurre due account per lo stesso docente.
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO admin_user (
                    id, username, normalized_username, password_hash, security_stamp,
                    is_active, failed_login_count, created_at_utc_ms, updated_at_utc_ms, row_version)
                VALUES ($id, 'Maestra', 'insegnante', $hash, 'stamp', 1, 0, $now, $now, 1);
                """;

            AddParameter(command, "$id", Guid.NewGuid().ToString("D"));
            AddParameter(command, "$hash", Hash);
            AddParameter(command, "$now", Now.ToUnixTimeMilliseconds());

            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        // Act
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _repository.ReadAdminAsync("insegnante", CancellationToken.None));

        // Assert
        Assert.Equal("normalizedUsername", exception.ParamName);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TryUpdateAsync_DopoUnTentativoFallito_PersisteConteggioEBlocco()
    {
        // Arrange
        Assert.True(await _repository.TryInsertAsync(AdminUser.Create("Maestra", Hash, Now), CancellationToken.None));
        var read = await _repository.ReadAdminAsync("maestra", CancellationToken.None);

        // Act: raggiunta la soglia, blocco e conteggio fanno parte della modifica da persistere.
        for (var attempt = 0; attempt < AdminUser.MaxFailedAttempts; attempt++)
        {
            read!.RegisterFailedAttempt(Now.AddMinutes(attempt));
        }

        var updated = await _repository.TryUpdateAsync(read!, CancellationToken.None);

        // Assert
        Assert.NotNull(updated);
        Assert.Equal(2, updated.RowVersion);

        var reloaded = await _repository.ReadAdminAsync("maestra", CancellationToken.None);
        Assert.Equal(AdminUser.MaxFailedAttempts, reloaded!.FailedLoginAttempts);
        Assert.Equal(
            Now.AddMinutes(AdminUser.MaxFailedAttempts - 1) + AdminUser.LockDuration,
            reloaded.LockedUntilUtc);
        Assert.True(reloaded.IsLockedAt(Now.AddMinutes(AdminUser.MaxFailedAttempts)));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TryUpdateAsync_ConVersioneObsoleta_RestituisceNullSenzaSovrascrivere()
    {
        // Arrange: due copie della stessa riga, come due richieste concorrenti.
        Assert.True(await _repository.TryInsertAsync(AdminUser.Create("Maestra", Hash, Now), CancellationToken.None));
        var first = await _repository.ReadAdminAsync("maestra", CancellationToken.None);
        var second = await _repository.ReadAdminAsync("maestra", CancellationToken.None);

        first!.RegisterFailedAttempt(Now.AddMinutes(1));
        Assert.NotNull(await _repository.TryUpdateAsync(first, CancellationToken.None));

        // Act
        second!.RegisterFailedAttempt(Now.AddMinutes(2));
        var overwritten = await _repository.TryUpdateAsync(second, CancellationToken.None);

        // Assert
        Assert.Null(overwritten);

        var reloaded = await _repository.ReadAdminAsync("maestra", CancellationToken.None);
        Assert.Equal(1, reloaded!.FailedLoginAttempts);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
