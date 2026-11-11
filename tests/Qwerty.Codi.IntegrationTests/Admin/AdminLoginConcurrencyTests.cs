using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Admin;
using Qwerty.Codi.Infrastructure.Migrations;
using Qwerty.Codi.Infrastructure.Realtime;
using Qwerty.Codi.IntegrationTests.Realtime;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Admin;

public sealed class AdminLoginConcurrencyTests : IAsyncLifetime
{
    private TempSqliteDatabase _database = null!;

    public async Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();
        await new MigrationRunner(_database.Factory, new TestTimeProvider()).ApplyAsync(
            MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly), CancellationToken.None);
        await using var seed = await _database.Units.CreateAsync();
        Assert.True(await seed.Repo<IAdminUserRepository>().TryInsertAsync(
            AdminUser.Create("Maestra", "correct", TestTimeProvider.Instant), CancellationToken.None));
    }

    public Task DisposeAsync()
    {
        _database.Dispose();
        return Task.CompletedTask;
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(4, 4, false)]
    [InlineData(8, 5, true)]
    public async Task Login_ConLettureConcorrenti_NonPerdeTentativiERispettaIlBlocco(
        int requests, int expectedAttempts, bool expectedLock)
    {
        // Arrange: prime letture sincronizzate, ognuna sulla propria connessione.
        var barrier = new ReadBarrier(requests);
        var units = new DecoratingUnitOfWorkContext(_database.Units,
            repository => repository is IAdminUserRepository users ? new SynchronizedReads(users, barrier) : repository);
        await using var services = BuildServices(units);

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, requests).Select(_ => RejectionAsync(services, "wrong")));

        // Assert
        await using var read = await _database.Units.CreateAsync();
        var stored = await read.Repo<IAdminUserRepository>().ReadAdminAsync("maestra", CancellationToken.None);
        Assert.Equal(expectedAttempts, stored!.FailedLoginAttempts);
        Assert.Equal(expectedLock, stored.IsLockedAt(TestTimeProvider.Instant));
        Assert.All(results, result => Assert.Contains(result.Code,
            new[] { CodiErrorCodes.CredentialsRejected, CodiErrorCodes.AccountLocked }));
        if (expectedLock)
        {
            Assert.Contains(results, result => result.Code == CodiErrorCodes.AccountLocked);
            var correct = await RejectionAsync(services, "correct");
            Assert.Equal(CodiErrorCodes.AccountLocked, correct.Code);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LoginCorretto_ConBloccoPersistitoDuranteIlConflitto_NonEmetteCredenziali()
    {
        // Arrange: un'altra UoW conferma il blocco fra lettura e UPDATE del login.
        var units = new DecoratingUnitOfWorkContext(_database.Units, repository => repository, async token =>
        {
            await using var concurrent = await _database.Units.CreateAsync(useTransaction: true, cancellationToken: token);
            var users = concurrent.Repo<IAdminUserRepository>();
            var account = (await users.ReadAdminAsync("maestra", token))!;
            for (var attempt = 0; attempt < AdminUser.MaxFailedAttempts; attempt++)
                account.RegisterFailedAttempt(TestTimeProvider.Instant);
            Assert.NotNull(await users.TryUpdateAsync(account, token));
            await concurrent.CommitAsync(token);
        });
        await using var services = BuildServices(units);

        // Act
        var rejection = await RejectionAsync(services, "correct");

        // Assert
        Assert.Equal(CodiErrorCodes.AccountLocked, rejection.Code);
        await AssertNoCredentialsAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Login_ConConflittoPersistente_TerminaConErroreSenzaEmettereCredenziali()
    {
        // Arrange
        var units = new DecoratingUnitOfWorkContext(_database.Units,
            repository => repository is IAdminUserRepository users ? new AlwaysConflict(users) : repository);
        await using var services = BuildServices(units);

        // Act: il watchdog rileva un eventuale ciclo senza limite, non attende una durata di dominio.
        var rejection = await RejectionAsync(services, "correct").WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        Assert.Equal(CodiErrorCodes.RowVersionMismatch, rejection.Code);
        await AssertNoCredentialsAsync();
    }

    private ServiceProvider BuildServices(IUnitOfWorkContext units)
    {
        var services = new ServiceCollection();
        services.AddCodiRequests();
        services.AddSingleton(units);
        services.AddSingleton<IAdminPasswordHasher, TestPasswordHasher>();
        services.AddSingleton<TimeProvider>(new TestTimeProvider());
        // La composizione deve coprire l'intero catalogo: i comandi di regia dipendono dal segnale
        // dell'outbox e dal registro delle connessioni, che in produzione arrivano dall'infrastruttura
        // realtime.
        services.AddCodiRealtimeInfrastructure();
        // Gli handler della chat dipendono dall'astrazione dell'avviso: anche qui il trasporto è
        // sostituito dal doppio di test, altrimenti la validazione della composizione fallisce.
        services.AddSingleton<IParticipantMessageNotifier, RecordingParticipantMessageNotifier>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private async Task AssertNoCredentialsAsync()
    {
        await using var read = await _database.Units.CreateAsync();
        Assert.Empty(await read.Repo<ICredentialSessionRepository>().GetAllAsync());
    }

    private static async Task<CodiRequestException> RejectionAsync(ServiceProvider services, string password)
    {
        using var scope = services.CreateScope();
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(new { username = "Maestra", password }));
        return await Assert.ThrowsAsync<CodiRequestException>(() => scope.ServiceProvider
            .GetRequiredService<CodiRequestExecutor>().ExecuteAsync(
                CodiRequestKind.Command, "LoginAdmin", CodiRequestChannel.Http,
                payload.RootElement, CodiRequestContext.Anonymous, CancellationToken.None).AsTask());
    }

    private sealed class TestPasswordHasher : IAdminPasswordHasher
    {
        public string Hash(string password) => password;
        public bool Verify(string passwordHash, string password) => passwordHash == password;
    }

    private abstract class AdminDecorator(IAdminUserRepository inner)
        : RepositoryTestDecorator<AdminUserEntity, string>(inner), IAdminUserRepository
    {
        protected IAdminUserRepository Inner { get; } = inner;
        public Task<bool> TryInsertAsync(AdminUser user, CancellationToken token) => Inner.TryInsertAsync(user, token);
        public virtual Task<AdminUser?> TryUpdateAsync(AdminUser user, CancellationToken token) => Inner.TryUpdateAsync(user, token);
    }

    private sealed class ReadBarrier(int readers)
    {
        private int _count;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task WaitAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref _count) == readers) _ready.SetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        }
    }

    private sealed class SynchronizedReads(IAdminUserRepository inner, ReadBarrier barrier) : AdminDecorator(inner)
    {
        public override async Task<IReadOnlyList<AdminUserEntity>> QueryAsync(
            Expression<Func<AdminUserEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var rows = await Inner.QueryAsync(predicate, cancellationToken);
            await barrier.WaitAsync(cancellationToken);
            return rows;
        }
    }

    private sealed class AlwaysConflict(IAdminUserRepository inner) : AdminDecorator(inner)
    {
        public override Task<AdminUser?> TryUpdateAsync(AdminUser user, CancellationToken token) =>
            Task.FromResult<AdminUser?>(null);
    }
}
