using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Organization;
using Qwerty.Codi.Infrastructure.Migrations;
using Qwerty.Codi.Infrastructure.Persistence;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Organization;

/// <summary>
/// Repository delle scuole su un database temporaneo migrato (§7.2, §20.3): andata e ritorno
/// dell'entità, unicità del nome normalizzato e controllo di versione sull'aggiornamento.
/// </summary>
public sealed class SchoolRepositoryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private TempSqliteDatabase _database = null!;

    private ISchoolRepository _repository = null!;

    private IUnitOfWork _work = null!;

    public async Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();

        var runner = new MigrationRunner(_database.Factory, new TestTimeProvider());
        await runner.ApplyAsync(
            MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly),
            CancellationToken.None);

        _work = await _database.Units.CreateAsync();
        _repository = _work.Repo<ISchoolRepository>();
    }

    public async Task DisposeAsync()
    {
        await _work.DisposeAsync();
        _database.Dispose();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TryInsertAsync_ConScuolaNuova_SiRileggeUguale()
    {
        // Arrange
        var school = School.Create("Scuola   Manzòni", Now);

        // Act
        var inserted = await _repository.TryInsertAsync(school, CancellationToken.None);

        // Assert
        Assert.True(inserted);

        var read = await ReadByNameAsync("scuola manzoni");

        Assert.NotNull(read);
        Assert.Equal(school.Id, read.Id);
        Assert.Equal("Scuola   Manzòni", read.Name);
        Assert.Equal("scuola manzoni", read.NormalizedName);
        Assert.False(read.IsArchived);
        Assert.Equal(Now, read.CreatedAtUtc);
        Assert.Equal(Now, read.UpdatedAtUtc);
        Assert.Equal(1, read.RowVersion);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TryInsertAsync_ConNomeNormalizzatoGiaInUso_RifiutaSenzaDuplicare()
    {
        // Arrange
        var first = School.Create("Scuola Manzoni", Now);
        Assert.True(await _repository.TryInsertAsync(first, CancellationToken.None));

        // Act: stessa normalizzazione, scrittura diversa. Il vincolo unico è sul nome normalizzato,
        // e vale anche per una scuola archiviata, che non è stata eliminata (§6.1).
        var second = School.Create("  SCUOLA MANZÒNI  ", Now);
        var inserted = await _repository.TryInsertAsync(second, CancellationToken.None);

        // Assert
        Assert.False(inserted);

        var found = await ReadByNameAsync("scuola manzoni");
        Assert.Equal(first.Id, found!.Id);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task QueryAsync_ConNomeAssente_RestituisceElencoVuoto()
    {
        var rows = await _repository.QueryAsync(
            entity => entity.NormalizedName == "scuola sconosciuta", CancellationToken.None);

        Assert.Empty(rows);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TryUpdateAsync_ConNomeNuovo_IncrementaLaVersioneEMantieneLaCreazione()
    {
        // Arrange
        var school = School.Create("Scuola Manzoni", Now);
        Assert.True(await _repository.TryInsertAsync(school, CancellationToken.None));

        // Act
        Assert.True(school.Rename("Scuola Manzoni Due", Now.AddMinutes(10)));
        var updated = await _repository.TryUpdateAsync(school, CancellationToken.None);

        // Assert
        Assert.NotNull(updated);
        Assert.Equal(2, updated.RowVersion);

        var reloaded = await ReadByNameAsync("scuola manzoni due");
        Assert.NotNull(reloaded);
        Assert.Equal(school.Id, reloaded.Id);
        Assert.Equal(Now, reloaded.CreatedAtUtc);
        Assert.Equal(Now.AddMinutes(10), reloaded.UpdatedAtUtc);
        Assert.Null(await ReadByNameAsync("scuola manzoni"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TryUpdateAsync_ConVersioneObsoleta_RestituisceNullSenzaSovrascrivere()
    {
        // Arrange: due copie della stessa riga, come due richieste concorrenti.
        var school = School.Create("Scuola Manzoni", Now);
        Assert.True(await _repository.TryInsertAsync(school, CancellationToken.None));

        var first = await ReadAsync(school.Id);
        var second = await ReadAsync(school.Id);

        first!.Archive(Now.AddMinutes(1));
        Assert.NotNull(await _repository.TryUpdateAsync(first, CancellationToken.None));

        // Act
        second!.Rename("Scuola Manzoni Due", Now.AddMinutes(2));
        var overwritten = await _repository.TryUpdateAsync(second, CancellationToken.None);

        // Assert
        Assert.Null(overwritten);

        var reloaded = await ReadAsync(school.Id);
        Assert.Equal("Scuola Manzoni", reloaded!.Name);
        Assert.True(reloaded.IsArchived);
        Assert.Equal(2, reloaded.RowVersion);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TryUpdateAsync_DopoUnArchiviazione_PersisteLoStato()
    {
        // Arrange
        var school = School.Create("Scuola Manzoni", Now);
        Assert.True(await _repository.TryInsertAsync(school, CancellationToken.None));

        // Act
        Assert.True(school.Archive(Now.AddMinutes(1)));
        var updated = await _repository.TryUpdateAsync(school, CancellationToken.None);

        // Assert
        Assert.NotNull(updated);

        var reloaded = await ReadAsync(school.Id);
        Assert.True(reloaded!.IsArchived);
        Assert.Equal(Now.AddMinutes(1), reloaded.UpdatedAtUtc);
    }

    private async Task<School?> ReadByNameAsync(string normalizedName)
    {
        var rows = await _repository.QueryAsync(
            entity => entity.NormalizedName == normalizedName, CancellationToken.None);

        return rows.SingleOrDefault()?.ToDomain();
    }

    private async Task<School?> ReadAsync(Guid id) =>
        (await _repository.GetAsync(id.ToString("D"), CancellationToken.None))?.ToDomain();
}
