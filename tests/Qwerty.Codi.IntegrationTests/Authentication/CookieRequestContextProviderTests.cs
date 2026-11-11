using Qwerty.Codi.Application.Abstractions;
using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Api.Authentication;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Domain.Admin;
using Qwerty.Codi.Domain.Credentials;
using Qwerty.Codi.Infrastructure.Migrations;
using Qwerty.Codi.Infrastructure.Persistence;
using Qwerty.Codi.Infrastructure.Realtime;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Authentication;

/// <summary>
/// Contesto di richiesta dal cookie amministrativo (§9.1, §9.2). Il punto dei test è che il
/// cookie da solo non basta: la credenziale viene riletta e deve risultare ancora utilizzabile,
/// quindi una revoca toglie il permesso a un ticket che resta formalmente valido.
/// </summary>
public sealed class CookieRequestContextProviderTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private TempSqliteDatabase _database = null!;

    private ICredentialSessionRepository _sessions = null!;
    private IUnitOfWork _work = null!;

    private CookieRequestContextProvider _provider = null!;

    public async Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();

        var runner = new MigrationRunner(_database.Factory, new TestTimeProvider());
        await runner.ApplyAsync(
            MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly),
            CancellationToken.None);

        _work = await _database.Units.CreateAsync();
        _sessions = _work.Repo<ICredentialSessionRepository>();
        _provider = new CookieRequestContextProvider(
            _database.Units,
            TestConnections(),
            new TestTimeProvider(),
            new EphemeralDataProtectionProvider());
    }

    public async Task DisposeAsync()
    {
        await _work.DisposeAsync();
        _database.Dispose();
    }

    /// <summary>
    /// Registro delle connessioni costruito come lo costruisce l'applicazione: il test non deve
    /// dipendere da un'implementazione scritta apposta, altrimenti verificherebbe il doppio.
    /// </summary>
    private static IParticipantConnectionRegistry TestConnections()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new TestTimeProvider());
        services.AddCodiRealtimeInfrastructure();

        return services.BuildServiceProvider().GetRequiredService<IParticipantConnectionRegistry>();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ResolveRequestContextAsync_SenzaCookie_RestituisceIlContestoAnonimo()
    {
        var context = await ResolveAsync(new DefaultHttpContext(), sessionId: null, stamp: null);

        Assert.False(context.IsAuthenticated);
        Assert.True(context.Allows(CodiPolicy.AnonymousBootstrap));
        Assert.False(context.Allows(CodiPolicy.Admin));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ResolveRequestContextAsync_ConCredenzialeValida_ConcedeLaPolicyAmministrativa()
    {
        var session = await IssueSessionAsync();

        var context = await ResolveAsync(new DefaultHttpContext(), session.Id, session.SecurityStamp);

        Assert.True(context.IsAuthenticated);
        Assert.True(context.Allows(CodiPolicy.Admin));
        Assert.Equal(session.Id, context.CredentialSessionId);
        Assert.Equal(session.AdminUserId, context.AdminUserId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ResolveRequestContextAsync_ConCredenzialeRevocata_NonConcedeNiente()
    {
        // Arrange: la revoca vale anche per una connessione già aperta (§9.2), quindi non deve
        // bastare che il cookie sia stato emesso regolarmente.
        var session = await IssueSessionAsync();
        session.Revoke(Now.AddMinutes(10));
        await _sessions.TryRevokeAsync(session, CancellationToken.None);

        // Act
        var context = await ResolveAsync(new DefaultHttpContext(), session.Id, session.SecurityStamp);

        // Assert
        Assert.False(context.IsAuthenticated);
        Assert.False(context.Allows(CodiPolicy.Admin));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ResolveRequestContextAsync_ConMarcaDiversa_NonConcedeNiente()
    {
        var session = await IssueSessionAsync();

        var context = await ResolveAsync(new DefaultHttpContext(), session.Id, "marca-sostituita");

        Assert.False(context.IsAuthenticated);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ResolveRequestContextAsync_ConIdentificativoNonValido_NonConcedeNiente()
    {
        var context = await ResolveAsync(new DefaultHttpContext(), sessionId: null, stamp: "marca");

        Assert.False(context.IsAuthenticated);
    }

    private async Task<CredentialSession> IssueSessionAsync()
    {
        var users = _work.Repo<IAdminUserRepository>();
        var admin = AdminUser.Create("Maestra", "impronta-di-prova", Now);
        Assert.True(await users.TryInsertAsync(admin, CancellationToken.None));

        var session = CredentialSession.IssueForAdmin(admin.Id, Now, TimeSpan.FromHours(8));
        await _sessions.StoreAsync(session, CancellationToken.None);

        return session;
    }

    private Task<CodiRequestContext> ResolveAsync(
        DefaultHttpContext httpContext,
        Guid? sessionId,
        string? stamp)
    {
        httpContext.User = CreatePrincipal(sessionId, stamp);

        return _provider.ResolveRequestContextAsync(httpContext, CancellationToken.None);
    }

    private static ClaimsPrincipal CreatePrincipal(Guid? sessionId, string? stamp)
    {
        var claims = new List<Claim>();

        if (sessionId is { } id)
        {
            claims.Add(new Claim(CodiClaimTypes.CredentialSessionId, id.ToString("D")));
        }

        if (!string.IsNullOrEmpty(stamp))
        {
            claims.Add(new Claim(CodiClaimTypes.CredentialStamp, stamp));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, AdminCookieAuthentication.SchemeName));
    }
}
