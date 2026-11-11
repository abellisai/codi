using System.Data.Common;
using Microsoft.Data.Sqlite;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Infrastructure.Migrations;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Persistence;

/// <summary>
/// Persistenza dell'autoraggio degli esercizi (§7.3) su un database temporaneo migrato (§20.3):
/// round trip di bozza e versione, controllo di <c>row_version</c> della bozza, unicità del numero
/// di versione, immutabilità della versione pubblicata, soluzione privata facoltativa e tenuta in
/// una tabella separata.
/// </summary>
public sealed class ExerciseAuthoringRepositoryTests : IAsyncLifetime
{
    /// <summary>Codice SQLITE_CONSTRAINT: il vincolo è stato violato, non è mancata una tabella.</summary>
    private const int ConstraintViolationCode = 19;

    /// <summary>Codice esteso SQLITE_CONSTRAINT_UNIQUE: la seconda riga collide sull'indice.</summary>
    private const int UniqueViolationCode = 2067;

    private const string ExerciseId = "a0000000-0000-0000-0000-000000000001";
    private const string SecondExerciseId = "a0000000-0000-0000-0000-000000000002";
    private const string AdminId = "b0000000-0000-0000-0000-000000000001";

    private const string VersionOneId = "c0000000-0000-0000-0000-000000000001";
    private const string DuplicateVersionId = "c0000000-0000-0000-0000-000000000002";
    private const string OtherExerciseVersionId = "c0000000-0000-0000-0000-000000000003";

    private const string DefinitionJson = "{\"version\":1}";
    private const string OtherDefinitionJson = "{\"tampered\":true}";

    private const long NowMs = 1_775_000_000_000;

    private TempSqliteDatabase _database = null!;

    public async Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();
        await new MigrationRunner(_database.Factory, new TestTimeProvider()).ApplyAsync(
            MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly),
            CancellationToken.None);
        Assert.Equal("wal", await _database.Factory.EnableWriteAheadLoggingAsync(CancellationToken.None));

        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO exercise (id, title, description, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($first, 'Primo', '', $now, $now), ($second, 'Secondo', '', $now, $now);
            INSERT INTO admin_user (
                id, username, normalized_username, password_hash, security_stamp,
                is_active, failed_login_count, created_at_utc_ms, updated_at_utc_ms)
                VALUES ($admin, 'Maestra', 'MAESTRA', 'hash-di-prova', 'stamp-di-prova', 1, 0, $now, $now);
            """;
        AddParameter(command, "$first", ExerciseId);
        AddParameter(command, "$second", SecondExerciseId);
        AddParameter(command, "$admin", AdminId);
        AddParameter(command, "$now", NowMs);
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync()
    {
        _database.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Bozza_RoundTrip_PreservaDocumentiESoluzioneFacoltativa()
    {
        // Arrange
        await using var work = await _database.Units.CreateAsync(useTransaction: true);
        var drafts = work.Repo<IExerciseDraftRepository>();
        var row = NewDraft(ExerciseId, referenceSolution: "{\"blocks\":{\"type\":\"solution\"}}");

        // Act: la chiave primaria della bozza è l'identificativo dell'esercizio.
        var id = await drafts.InsertAsync(row);
        var stored = await drafts.GetAsync(id);

        // Assert
        Assert.Equal(ExerciseId, id);
        Assert.True(await drafts.ExistsAsync(entity => entity.Id == ExerciseId));
        Assert.Equal(DefinitionJson, stored!.DefinitionJson);
        Assert.Equal("{\"blocks\":{}}", stored.WorkspaceJson);
        Assert.Equal("{\"blocks\":{\"type\":\"solution\"}}", stored.ReferenceSolutionJson);
        Assert.Equal(NowMs, stored.CreatedAtUtcMs);
        Assert.Equal(NowMs, stored.UpdatedAtUtcMs);
        Assert.Equal(1, stored.RowVersion);
        await work.CommitAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Bozza_Update_AvanzaLaRowVersionESalvaSenzaSovrascrivereCopieObsolete()
    {
        // Arrange
        await using var work = await _database.Units.CreateAsync(useTransaction: true);
        var drafts = work.Repo<IExerciseDraftRepository>();
        await drafts.InsertAsync(NewDraft(ExerciseId));
        var first = await drafts.GetAsync(ExerciseId);
        var stale = await drafts.GetAsync(ExerciseId);

        // Act: solo la copia letta per ultima porta la versione corrente.
        first!.WorkspaceJson = "{\"blocks\":{\"a\":1}}";
        first.UpdatedAtUtcMs = NowMs + 1000;
        Assert.True(await drafts.UpdateAsync(first));
        stale!.WorkspaceJson = "{\"blocks\":{\"b\":2}}";

        // Assert
        Assert.Equal(2, first.RowVersion);
        Assert.False(await drafts.UpdateAsync(stale));
        Assert.Equal(1, stale.RowVersion);
        var stored = await drafts.GetAsync(ExerciseId);
        Assert.Equal("{\"blocks\":{\"a\":1}}", stored!.WorkspaceJson);
        Assert.Equal(NowMs + 1000, stored.UpdatedAtUtcMs);
        await work.CommitAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Versione_RoundTrip_PreservaMetadatiEVersioneCostante()
    {
        // Arrange
        await using var work = await _database.Units.CreateAsync(useTransaction: true);
        var versions = work.Repo<IExerciseVersionRepository>();
        var row = NewVersion(VersionOneId, ExerciseId, versionNumber: 3);

        // Act
        var id = await versions.InsertAsync(row);
        var stored = await versions.GetAsync(id);

        // Assert: la versione esposta da IPersistedEntity è costante, non un token di concorrenza.
        Assert.Equal(VersionOneId, id);
        Assert.Equal(ExerciseId, stored!.ExerciseId);
        Assert.Equal(3, stored.VersionNumber);
        Assert.Equal(DefinitionJson, stored.DefinitionJson);
        Assert.Equal("{\"blocks\":{}}", stored.InitialWorkspaceJson);
        Assert.Equal("1.0.0", stored.EngineVersion);
        Assert.Equal("2026-10-01", stored.SchemaVersion);
        Assert.Equal("sha256:abc", stored.ContentHash);
        Assert.Equal(AdminId, stored.PublishedByAdminId);
        Assert.Equal(NowMs, stored.CreatedAtUtcMs);
        Assert.Equal(ExerciseVersionEntity.ImmutableRowVersion, stored.RowVersion);
        await work.CommitAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Versione_ConStessoNumeroPerLoStessoEsercizio_ViolaUnicita()
    {
        // Arrange: lo stesso numero è ammesso su esercizi diversi, non due volte sullo stesso.
        await using (var work = await _database.Units.CreateAsync(useTransaction: true))
        {
            var versions = work.Repo<IExerciseVersionRepository>();
            await versions.InsertAsync(NewVersion(VersionOneId, ExerciseId, versionNumber: 1));
            await versions.InsertAsync(NewVersion(OtherExerciseVersionId, SecondExerciseId, versionNumber: 1));
            await work.CommitAsync();
        }

        // Act: il lavoro fallito si chiude prima di rileggere, così la transazione non resta aperta.
        SqliteException error = null!;
        await using (var second = await _database.Units.CreateAsync(useTransaction: true))
        {
            error = await Assert.ThrowsAsync<SqliteException>(() =>
                second.Repo<IExerciseVersionRepository>().InsertAsync(NewVersion(DuplicateVersionId, ExerciseId, versionNumber: 1)));
        }

        // Assert
        Assert.Equal(UniqueViolationCode, error.SqliteExtendedErrorCode);
        await using var read = await _database.Units.CreateAsync();
        Assert.Equal(2, (await read.Repo<IExerciseVersionRepository>().GetAllAsync()).Count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Versione_ConUpdateDiretto_NonModificaLaRiga()
    {
        // Arrange: la versione esiste, inserita e confermata.
        await using (var work = await _database.Units.CreateAsync(useTransaction: true))
        {
            await work.Repo<IExerciseVersionRepository>().InsertAsync(NewVersion(VersionOneId, ExerciseId, versionNumber: 1));
            await work.CommitAsync();
        }

        // Act: un UPDATE diretto è rifiutato dal database, che è l'unico percorso che potrebbe
        // cambiare una riga pubblicata. La tabella non ha `updated_at_utc_ms` né `row_version`.
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE exercise_version SET definition_json = $definition WHERE id = $id;";
        AddParameter(command, "$definition", OtherDefinitionJson);
        AddParameter(command, "$id", VersionOneId);
        var error = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());

        // Assert
        Assert.Equal(ConstraintViolationCode, error.SqliteErrorCode);
        var columns = await ReadColumnsAsync("exercise_version");
        Assert.DoesNotContain("updated_at_utc_ms", columns);
        Assert.DoesNotContain("row_version", columns);
        await using var read = await _database.Units.CreateAsync();
        var stored = await read.Repo<IExerciseVersionRepository>().GetAsync(VersionOneId);
        Assert.Equal(DefinitionJson, stored!.DefinitionJson);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SoluzionePrivata_SenzaContenuti_RestaAnnullabileEUnValoreAllaVolta()
    {
        // Arrange: due versioni pubblicate, ciascuna con la propria riga privata.
        await using var work = await _database.Units.CreateAsync(useTransaction: true);
        var versions = work.Repo<IExerciseVersionRepository>();
        await versions.InsertAsync(NewVersion(VersionOneId, ExerciseId, versionNumber: 1));
        await versions.InsertAsync(NewVersion(OtherExerciseVersionId, SecondExerciseId, versionNumber: 1));
        var privateRows = work.Repo<IExerciseVersionPrivateRepository>();

        // Act: una riga senza contenuti, una con soluzione e note.
        await privateRows.InsertAsync(new ExerciseVersionPrivateEntity { Id = VersionOneId });
        await privateRows.InsertAsync(new ExerciseVersionPrivateEntity
        {
            Id = OtherExerciseVersionId,
            ReferenceSolutionJson = "{\"s\":1}",
            AuthorNotes = "Nota dell'autore",
        });

        // Assert
        var empty = await privateRows.GetAsync(VersionOneId);
        Assert.Null(empty!.ReferenceSolutionJson);
        Assert.Null(empty.AuthorNotes);
        var filled = await privateRows.GetAsync(OtherExerciseVersionId);
        Assert.Equal("{\"s\":1}", filled!.ReferenceSolutionJson);
        Assert.Equal("Nota dell'autore", filled.AuthorNotes);
        await work.CommitAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SoluzionePrivata_ViveInUnaTabellaSeparataDallaVersione()
    {
        // Arrange: la versione esiste senza una riga privata obbligatoria.
        await using (var work = await _database.Units.CreateAsync(useTransaction: true))
        {
            await work.Repo<IExerciseVersionRepository>().InsertAsync(NewVersion(VersionOneId, ExerciseId, versionNumber: 1));
            await work.CommitAsync();
        }

        // Act: i contenuti privati non sono colonne della versione.
        var versionColumns = await ReadColumnsAsync("exercise_version");
        var privateColumns = await ReadColumnsAsync("exercise_version_private");

        // Assert
        Assert.DoesNotContain("reference_solution_json", versionColumns);
        Assert.DoesNotContain("author_notes", versionColumns);
        Assert.Contains("reference_solution_json", privateColumns);
        Assert.Contains("author_notes", privateColumns);
        await using var read = await _database.Units.CreateAsync();
        Assert.Null(await read.Repo<IExerciseVersionPrivateRepository>().GetAsync(VersionOneId));
    }

    private static ExerciseDraftEntity NewDraft(string exerciseId, string? referenceSolution = null) => new()
    {
        Id = exerciseId,
        DefinitionJson = DefinitionJson,
        WorkspaceJson = "{\"blocks\":{}}",
        ReferenceSolutionJson = referenceSolution,
        CreatedAtUtcMs = NowMs,
        UpdatedAtUtcMs = NowMs,
        RowVersion = 1,
    };

    private static ExerciseVersionEntity NewVersion(string id, string exerciseId, long versionNumber) => new()
    {
        Id = id,
        ExerciseId = exerciseId,
        VersionNumber = versionNumber,
        DefinitionJson = DefinitionJson,
        InitialWorkspaceJson = "{\"blocks\":{}}",
        EngineVersion = "1.0.0",
        SchemaVersion = "2026-10-01",
        ContentHash = "sha256:abc",
        PublishedByAdminId = AdminId,
        CreatedAtUtcMs = NowMs,
    };

    private async Task<IReadOnlyList<string>> ReadColumnsAsync(string table)
    {
        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";

        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
