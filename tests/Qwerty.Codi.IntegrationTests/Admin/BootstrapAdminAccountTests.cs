using Qwerty.Codi.Application.Dto;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Api.Authentication;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Admin;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Domain.Admin;
using Qwerty.Codi.Infrastructure.Migrations;
using Qwerty.Codi.Infrastructure.Persistence;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Admin;

/// <summary>
/// Bootstrap del primo account amministrativo (§9.1) attraverso il percorso reale: registro,
/// canale locale, mediator generato in <c>Application</c>, handler e database migrato. È la prima
/// richiesta vera che attraversa il dispatcher, e sostituisce la copertura che il prototipo di T0
/// dava con richieste di prova.
/// </summary>
public sealed class BootstrapAdminAccountTests : IAsyncLifetime
{
    private const string Password = "password-del-docente";

    private TempSqliteDatabase _database = null!;

    private ServiceProvider _services = null!;

    private IAdminPasswordHasher _passwordHasher = null!;

    public async Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();

        var runner = new MigrationRunner(_database.Factory, new TestTimeProvider());
        await runner.ApplyAsync(
            MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly),
            CancellationToken.None);

        var services = new ServiceCollection();

        // La composizione applicativa reale: mediator, registro, esecutore.
        services.AddCodiRequests();

        services.AddSingleton(_database.Factory);
        services.AddCodiPersistence();
        services.AddSingleton<IAdminPasswordHasher>(new AspNetCorePasswordHasher());
        services.AddSingleton(TimeProvider.System);

        _services = services.BuildServiceProvider();
        _passwordHasher = _services.GetRequiredService<IAdminPasswordHasher>();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        _database.Dispose();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConInstallazioneVuota_CreaIlPrimoAccountConImprontaNonInChiaro()
    {
        // Arrange
        var executor = CreateExecutor();
        using var payload = Payload("Maestra", Password);

        // Act
        var result = await executor.ExecuteAsync(
            CodiRequestKind.Command,
            AnonymousBootstrapRequests.BootstrapAdminAccount,
            CodiRequestChannel.Local,
            payload.RootElement,
            CodiRequestContext.Anonymous,
            CancellationToken.None);

        // Assert
        var created = Assert.IsType<AdminAccountCreatedDto>(result);
        Assert.Equal("Maestra", created.Username);
        Assert.NotEqual(Guid.Empty, created.AdminUserId);

        var stored = await ReadStoredUserAsync("maestra");
        Assert.NotNull(stored);
        Assert.Equal(created.AdminUserId, stored.Id);
        Assert.True(stored.IsActive);
        Assert.NotEqual(Password, stored.PasswordHash);
        Assert.True(
            _passwordHasher.Verify(stored.PasswordHash, Password),
            "L'impronta memorizzata deve verificare la password scelta al bootstrap.");
        Assert.False(_passwordHasher.Verify(stored.PasswordHash, "password-sbagliata"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConAmministratoreGiaAttivo_RifiutaConIlCodiceDelBootstrap()
    {
        // Arrange
        var executor = CreateExecutor();
        using var first = Payload("Maestra", Password);
        await executor.ExecuteAsync(
            CodiRequestKind.Command,
            AnonymousBootstrapRequests.BootstrapAdminAccount,
            CodiRequestChannel.Local,
            first.RootElement,
            CodiRequestContext.Anonymous,
            CancellationToken.None);

        using var second = Payload("Seconda", Password);

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                AnonymousBootstrapRequests.BootstrapAdminAccount,
                CodiRequestChannel.Local,
                second.RootElement,
                CodiRequestContext.Anonymous,
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.BootstrapNotAllowed, exception.Code);
        Assert.Null(await ReadStoredUserAsync("seconda"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SulCanaleHttp_NonEsiste()
    {
        // Arrange: il bootstrap amministrativo non è raggiungibile dall'API pubblica (§10.4).
        var executor = CreateExecutor();
        using var payload = Payload("Maestra", Password);

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                AnonymousBootstrapRequests.BootstrapAdminAccount,
                CodiRequestChannel.Http,
                payload.RootElement,
                CodiRequestContext.Anonymous,
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.CommandNotFound, exception.Code);
        Assert.Null(await ReadStoredUserAsync("maestra"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConNomeNonUtilizzabile_RifiutaConIlCodiceDelNome()
    {
        // Arrange
        var executor = CreateExecutor();
        using var payload = Payload("   ", Password);

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                AnonymousBootstrapRequests.BootstrapAdminAccount,
                CodiRequestChannel.Local,
                payload.RootElement,
                CodiRequestContext.Anonymous,
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.InvalidName, exception.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConPasswordVuota_RifiutaConIlCodiceDiValidazione()
    {
        // Arrange
        var executor = CreateExecutor();
        using var payload = Payload("Maestra", "   ");

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                AnonymousBootstrapRequests.BootstrapAdminAccount,
                CodiRequestChannel.Local,
                payload.RootElement,
                CodiRequestContext.Anonymous,
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, exception.Code);
        Assert.Null(await ReadStoredUserAsync("maestra"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConNomeOccupatoDaUnAccountNonAttivo_RifiutaSenzaSovrascrivere()
    {
        // Arrange: un account disattivato con lo stesso nome non blocca la prima verifica, ma
        // occupa comunque il nome normalizzato: il bootstrap non deve rubargli l'identità.
        await InsertInactiveAccountAsync("Maestra", "maestra");

        var executor = CreateExecutor();
        using var payload = Payload("  MAESTRÀ  ", Password);

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                AnonymousBootstrapRequests.BootstrapAdminAccount,
                CodiRequestChannel.Local,
                payload.RootElement,
                CodiRequestContext.Anonymous,
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.AdminAccountAlreadyExists, exception.Code);

        var stored = await ReadStoredUserAsync("maestra");
        Assert.NotNull(stored);
        Assert.False(stored.IsActive);
    }

    private CodiRequestExecutor CreateExecutor()
    {
        var scope = _services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<CodiRequestExecutor>();
    }

    private static JsonDocument Payload(string username, string password) =>
        JsonDocument.Parse(JsonSerializer.Serialize(new { username, password }, CodiJson.Payload));

    private async Task<AdminUser?> ReadStoredUserAsync(string normalizedUsername)
    {
        await using var work = await _database.Units.CreateAsync();
        var repository = work.Repo<IAdminUserRepository>();
        return await repository.ReadAdminAsync(normalizedUsername, CancellationToken.None);
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

        AddParameter(command, "$id", Guid.NewGuid().ToString("D"));
        AddParameter(command, "$username", username);
        AddParameter(command, "$normalizedUsername", normalizedUsername);
        AddParameter(command, "$passwordHash", _passwordHasher.Hash(Password));
        AddParameter(command, "$securityStamp", Guid.NewGuid().ToString("N"));
        AddParameter(command, "$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
