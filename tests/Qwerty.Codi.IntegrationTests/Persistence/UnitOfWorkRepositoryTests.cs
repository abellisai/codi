using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Admin;
using Qwerty.Codi.Domain.Credentials;
using Qwerty.Codi.Infrastructure.Migrations;
using Qwerty.Codi.Infrastructure.Persistence;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Persistence;

public sealed class UnitOfWorkRepositoryTests : IAsyncLifetime
{
    private TempSqliteDatabase _database = null!;
    public async Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();
        await new MigrationRunner(_database.Factory, new TestTimeProvider()).ApplyAsync(
            MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly), CancellationToken.None);
        Assert.Equal("wal", await _database.Factory.EnableWriteAheadLoggingAsync(CancellationToken.None));
    }

    public Task DisposeAsync() { _database.Dispose(); return Task.CompletedTask; }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Crud_ConMappingEVersione_RileggeAggiornaEEliminaSenzaSovrascrivereCopieObsolete()
    {
        // Arrange
        await using var work = await _database.Units.CreateAsync(useTransaction: true);
        var users = work.Repo<IAdminUserRepository>();
        var row = AdminUserEntity.FromDomain(AdminUser.Create("Maestra", "hash", TestTimeProvider.Instant));

        // Act: identificativo UUID applicativo, non AUTOINCREMENT del database.
        var id = await users.InsertAsync(row);
        var first = await users.GetAsync(id);
        var stale = await users.GetAsync(id);
        first!.Username = "Maestro";
        first.NormalizedUsername = "maestro";
        Assert.True(await users.UpdateAsync(first));
        stale!.Username = "Sovrascrittura";
        stale.NormalizedUsername = "sovrascrittura";

        // Assert: il CRUD generico usa row_version anche per Delete.
        Assert.Equal(row.Id, id);
        Assert.Equal(2, first.RowVersion);
        Assert.False(await users.UpdateAsync(stale));
        Assert.Equal(1, stale.RowVersion);
        Assert.False(await users.DeleteAsync(stale));
        var stored = Assert.Single(await users.GetAllAsync());
        Assert.Equal("Maestro", stored.Username);
        Assert.Equal("maestro", stored.NormalizedUsername);
        Assert.Equal(row.SecurityStamp, stored.SecurityStamp);
        Assert.Equal(TestTimeProvider.Instant, stored.ToDomain().CreatedAtUtc);
        Assert.True(await users.DeleteAsync(first));
        Assert.Null(await users.GetAsync(id));
        await work.CommitAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task QueryAsync_ConPredicatoCompostoNullEParametri_TraduceIlFiltroSulleColonneMappate()
    {
        // Arrange
        var admin = AdminUser.Create("Maestra", "hash", TestTimeProvider.Instant);
        var current = CredentialSession.IssueForAdmin(admin.Id, TestTimeProvider.Instant, TimeSpan.FromHours(8));
        var revoked = CredentialSession.IssueForAdmin(admin.Id, TestTimeProvider.Instant, TimeSpan.FromHours(8));
        revoked.Revoke(TestTimeProvider.Instant.AddMinutes(1));
        await using var work = await _database.Units.CreateAsync(useTransaction: true);
        await work.Repo<IAdminUserRepository>().InsertAsync(AdminUserEntity.FromDomain(admin));
        var sessions = work.Repo<ICredentialSessionRepository>();
        await sessions.InsertAsync(CredentialSessionEntity.FromDomain(current));
        await sessions.InsertAsync(CredentialSessionEntity.FromDomain(revoked));
        var adminId = admin.Id.ToString("D");
        var expires = current.ExpiresAtUtc.ToUnixTimeMilliseconds();
        var injectedName = "maestra' OR 1=1 --";
        var currentId = current.Id.ToString("D");
        var revokedId = revoked.Id.ToString("D");

        // Act
        var active = await sessions.QueryAsync(entity => entity.AdminUserId == adminId &&
            entity.RevokedAtUtcMs == null && entity.ExpiresAtUtcMs >= expires);
        var either = await sessions.QueryAsync(entity => entity.Id == currentId || entity.Id == revokedId);
        var unrecognized = await work.Repo<IAdminUserRepository>().QueryAsync(
            entity => entity.NormalizedUsername == injectedName);

        // Assert: WHERE e parametri sono eseguiti da SQLite, nessun filtro in memoria.
        Assert.Equal(current.Id.ToString("D"), Assert.Single(active).Id);
        Assert.Equal(2, either.Count);
        Assert.Empty(unrecognized);
        Assert.True(await sessions.ExistsAsync(entity => entity.RevokedAtUtcMs != null));
        Assert.False(await sessions.ExistsAsync(entity => entity.PrincipalKind == "Participant"));
        Assert.Null(active[0].ParticipantId);
        Assert.Equal(nameof(CredentialPrincipalKind.Admin), active[0].PrincipalKind);
        await work.CommitAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CommitAsync_ConDueRepository_CondivideLaTransazioneEConfermaEntrambeLeRighe()
    {
        // Arrange
        var admin = AdminUser.Create("Maestra", "hash", TestTimeProvider.Instant);
        var session = CredentialSession.IssueForAdmin(admin.Id, TestTimeProvider.Instant, TimeSpan.FromHours(8));
        await using var write = await _database.Units.CreateAsync(useTransaction: true);
        var users = write.Repo<IAdminUserRepository>();

        // Act: la FK vede il padre non ancora confermato soltanto con la stessa transazione.
        await users.InsertAsync(AdminUserEntity.FromDomain(admin));
        await write.Repo<ICredentialSessionRepository>().InsertAsync(CredentialSessionEntity.FromDomain(session));
        await using (var outside = await _database.Units.CreateAsync())
        {
            Assert.Empty(await outside.Repo<IAdminUserRepository>().GetAllAsync());
            Assert.Empty(await outside.Repo<ICredentialSessionRepository>().GetAllAsync());
        }
        await write.CommitAsync();

        // Assert
        await using var read = await _database.Units.CreateAsync();
        Assert.Equal(admin.Id.ToString("D"), Assert.Single(await read.Repo<IAdminUserRepository>().GetAllAsync()).Id);
        Assert.Equal(session.Id.ToString("D"), Assert.Single(await read.Repo<ICredentialSessionRepository>().GetAllAsync()).Id);
        Assert.Throws<InvalidOperationException>(() => write.Repo<IAdminUserRepository>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => users.GetAllAsync());
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("dispose")]
    [InlineData("rollback")]
    [InlineData("exception")]
    [InlineData("cancellation")]
    public async Task UnitOfWork_SenzaCommitRiuscito_AnnullaLeScrittureDiEntrambiIRepository(string ending)
    {
        // Arrange
        var admin = AdminUser.Create("Maestra", "hash", TestTimeProvider.Instant);
        using var cancellation = new CancellationTokenSource();

        // Act
        async Task WriteAsync()
        {
            await using var work = await _database.Units.CreateAsync(useTransaction: true);
            await work.Repo<IAdminUserRepository>().InsertAsync(AdminUserEntity.FromDomain(admin));
            await work.Repo<ICredentialSessionRepository>().InsertAsync(CredentialSessionEntity.FromDomain(
                CredentialSession.IssueForAdmin(admin.Id, TestTimeProvider.Instant, TimeSpan.FromHours(8))));
            if (ending == "rollback") await work.RollbackAsync();
            if (ending == "exception") throw new InvalidOperationException("Guasto dopo entrambe le scritture.");
            if (ending == "cancellation")
            {
                cancellation.Cancel();
                await work.CommitAsync(cancellation.Token);
            }
        }
        if (ending == "exception") await Assert.ThrowsAsync<InvalidOperationException>(WriteAsync);
        else if (ending == "cancellation") await Assert.ThrowsAnyAsync<OperationCanceledException>(WriteAsync);
        else await WriteAsync();

        // Assert
        await using var read = await _database.Units.CreateAsync();
        Assert.Empty(await read.Repo<IAdminUserRepository>().GetAllAsync());
        Assert.Empty(await read.Repo<ICredentialSessionRepository>().GetAllAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteAsync_ConCredenzialeCollegata_RispettaLaForeignKeyRestrict()
    {
        // Arrange
        await using var work = await _database.Units.CreateAsync(useTransaction: true);
        var admin = AdminUser.Create("Maestra", "hash", TestTimeProvider.Instant);
        var row = AdminUserEntity.FromDomain(admin);
        var users = work.Repo<IAdminUserRepository>();
        await users.InsertAsync(row);
        await work.Repo<ICredentialSessionRepository>().InsertAsync(CredentialSessionEntity.FromDomain(
            CredentialSession.IssueForAdmin(admin.Id, TestTimeProvider.Instant, TimeSpan.FromHours(8))));

        // Act
        var failure = await Assert.ThrowsAsync<SqliteException>(() => users.DeleteAsync(row));

        // Assert
        Assert.Equal(19, failure.SqliteErrorCode);
        Assert.NotNull(await users.GetAsync(row.Id));
        Assert.Single(await work.Repo<ICredentialSessionRepository>().GetAllAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateAsync_ConNomeGiaOccupato_NonAvanzaLaVersioneNeSovrascriveLaRiga()
    {
        // Arrange
        await using var work = await _database.Units.CreateAsync(useTransaction: true);
        var users = work.Repo<IAdminUserRepository>();
        var first = AdminUserEntity.FromDomain(AdminUser.Create("Maestra", "hash", TestTimeProvider.Instant));
        var second = AdminUserEntity.FromDomain(AdminUser.Create("Maestro", "hash", TestTimeProvider.Instant));
        await users.InsertAsync(first);
        await users.InsertAsync(second);
        second.NormalizedUsername = first.NormalizedUsername;

        // Act
        var error = await Assert.ThrowsAsync<SqliteException>(() => users.UpdateAsync(second));

        // Assert: fallimento prima del commit, nessun incremento della copia o della riga.
        Assert.Equal(2067, error.SqliteExtendedErrorCode);
        Assert.Equal(1, second.RowVersion);
        var stored = await users.GetAsync(second.Id);
        Assert.Equal("maestro", stored!.NormalizedUsername);
        Assert.Equal(1, stored.RowVersion);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Repository_DopoDispose_NonRiapreUnaConnessioneNascosta()
    {
        // Arrange
        var work = await _database.Units.CreateAsync();
        var users = work.Repo<IAdminUserRepository>();
        Assert.Same(users, work.Repo<IAdminUserRepository>());

        // Act
        await work.DisposeAsync();

        // Assert
        Assert.Throws<ObjectDisposedException>(() => work.Repo<IAdminUserRepository>());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => users.GetAllAsync());
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateAsync_ConBeginFallito_LiberaLaConnessioneEPreservaLErroreOriginale(bool disposeThrows)
    {
        // Arrange
        var connection = new FailingBeginConnection(_database.Database.FilePath, disposeThrows);
        var services = new ServiceCollection();
        services.AddSingleton(new SqliteConnectionFactory(_database.Database, () => connection));
        services.AddCodiPersistence();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetRequiredService<IUnitOfWorkContext>().CreateAsync(useTransaction: true));

        // Assert
        Assert.Equal("BEGIN fallito", failure.Message);
        Assert.True(connection.WasDisposed);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    private sealed class FailingBeginConnection(string filePath, bool disposeThrows)
        : SqliteConnection(new SqliteConnectionStringBuilder { DataSource = filePath, Pooling = false }.ToString())
    {
        public bool WasDisposed { get; private set; }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new InvalidOperationException("BEGIN fallito");
        public override ValueTask DisposeAsync()
        {
            WasDisposed = true;
            base.Dispose();
            if (disposeThrows) throw new IOException("Pulizia fallita");
            return ValueTask.CompletedTask;
        }
    }
}
