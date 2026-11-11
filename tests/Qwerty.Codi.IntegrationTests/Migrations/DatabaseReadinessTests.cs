using Qwerty.Codi.Infrastructure.Migrations;
using Qwerty.Codi.Infrastructure.Persistence;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Migrations;

/// <summary>
/// Prontezza del database (§8.3, §18.4): richiede journal WAL effettivo e schema allineato agli
/// script distribuiti, e distingue una migrazione ancora da applicare da una divergenza.
/// La verifica usa un file temporaneo dedicato (§20.3).
/// </summary>
public sealed class DatabaseReadinessTests : IAsyncLifetime
{
    private TempSqliteDatabase _database = null!;

    private MigrationCatalog _catalog = null!;

    private MigrationRunner _runner = null!;

    public Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();
        _catalog = MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly);
        _runner = new MigrationRunner(_database.Factory, new TestTimeProvider());
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _database.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CheckAsync_DopoLaPreparazioneDelBootstrap_RiportaPronto()
    {
        // Act
        var preparation = await PrepareAsync();
        var readiness = await Probe(_catalog).CheckAsync(CancellationToken.None);

        // Assert
        Assert.Equal("wal", preparation.JournalMode);
        Assert.Equal(preparation.SchemaVersion, readiness.Schema.CurrentVersion);
        Assert.True(readiness.UsesWriteAheadLogging);
        Assert.True(readiness.Schema.IsUpToDate);
        Assert.True(readiness.IsReady);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CheckAsync_ConUnaMigrazioneInAttesa_NonRiportaPronto()
    {
        // Arrange
        await PrepareAsync();
        var extended = new MigrationCatalog(
        [
            .. _catalog.Scripts,
            MigrationScript.Create(
                _catalog.LatestVersion + 1,
                "probe_pending",
                "CREATE TABLE probe_pending (id TEXT NOT NULL PRIMARY KEY);"),
        ]);

        // Act
        var readiness = await Probe(extended).CheckAsync(CancellationToken.None);

        // Assert
        Assert.False(readiness.IsReady);
        Assert.Empty(readiness.Schema.Divergences);
        Assert.Equal(new[] { _catalog.LatestVersion + 1 }, readiness.Schema.PendingVersions);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CheckAsync_ConImprontaDivergente_NonRiportaPronto()
    {
        // Arrange
        await PrepareAsync();
        var diverged = new MigrationCatalog([MigrationScript.Create(1, "admin_identity", "SELECT 1;")]);

        // Act
        var readiness = await Probe(diverged).CheckAsync(CancellationToken.None);

        // Assert
        Assert.False(readiness.IsReady);
        Assert.NotEmpty(readiness.Schema.Divergences);
    }

    private Task<DatabasePreparation> PrepareAsync() =>
        new DatabaseBootstrap(_database.Factory, _database.Database, _catalog, _runner)
            .PrepareAsync(CancellationToken.None);

    private DatabaseReadinessProbe Probe(MigrationCatalog catalog) =>
        new(_database.Factory, catalog, _runner);
}
