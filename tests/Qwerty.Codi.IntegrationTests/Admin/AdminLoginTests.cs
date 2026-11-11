using Qwerty.Codi.Application.Dto;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Api.Authentication;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Admin;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Domain.Admin;
using Qwerty.Codi.Domain.Credentials;
using Qwerty.Codi.Infrastructure.Migrations;
using Qwerty.Codi.Infrastructure.Persistence;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Admin;

/// <summary>
/// Accesso e uscita amministrativi (§9.1) attraverso il percorso reale: registro, mediator
/// generato in <c>Application</c>, handler, repository e database migrato. Verifica le regole che
/// non si vedono dalla lettura del codice: il blocco temporaneo che vince sulla password giusta,
/// il conteggio azzerato da un accesso riuscito e la credenziale revocata che smette di valere.
/// </summary>
public sealed class AdminLoginTests : IAsyncLifetime
{
    private const string Username = "Maestra";

    private const string Password = "password-del-docente";

    private TempSqliteDatabase _database = null!;

    private ServiceProvider _services = null!;

    private IServiceScope _scope = null!;

    private CodiRequestExecutor _executor = null!;

    private IAdminPasswordHasher _passwordHasher = null!;
    private IUnitOfWork _work = null!;

    public async Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();

        var runner = new MigrationRunner(_database.Factory, new TestTimeProvider());
        await runner.ApplyAsync(
            MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly),
            CancellationToken.None);

        var services = new ServiceCollection();
        services.AddCodiRequests();
        services.AddSingleton(_database.Factory);
        services.AddCodiPersistence();
        services.AddSingleton<IAdminPasswordHasher>(new AspNetCorePasswordHasher());
        services.AddSingleton(TimeProvider.System);

        _services = services.BuildServiceProvider();
        _scope = _services.CreateScope();
        _executor = _scope.ServiceProvider.GetRequiredService<CodiRequestExecutor>();
        _passwordHasher = _scope.ServiceProvider.GetRequiredService<IAdminPasswordHasher>();
        _work = await _database.Units.CreateAsync();

        await CreateAdminAsync(Username);
    }

    public async Task DisposeAsync()
    {
        await _work.DisposeAsync();
        _scope.Dispose();
        await _services.DisposeAsync();
        _database.Dispose();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConCredenzialiCorrette_EmettteUnaCredenzialeUtilizzabile()
    {
        // Act
        var signedIn = await SignInAsync(Username, Password);

        // Assert
        Assert.Equal(Username, signedIn.Username);
        Assert.NotEqual(Guid.Empty, signedIn.AdminUserId);
        Assert.False(string.IsNullOrWhiteSpace(signedIn.SecurityStamp));

        var session = await Sessions().ReadCredentialAsync(signedIn.CredentialSessionId, CancellationToken.None);
        Assert.NotNull(session);
        Assert.Equal(CredentialPrincipalKind.Admin, session.PrincipalKind);
        Assert.Equal(signedIn.AdminUserId, session.AdminUserId);
        Assert.Equal(signedIn.SecurityStamp, session.SecurityStamp);
        Assert.True(session.IsUsableAt(DateTimeOffset.UtcNow, signedIn.SecurityStamp));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConNomeScrittoDiversamente_TrovaLAccount()
    {
        var signedIn = await SignInAsync("  MAESTRÀ  ", Password);

        Assert.Equal(Username, signedIn.Username);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConPasswordSbagliata_RifiutaERegistraIlTentativo()
    {
        var exception = await SignInFailureAsync(Username, "password-sbagliata");

        Assert.Equal(CodiErrorCodes.CredentialsRejected, exception.Code);

        var stored = await Users().ReadAdminAsync("maestra", CancellationToken.None);
        Assert.Equal(1, stored!.FailedLoginAttempts);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConNomeInesistente_RifiutaConLoStessoMessaggio()
    {
        var unknownName = await SignInFailureAsync("Nessuno", Password);
        var wrongPassword = await SignInFailureAsync(Username, "password-sbagliata");

        Assert.Equal(unknownName.Code, wrongPassword.Code);
        Assert.Equal(unknownName.Message, wrongPassword.Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_DopoLaSoglia_BloccaAncheLaPasswordGiusta()
    {
        // Arrange: quattro tentativi falliti non bloccano e restano rifiuti di credenziali.
        for (var attempt = 1; attempt < AdminUser.MaxFailedAttempts; attempt++)
        {
            var rejection = await SignInFailureAsync(Username, "password-sbagliata");
            Assert.Equal(CodiErrorCodes.CredentialsRejected, rejection.Code);
        }

        // Act: il quinto tentativo fa scattare il blocco.
        var locked = await SignInFailureAsync(Username, "password-sbagliata");

        // Assert
        Assert.Equal(CodiErrorCodes.AccountLocked, locked.Code);

        // La password giusta non aggira il blocco: è il punto della regola.
        var correctPassword = await SignInFailureAsync(Username, Password);
        Assert.Equal(CodiErrorCodes.AccountLocked, correctPassword.Code);

        var stored = await Users().ReadAdminAsync("maestra", CancellationToken.None);
        Assert.True(stored!.IsLockedAt(DateTimeOffset.UtcNow));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConAccessoRiuscito_AzzeraITentativiFalliti()
    {
        await SignInFailureAsync(Username, "password-sbagliata");
        await SignInFailureAsync(Username, "password-sbagliata");
        await SignInFailureAsync(Username, "password-sbagliata");

        await SignInAsync(Username, Password);

        var stored = await Users().ReadAdminAsync("maestra", CancellationToken.None);
        Assert.Equal(0, stored!.FailedLoginAttempts);
        Assert.Null(stored.LockedUntilUtc);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConAccountDisattivato_RifiutaAncheConLaPasswordGiusta()
    {
        // Arrange: un account disattivato non deve poter accedere, e la risposta non deve
        // distinguerlo da credenziali sbagliate.
        await InsertInactiveAccountAsync("Assistente", "assistente");

        var exception = await SignInFailureAsync("Assistente", Password);

        Assert.Equal(CodiErrorCodes.CredentialsRejected, exception.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LogoutAdmin_ConContestoAmministrativo_RevocaLaCredenziale()
    {
        // Arrange
        var signedIn = await SignInAsync(Username, Password);
        using var payload = JsonDocument.Parse(
            JsonSerializer.Serialize(new { credentialSessionId = signedIn.CredentialSessionId }, CodiJson.Payload));

        // Act
        var revoked = await _executor.ExecuteAsync(
            CodiRequestKind.Command,
            "LogoutAdmin",
            CodiRequestChannel.Http,
            payload.RootElement,
            new CodiRequestContext(
                isAuthenticated: true, [CodiPolicy.Admin], signedIn.CredentialSessionId, signedIn.AdminUserId),
            CancellationToken.None);

        // Assert
        Assert.Equal(true, revoked);

        var session = await Sessions().ReadCredentialAsync(signedIn.CredentialSessionId, CancellationToken.None);
        Assert.NotNull(session!.RevokedAtUtc);
        Assert.False(session.IsUsableAt(DateTimeOffset.UtcNow, signedIn.SecurityStamp));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LogoutAdmin_SenzaContestoAmministrativo_RifiutaPerAutenticazione()
    {
        // Arrange
        var signedIn = await SignInAsync(Username, Password);
        using var payload = JsonDocument.Parse(
            JsonSerializer.Serialize(new { credentialSessionId = signedIn.CredentialSessionId }, CodiJson.Payload));

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => _executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                "LogoutAdmin",
                CodiRequestChannel.Http,
                payload.RootElement,
                CodiRequestContext.Anonymous,
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.AuthenticationRequired, exception.Code);

        var session = await Sessions().ReadCredentialAsync(signedIn.CredentialSessionId, CancellationToken.None);
        Assert.Null(session!.RevokedAtUtc);
    }

    private async Task<AdminSignedInDto> SignInAsync(string username, string password)
    {
        using var payload = Payload(username, password);

        var result = await _executor.ExecuteAsync(
            CodiRequestKind.Command,
            AnonymousBootstrapRequests.LoginAdmin,
            CodiRequestChannel.Http,
            payload.RootElement,
            CodiRequestContext.Anonymous,
            CancellationToken.None);

        return Assert.IsType<AdminSignedInDto>(result);
    }

    private async Task<CodiRequestException> SignInFailureAsync(string username, string password)
    {
        using var payload = Payload(username, password);

        return await Assert.ThrowsAsync<CodiRequestException>(() => _executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                AnonymousBootstrapRequests.LoginAdmin,
                CodiRequestChannel.Http,
                payload.RootElement,
                CodiRequestContext.Anonymous,
                CancellationToken.None)
            .AsTask());
    }

    private async Task CreateAdminAsync(string username)
    {
        using var payload = Payload(username, Password);

        await _executor.ExecuteAsync(
            CodiRequestKind.Command,
            AnonymousBootstrapRequests.BootstrapAdminAccount,
            CodiRequestChannel.Local,
            payload.RootElement,
            CodiRequestContext.Anonymous,
            CancellationToken.None);
    }

    private async Task InsertInactiveAccountAsync(string username, string normalizedUsername)
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO admin_user (
                id, username, normalized_username, password_hash, security_stamp,
                is_active, failed_login_count, created_at_utc_ms, updated_at_utc_ms, row_version)
            VALUES ($id, $username, $normalizedUsername, $passwordHash, $securityStamp, 0, 0, $now, $now, 1);
            """;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        AddParameter(command, "$id", Guid.NewGuid().ToString("D"));
        AddParameter(command, "$username", username);
        AddParameter(command, "$normalizedUsername", normalizedUsername);
        AddParameter(command, "$passwordHash", _passwordHasher.Hash(Password));
        AddParameter(command, "$securityStamp", Guid.NewGuid().ToString("N"));
        AddParameter(command, "$now", now);

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private IAdminUserRepository Users() => _work.Repo<IAdminUserRepository>();

    private ICredentialSessionRepository Sessions() => _work.Repo<ICredentialSessionRepository>();

    private static JsonDocument Payload(string username, string password) =>
        JsonDocument.Parse(JsonSerializer.Serialize(new { username, password }, CodiJson.Payload));

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
