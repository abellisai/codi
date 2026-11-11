using Qwerty.Codi.Infrastructure.Configuration;
using Qwerty.Codi.Infrastructure.Migrations;
using Qwerty.Codi.Infrastructure.Persistence;
using RepoDb;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Persistence;

/// <summary>
/// Preparazione del database di §16.1 e risoluzione dei percorsi di §18.2: directory creata,
/// WAL effettivo e provider RepoDB utilizzabile dopo la preparazione.
/// </summary>
public sealed class DatabaseBootstrapTests : IAsyncLifetime
{
    private const int BusyTimeoutMs = 5000;

    private readonly string _basePath =
        Path.Combine(Path.GetTempPath(), $"codi-t1-{Guid.NewGuid():N}");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(_basePath))
        {
            // Il database è in una cartella dedicata: la si elimina con i file -wal e -shm.
            Directory.Delete(_basePath, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public void FromOptions_ConDirectoryRelativa_RisolveControLaBaseDelServizio()
    {
        var options = new CodiOptions
        {
            DataDirectory = "data",
            Database = new DatabaseOptions { FileName = "codi.db", BusyTimeoutMs = BusyTimeoutMs },
        };

        var database = SqliteDatabase.FromOptions(_basePath, options);

        Assert.Equal(Path.Combine(_basePath, "data", "codi.db"), database.FilePath);
        Assert.Equal(BusyTimeoutMs, database.BusyTimeoutMs);
    }

    [Fact]
    public void FromOptions_ConNomeFileAssente_RifiutaLaConfigurazione()
    {
        var options = new CodiOptions
        {
            DataDirectory = "data",
            Database = new DatabaseOptions { FileName = string.Empty, BusyTimeoutMs = BusyTimeoutMs },
        };

        Assert.Throws<ArgumentException>(() => SqliteDatabase.FromOptions(_basePath, options));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PrepareAsync_CreaIlDatabase_AttivaIlWalERendeUsabileRepoDb()
    {
        var database = BuildDatabase();
        var factory = new SqliteConnectionFactory(database);
        var bootstrap = CreateBootstrap(factory, database);

        var preparation = await bootstrap.PrepareAsync(CancellationToken.None);

        Assert.Equal("wal", preparation.JournalMode);

        // La versione attesa è quella del catalogo distribuito, non un numero scritto qui: la
        // preparazione deve aver applicato tutte le migrazioni disponibili, quante siano.
        Assert.Equal(
            MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly).LatestVersion,
            preparation.SchemaVersion);
        Assert.True(File.Exists(database.FilePath), "Il file di database deve esistere dopo la preparazione.");

        await using var connection = await factory.OpenAsync(CancellationToken.None);

        await using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE bootstrap_probe (id TEXT NOT NULL PRIMARY KEY);";
            await create.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO bootstrap_probe (id) VALUES ('probe');";
            await insert.ExecuteNonQueryAsync(CancellationToken.None);
        }

        // La lettura tipata passa da RepoDB: senza provider inizializzato fallirebbe.
        var rows = await connection.ExecuteQueryAsync<BootstrapProbe>(
            "SELECT id AS Id FROM bootstrap_probe;");

        Assert.Equal("probe", Assert.Single(rows).Id);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PrepareAsync_EseguitaDueVolte_NonCambiaIlModoDiJournalNeLaVersioneDiSchema()
    {
        var database = BuildDatabase();
        var bootstrap = CreateBootstrap(new SqliteConnectionFactory(database), database);

        var first = await bootstrap.PrepareAsync(CancellationToken.None);
        var second = await bootstrap.PrepareAsync(CancellationToken.None);

        Assert.Equal("wal", first.JournalMode);
        Assert.Equal("wal", second.JournalMode);
        Assert.Equal(first.SchemaVersion, second.SchemaVersion);
    }

    private static DatabaseBootstrap CreateBootstrap(SqliteConnectionFactory factory, SqliteDatabase database) =>
        new(
            factory,
            database,
            MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly),
            new MigrationRunner(factory, new TestTimeProvider()));

    private SqliteDatabase BuildDatabase() => SqliteDatabase.FromOptions(
        _basePath,
        new CodiOptions
        {
            DataDirectory = "data",
            Database = new DatabaseOptions { FileName = "codi.db", BusyTimeoutMs = BusyTimeoutMs },
        });

    /// <summary>Riga di prova per verificare che il provider RepoDB sia utilizzabile.</summary>
    public sealed class BootstrapProbe
    {
        public string Id { get; set; } = string.Empty;
    }
}
