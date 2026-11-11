using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Domain.Admin;
using Qwerty.Codi.Domain.Credentials;
using Qwerty.Codi.Infrastructure.Migrations;
using Qwerty.Codi.Infrastructure.Persistence;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Admin;

/// <summary>
/// Repository delle credenziali su un database temporaneo migrato (§7.2): andata e ritorno,
/// revoca registrata una volta sola e revoca concorrente che non sovrascrive.
/// </summary>
public sealed class CredentialSessionRepositoryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    private TempSqliteDatabase _database = null!;

    private ICredentialSessionRepository _repository = null!;
    private IUnitOfWork _work = null!;

    public async Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();

        var runner = new MigrationRunner(_database.Factory, new TestTimeProvider());
        await runner.ApplyAsync(
            MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly),
            CancellationToken.None);

        _work = await _database.Units.CreateAsync();
        _repository = _work.Repo<ICredentialSessionRepository>();
    }

    public async Task DisposeAsync()
    {
        await _work.DisposeAsync();
        _database.Dispose();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InsertAsync_ConCredenzialeEmessa_SiRileggeUguale()
    {
        // Arrange
        var adminUserId = await InsertAdminUserAsync();
        var session = CredentialSession.IssueForAdmin(adminUserId, Now, Lifetime);

        // Act
        await _repository.StoreAsync(session, CancellationToken.None);

        // Assert
        var read = await _repository.ReadCredentialAsync(session.Id, CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(CredentialPrincipalKind.Admin, read.PrincipalKind);
        Assert.Equal(adminUserId, read.AdminUserId);
        Assert.Null(read.ParticipantId);
        Assert.Equal(session.SecurityStamp, read.SecurityStamp);
        Assert.Equal(Now, read.IssuedAtUtc);
        Assert.Equal(Now + Lifetime, read.ExpiresAtUtc);
        Assert.Null(read.RevokedAtUtc);
        Assert.Equal(1, read.RowVersion);
        Assert.True(read.IsUsableAt(Now, session.SecurityStamp));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FindAsync_ConIdentificativoSconosciuto_RestituisceNull()
    {
        Assert.Null(await _repository.ReadCredentialAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TryRevokeAsync_ConCredenzialeValida_RegistraLaRevoca()
    {
        // Arrange
        var session = CredentialSession.IssueForAdmin(await InsertAdminUserAsync(), Now, Lifetime);
        await _repository.StoreAsync(session, CancellationToken.None);

        // Act
        session.Revoke(Now.AddMinutes(30));
        var revoked = await _repository.TryRevokeAsync(session, CancellationToken.None);

        // Assert
        Assert.NotNull(revoked);
        Assert.Equal(2, revoked.RowVersion);

        var read = await _repository.ReadCredentialAsync(session.Id, CancellationToken.None);
        Assert.Equal(Now.AddMinutes(30), read!.RevokedAtUtc);
        Assert.False(read.IsUsableAt(Now.AddMinutes(31), session.SecurityStamp));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TryRevokeAsync_ConVersioneObsoleta_RestituisceNull()
    {
        // Arrange: due copie della stessa credenziale, come due richieste concorrenti.
        var session = CredentialSession.IssueForAdmin(await InsertAdminUserAsync(), Now, Lifetime);
        await _repository.StoreAsync(session, CancellationToken.None);

        var first = await _repository.ReadCredentialAsync(session.Id, CancellationToken.None);
        var second = await _repository.ReadCredentialAsync(session.Id, CancellationToken.None);

        first!.Revoke(Now.AddMinutes(5));
        Assert.NotNull(await _repository.TryRevokeAsync(first, CancellationToken.None));

        // Act
        second!.Revoke(Now.AddMinutes(6));
        var overwritten = await _repository.TryRevokeAsync(second, CancellationToken.None);

        // Assert
        Assert.Null(overwritten);

        var read = await _repository.ReadCredentialAsync(session.Id, CancellationToken.None);
        Assert.Equal(Now.AddMinutes(5), read!.RevokedAtUtc);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TryRevokeAsync_ConCredenzialeNonRevocataInMemoria_Rifiuta()
    {
        var session = CredentialSession.IssueForAdmin(Guid.NewGuid(), Now, Lifetime);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repository.TryRevokeAsync(session, CancellationToken.None));
    }

    private async Task<Guid> InsertAdminUserAsync()
    {
        var users = _work.Repo<IAdminUserRepository>();
        var user = AdminUser.Create("Maestra", "impronta-di-prova", Now);

        Assert.True(await users.TryInsertAsync(user, CancellationToken.None));

        return user.Id;
    }
}
