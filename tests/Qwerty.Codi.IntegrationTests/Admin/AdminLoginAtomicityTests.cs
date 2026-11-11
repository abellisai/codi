using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Admin;
using Qwerty.Codi.Domain.Credentials;
using Qwerty.Codi.Infrastructure.Migrations;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Admin;

public sealed class AdminLoginAtomicityTests : IAsyncLifetime
{
    private TempSqliteDatabase _database = null!;
    private AdminUser _admin = null!;
    public async Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();
        await new MigrationRunner(_database.Factory, new TestTimeProvider()).ApplyAsync(
            MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly), CancellationToken.None);
        _admin = AdminUser.Create("Maestra", "correct", TestTimeProvider.Instant);
        _admin.RegisterFailedAttempt(TestTimeProvider.Instant);
        await using var seed = await _database.Units.CreateAsync();
        await seed.Repo<IAdminUserRepository>().InsertAsync(AdminUserEntity.FromDomain(_admin));
    }

    public Task DisposeAsync() { _database.Dispose(); return Task.CompletedTask; }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Login_DopoInserimentoCredenzialeFallitoOAnnullato_AnnullaAncheLAggiornamentoAccount(bool cancel)
    {
        // Arrange: entrambe le scritture raggiungono SQLite, poi si inietta l'errore.
        using var cancellation = new CancellationTokenSource();
        var units = new DecoratingUnitOfWorkContext(_database.Units, repository =>
            repository is ICredentialSessionRepository sessions
                ? new FailAfterInsert(sessions, cancellation, cancel) : repository);
        var services = new ServiceCollection();
        services.AddCodiRequests();
        services.AddSingleton<IUnitOfWorkContext>(units);
        services.AddSingleton<IAdminPasswordHasher, TestPasswordHasher>();
        services.AddSingleton<TimeProvider>(new TestTimeProvider());
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var payload = JsonDocument.Parse("""{"username":"Maestra","password":"correct"}""");

        // Act
        Task<object?> ExecuteAsync() => scope.ServiceProvider.GetRequiredService<CodiRequestExecutor>().ExecuteAsync(
            CodiRequestKind.Command, "LoginAdmin", CodiRequestChannel.Http, payload.RootElement,
            CodiRequestContext.Anonymous, cancellation.Token).AsTask();
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(ExecuteAsync);
        else await Assert.ThrowsAsync<InvalidOperationException>(ExecuteAsync);

        // Assert: il login fallito non azzera il conteggio e non lascia una sessione emessa.
        await using var read = await _database.Units.CreateAsync();
        var stored = await read.Repo<IAdminUserRepository>().GetAsync(_admin.Id.ToString("D"));
        Assert.Equal(1, stored!.FailedLoginAttempts);
        Assert.Equal(_admin.RowVersion, stored.RowVersion);
        Assert.Empty(await read.Repo<ICredentialSessionRepository>().GetAllAsync());
    }

    private sealed class TestPasswordHasher : IAdminPasswordHasher
    {
        public string Hash(string password) => password;
        public bool Verify(string passwordHash, string password) => passwordHash == password;
    }

    private sealed class FailAfterInsert(
        ICredentialSessionRepository inner, CancellationTokenSource cancellation, bool cancel)
        : RepositoryTestDecorator<CredentialSessionEntity, string>(inner), ICredentialSessionRepository
    {
        public override async Task<string> InsertAsync(CredentialSessionEntity entity, CancellationToken cancellationToken = default)
        {
            await inner.InsertAsync(entity, cancellationToken);
            if (cancel)
            {
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            throw new InvalidOperationException("Guasto dopo l'inserimento della credenziale.");
        }

        public Task<CredentialSession?> TryRevokeAsync(CredentialSession session, CancellationToken cancellationToken) =>
            inner.TryRevokeAsync(session, cancellationToken);
    }
}
