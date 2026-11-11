using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Exercises;
using Qwerty.Codi.Domain.Exercises;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Exercises;

/// <summary>
/// Casi d'uso dell'anagrafica degli esercizi (§10.4, §10.5) attraverso il percorso reale:
/// registro, canale, policy, deserializzazione, mediator, handler, repository e database migrato.
/// L'esercizio è una libreria: il titolo non è una chiave, quindi due esercizi possono chiamarsi
/// allo stesso modo e nessun caso d'uso risponde con un conflitto.
/// </summary>
public sealed class ExerciseUseCaseTests : IAsyncLifetime
{
    private CodiUseCaseHost _host = null!;

    public async Task InitializeAsync() => _host = await CodiUseCaseHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_CreateExercise_RipulisceIlTitoloECompareNellElenco()
    {
        // Act
        var exercise = await _host.CommandAsync<ExerciseDto>(
            CreateExerciseCommand.RequestName,
            new { exerciseType = "GridPath", title = "  Ciclo con i blocchi  ", description = "Ripetere tre volte" });

        // Assert
        Assert.Equal("Ciclo con i blocchi", exercise.Title);
        Assert.Equal("Ripetere tre volte", exercise.Description);
        Assert.False(exercise.IsArchived);
        Assert.Equal(1, exercise.RowVersion);
        Assert.Equal(exercise, Assert.Single(await ListExercisesAsync()));
        Assert.Equal(exercise, await GetExerciseAsync(exercise.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_CreateExerciseSenzaDescrizione_LaMemorizzaVuota()
    {
        // Act
        var exercise = await _host.CommandAsync<ExerciseDto>(
            CreateExerciseCommand.RequestName, new { exerciseType = "GridPath", title = "Ripetizione" });

        // Assert: nessuna descrizione è un valore, non un dato mancante.
        Assert.Equal(string.Empty, exercise.Description);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ListExercises_OrdinaPerTitoloSenzaDistinguereLeMaiuscole()
    {
        // Arrange
        await CreateExerciseAsync("ripetizione");
        await CreateExerciseAsync("Ciclo con i blocchi");
        await CreateExerciseAsync("Algoritmo");

        // Act
        var listed = await ListExercisesAsync();

        // Assert
        Assert.Equal(
            new[] { "Algoritmo", "Ciclo con i blocchi", "ripetizione" },
            listed.Select(exercise => exercise.Title));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_CreateExerciseConTitoloNonUtilizzabile_RifiutaConIlCodiceDelNome()
    {
        // Act
        var empty = await _host.ExpectFailureAsync(
            CreateExerciseCommand.RequestName, new { exerciseType = "GridPath", title = "   " });
        var tooLong = await _host.ExpectFailureAsync(
            CreateExerciseCommand.RequestName,
            new { exerciseType = "GridPath", title = new string('a', Exercise.MaxTitleLength + 1) });

        // Assert
        Assert.Equal(CodiErrorCodes.InvalidName, empty.Code);
        Assert.Equal(CodiErrorCodes.InvalidName, tooLong.Code);
        Assert.Empty(await ListExercisesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_CreateExerciseConDescrizioneTroppoLunga_RifiutaConValidazione()
    {
        // Act
        var exception = await _host.ExpectFailureAsync(
            CreateExerciseCommand.RequestName,
            new { exerciseType = "GridPath", title = "Ciclo con i blocchi", description = new string('a', Exercise.MaxDescriptionLength + 1) });

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, exception.Code);
        Assert.Empty(await ListExercisesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UpdateExercise_CambiaITestiEAlzaLaVersione()
    {
        // Arrange
        var created = await CreateExerciseAsync("Ciclo con i blocchi");

        // Act
        var updated = await _host.CommandAsync<ExerciseDto>(
            UpdateExerciseCommand.RequestName,
            new { id = created.Id, title = "  Ripetizione  ", description = "Tre volte", baseRowVersion = created.RowVersion });

        // Assert
        Assert.Equal("Ripetizione", updated.Title);
        Assert.Equal("Tre volte", updated.Description);
        Assert.Equal(2, updated.RowVersion);
        Assert.Equal(updated, await GetExerciseAsync(created.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UpdateExerciseConGliStessiValori_NonAlzaLaVersione()
    {
        // Arrange
        var created = await CreateExerciseAsync("Ciclo con i blocchi");

        // Act
        var updated = await _host.CommandAsync<ExerciseDto>(
            UpdateExerciseCommand.RequestName,
            new { id = created.Id, title = "Ciclo con i blocchi", description = "", baseRowVersion = created.RowVersion });

        // Assert
        Assert.Equal(created, updated);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UpdateExerciseConDatiNonValidi_RifiutaConIlCodiceAttesoSenzaScrivere()
    {
        // Arrange
        var created = await CreateExerciseAsync("Ciclo con i blocchi");

        // Act: la validazione precede la lettura della riga, quindi l'esercizio resta com'era.
        var invalidTitle = await _host.ExpectFailureAsync(
            UpdateExerciseCommand.RequestName,
            new { id = created.Id, title = "   ", baseRowVersion = created.RowVersion });
        var tooLongTitle = await _host.ExpectFailureAsync(
            UpdateExerciseCommand.RequestName,
            new
            {
                id = created.Id,
                title = new string('a', Exercise.MaxTitleLength + 1),
                baseRowVersion = created.RowVersion,
            });
        var longDescription = await _host.ExpectFailureAsync(
            UpdateExerciseCommand.RequestName,
            new
            {
                id = created.Id,
                title = "Ripetizione",
                description = new string('a', Exercise.MaxDescriptionLength + 1),
                baseRowVersion = created.RowVersion,
            });

        // Assert
        Assert.Equal(CodiErrorCodes.InvalidName, invalidTitle.Code);
        Assert.Equal(CodiErrorCodes.InvalidName, tooLongTitle.Code);
        Assert.Equal(CodiErrorCodes.ValidationFailed, longDescription.Code);
        Assert.Equal(created, await GetExerciseAsync(created.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UpdateOArchiveSenzaVersioneOConVersioneObsoleta_RifiutanoSenzaScrivere()
    {
        // Arrange
        var created = await CreateExerciseAsync("Ciclo con i blocchi");

        // Act
        var updateWithoutVersion = await _host.ExpectFailureAsync(
            UpdateExerciseCommand.RequestName, new { id = created.Id, title = "Ripetizione" });
        var updateStale = await _host.ExpectFailureAsync(
            UpdateExerciseCommand.RequestName,
            new { id = created.Id, title = "Ripetizione", baseRowVersion = created.RowVersion + 1 });
        var updateMissing = await _host.ExpectFailureAsync(
            UpdateExerciseCommand.RequestName,
            new { id = Guid.NewGuid(), title = "Ripetizione", baseRowVersion = 1L });

        // Assert
        Assert.Equal(CodiErrorCodes.EntityVersionRequired, updateWithoutVersion.Code);
        Assert.Equal(CodiErrorCodes.RowVersionMismatch, updateStale.Code);
        Assert.Equal(CodiErrorCodes.ResourceNotFound, updateMissing.Code);
        Assert.Equal(created, await GetExerciseAsync(created.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ArchiveExercise_EsceDallElencoAttivoEConservaLaRiga()
    {
        // Arrange
        var created = await CreateExerciseAsync("Ciclo con i blocchi");

        // Act
        var archived = await _host.CommandAsync<ExerciseDto>(
            ArchiveExerciseCommand.RequestName,
            new { id = created.Id, baseRowVersion = created.RowVersion });

        // Assert
        Assert.True(archived.IsArchived);
        Assert.Equal(2, archived.RowVersion);
        Assert.Empty(await ListExercisesAsync());
        Assert.Equal(archived, Assert.Single(await ListExercisesAsync(includeArchived: true)));

        // Ripetere l'archiviazione restituisce lo stato corrente senza alzare la versione.
        var again = await _host.CommandAsync<ExerciseDto>(
            ArchiveExerciseCommand.RequestName,
            new { id = created.Id, baseRowVersion = archived.RowVersion });
        Assert.Equal(archived, again);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ArchiveExerciseSenzaVersioneOConVersioneObsoleta_Rifiuta()
    {
        // Arrange
        var created = await CreateExerciseAsync("Ciclo con i blocchi");

        // Act
        var withoutVersion = await _host.ExpectFailureAsync(
            ArchiveExerciseCommand.RequestName, new { id = created.Id });
        var stale = await _host.ExpectFailureAsync(
            ArchiveExerciseCommand.RequestName, new { id = created.Id, baseRowVersion = created.RowVersion + 1 });
        var missing = await _host.ExpectFailureAsync(
            ArchiveExerciseCommand.RequestName, new { id = Guid.NewGuid(), baseRowVersion = 1L });

        // Assert
        Assert.Equal(CodiErrorCodes.EntityVersionRequired, withoutVersion.Code);
        Assert.Equal(CodiErrorCodes.RowVersionMismatch, stale.Code);
        Assert.Equal(CodiErrorCodes.ResourceNotFound, missing.Code);
        Assert.False((await GetExerciseAsync(created.Id)).IsArchived);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_GetExerciseDiUnIdentificativoInesistente_RestituisceRisorsaNonTrovata()
    {
        // Act
        var exception = await _host.ExpectQueryFailureAsync(
            GetExerciseQuery.RequestName, new { id = Guid.NewGuid() });

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, exception.Code);
    }

    private Task<ExerciseDto> CreateExerciseAsync(string title, string? description = null) =>
        _host.CommandAsync<ExerciseDto>(CreateExerciseCommand.RequestName, new { title, exerciseType = "GridPath", description });

    private Task<IReadOnlyList<ExerciseDto>> ListExercisesAsync(bool includeArchived = false) =>
        _host.QueryAsync<ExerciseDto>(ListExercisesQuery.RequestName, new { includeArchived });

    private Task<ExerciseDto> GetExerciseAsync(Guid id) =>
        _host.QueryOneAsync<ExerciseDto>(GetExerciseQuery.RequestName, new { id });
}
