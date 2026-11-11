using System.Data.Common;
using Qwerty.Codi.Infrastructure.Migrations;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Migrations;

/// <summary>
/// Applicazione e ispezione delle migrazioni su un database temporaneo (§8.3, §20.3): ordine,
/// riapplicazione senza effetti, atomicità dello script, impronta divergente, versione
/// sconosciuta, cataloghi con buchi e annullamento prima della prima scrittura.
/// </summary>
public sealed class MigrationRunnerTests : IAsyncLifetime
{
    private const string ProbeASql = "CREATE TABLE probe_a (id TEXT NOT NULL PRIMARY KEY);";

    private const string ProbeBSql = "CREATE TABLE probe_b (id TEXT NOT NULL PRIMARY KEY);";

    private TempSqliteDatabase _database = null!;

    private MigrationRunner _runner = null!;

    public Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();
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
    public async Task ApplyAsync_ConDatabaseVuoto_ApplicaTutteLeMigrazioniERegistraLaVersione()
    {
        // Arrange
        var catalog = EmbeddedCatalog();

        // Act
        var status = await _runner.ApplyAsync(catalog, CancellationToken.None);

        // Assert
        Assert.Equal(catalog.LatestVersion, status.CurrentVersion);
        Assert.Equal(catalog.LatestVersion, status.ExpectedVersion);
        Assert.Empty(status.PendingVersions);
        Assert.Empty(status.Divergences);
        Assert.True(status.IsUpToDate);

        var tables = await ReadTableNamesAsync();
        Assert.Contains("schema_migration", tables);
        Assert.Contains("admin_user", tables);
        Assert.Contains("credential_session", tables);
        Assert.Contains("school", tables);

        // Tutte le migrazioni distribuite sono registrate, ciascuna con la propria impronta.
        var applied = await ReadAppliedAsync();
        Assert.Equal(catalog.Scripts.Count, applied.Count);

        var firstApplied = applied[0];
        Assert.Equal(1, firstApplied.Version);
        Assert.Equal("schema", firstApplied.Name);
        Assert.Equal(catalog.Find(1)!.Checksum, firstApplied.Checksum);
        Assert.Equal(TestTimeProvider.Instant.ToUnixTimeMilliseconds(), firstApplied.AppliedAtUtcMs);
        Assert.False(string.IsNullOrWhiteSpace(firstApplied.ApplicationVersion));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ApplyAsync_ConMigrazioniGiaApplicate_NonRiesegueGliScript()
    {
        // Arrange
        var catalog = EmbeddedCatalog();
        await _runner.ApplyAsync(catalog, CancellationToken.None);

        // Act
        // Se il runner rieseguisse la migrazione, il CREATE TABLE fallirebbe: l'assenza di
        // eccezioni è già una prova, il conteggio delle righe la rende esplicita.
        var status = await _runner.ApplyAsync(catalog, CancellationToken.None);

        // Assert
        Assert.True(status.IsUpToDate);
        Assert.Equal(catalog.LatestVersion, status.CurrentVersion);
        Assert.Equal(catalog.Scripts.Count, (await ReadAppliedAsync()).Count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ApplyAsync_ConScriptAggiuntivo_ApplicaSoloLeNuove()
    {
        // Arrange
        var first = new MigrationCatalog([MigrationScript.Create(1, "probe_a", ProbeASql)]);
        var second = new MigrationCatalog(
        [
            MigrationScript.Create(1, "probe_a", ProbeASql),
            MigrationScript.Create(2, "probe_b", ProbeBSql),
        ]);

        await _runner.ApplyAsync(first, CancellationToken.None);

        // Act
        var status = await _runner.ApplyAsync(second, CancellationToken.None);

        // Assert
        Assert.Equal(2, status.CurrentVersion);
        Assert.True(status.IsUpToDate);

        var tables = await ReadTableNamesAsync();
        Assert.Contains("probe_a", tables);
        Assert.Contains("probe_b", tables);

        var applied = await ReadAppliedAsync();
        Assert.Equal(2, applied.Count);
        Assert.Equal(new[] { 1, 2 }, applied.Select(row => row.Version));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ApplyAsync_ConImprontaDivergente_RifiutaSenzaModificareIlDatabase()
    {
        // Arrange
        var original = new MigrationCatalog([MigrationScript.Create(1, "probe_a", ProbeASql)]);
        var modified = new MigrationCatalog(
            [MigrationScript.Create(1, "probe_a", $"{ProbeASql}\nALTER TABLE probe_a ADD COLUMN extra TEXT NULL;")]);

        await _runner.ApplyAsync(original, CancellationToken.None);

        // Act
        var exception = await Assert.ThrowsAsync<SchemaMigrationException>(() =>
            _runner.ApplyAsync(modified, CancellationToken.None));

        // Assert
        Assert.Contains("impronta", exception.Message, StringComparison.Ordinal);

        var status = await _runner.InspectAsync(original, CancellationToken.None);
        Assert.True(status.IsUpToDate, "Una divergenza rilevata non deve modificare il database.");

        var diverged = await _runner.InspectAsync(modified, CancellationToken.None);
        Assert.False(diverged.IsUpToDate);
        Assert.NotEmpty(diverged.Divergences);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ApplyAsync_ConVersionePiuNuovaDelDatabase_RifiutaSenzaApplicare()
    {
        // Arrange
        var current = new MigrationCatalog(
        [
            MigrationScript.Create(1, "probe_a", ProbeASql),
            MigrationScript.Create(2, "probe_b", ProbeBSql),
        ]);
        var older = new MigrationCatalog([MigrationScript.Create(1, "probe_a", ProbeASql)]);

        await _runner.ApplyAsync(current, CancellationToken.None);

        // Act
        var exception = await Assert.ThrowsAsync<SchemaMigrationException>(() =>
            _runner.ApplyAsync(older, CancellationToken.None));

        // Assert
        Assert.Contains("sconosciuta", exception.Message, StringComparison.Ordinal);

        var status = await _runner.InspectAsync(older, CancellationToken.None);
        Assert.False(status.IsUpToDate);
        Assert.Equal(2, status.CurrentVersion);
        Assert.Equal(1, status.ExpectedVersion);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ApplyAsync_ConScriptInterrotto_AnnullaLaMigrazioneFallita()
    {
        // Arrange
        var broken = new MigrationCatalog(
        [
            MigrationScript.Create(1, "probe_ok", "CREATE TABLE probe_ok (id TEXT NOT NULL PRIMARY KEY);"),
            MigrationScript.Create(
                2,
                "probe_broken",
                "CREATE TABLE probe_partial (id TEXT NOT NULL PRIMARY KEY);\nINSERT INTO tabella_inesistente (id) VALUES ('x');"),
        ]);

        // Act
        var exception = await Assert.ThrowsAsync<SchemaMigrationException>(() =>
            _runner.ApplyAsync(broken, CancellationToken.None));

        // Assert
        Assert.NotNull(exception.InnerException);

        var tables = await ReadTableNamesAsync();
        Assert.Contains("probe_ok", tables);
        Assert.DoesNotContain("probe_partial", tables);

        var applied = Assert.Single(await ReadAppliedAsync());
        Assert.Equal(1, applied.Version);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ApplyAsync_ConRichiestaAnnullata_NonLasciaSchema()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _runner.ApplyAsync(EmbeddedCatalog(), cancellation.Token));

        // Assert
        var tables = await ReadTableNamesAsync();
        Assert.DoesNotContain("admin_user", tables);
        Assert.DoesNotContain("credential_session", tables);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InspectAsync_SuDatabaseNonMigrato_RiportaTutteLeVersioniInAttesa()
    {
        // Act
        var status = await _runner.InspectAsync(EmbeddedCatalog(), CancellationToken.None);

        // Assert
        Assert.Equal(0, status.CurrentVersion);
        Assert.Equal(EmbeddedCatalog().LatestVersion, status.ExpectedVersion);
        Assert.Equal(EmbeddedCatalog().Scripts.Select(script => script.Version), status.PendingVersions);
        Assert.Empty(status.Divergences);
        Assert.False(status.IsUpToDate);
    }

    private static MigrationCatalog EmbeddedCatalog() =>
        MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly);

    private async Task<IReadOnlyList<AppliedMigrationRow>> ReadAppliedAsync()
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT version, name, checksum, applied_at_utc_ms, application_version
            FROM schema_migration
            ORDER BY version;
            """;

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        var rows = new List<AppliedMigrationRow>();
        while (await reader.ReadAsync(CancellationToken.None))
        {
            rows.Add(new AppliedMigrationRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.GetString(4)));
        }

        return rows;
    }

    private async Task<IReadOnlyList<string>> ReadTableNamesAsync()
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        var names = new List<string>();
        while (await reader.ReadAsync(CancellationToken.None))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    /// <summary>Riga di <c>schema_migration</c> usata solo dalle asserzioni di questi test.</summary>
    private sealed record AppliedMigrationRow(
        int Version,
        string Name,
        string Checksum,
        long AppliedAtUtcMs,
        string ApplicationVersion);
}
