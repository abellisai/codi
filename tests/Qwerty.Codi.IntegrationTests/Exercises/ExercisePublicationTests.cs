using Qwerty.Codi.Application.Dto;
using System.Data.Common;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Exercises;
using Qwerty.Codi.Application.Persistence;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Exercises;

/// <summary>
/// Pubblicazione delle versioni, verifica della bozza e soluzione privata (§10.4, §10.5, §13.8)
/// attraverso il percorso reale: registro, canale, policy, deserializzazione, mediator, handler,
/// repository e database migrato.
///
/// La pubblicazione ha due proprietà che questi casi difendono: una versione è immutabile e
/// numerata per esercizio, e ripetere la stessa pubblicazione non ne crea una seconda. Il contesto
/// porta l'amministratore della credenziale, perché la versione ne registra l'autore con una chiave
/// esterna (§9.3).
/// </summary>
public sealed class ExercisePublicationTests : IAsyncLifetime
{
    /// <summary>Progetto iniziale valido: il punto di ingresso segue un passo.</summary>
    private const string Workspace =
        """{"blocks":{"languageVersion":0,"blocks":[{"type":"aula_start","id":"start-1","next":{"block":{"type":"aula_move","id":"move-1"}}}]}}""";

    /// <summary>Soluzione privata: un programma completo, con il suo punto di ingresso.</summary>
    private const string Solution =
        """{"blocks":{"languageVersion":0,"blocks":[{"type":"aula_start","id":"solution-start","next":{"block":{"type":"aula_move","id":"solution-move"}}}]}}""";

    /// <summary>Progetto iniziale diverso dal primo: serve alla seconda versione.</summary>
    private const string OtherWorkspace =
        """{"blocks":{"languageVersion":0,"blocks":[{"type":"aula_start","id":"start-2","next":{"block":{"type":"aula_turn","id":"turn-1","fields":{"TURN":"Right"}}}}]}}""";

    /// <summary>Seconda soluzione: risolve l'esercizio, ma è un altro documento.</summary>
    private const string OtherSolution =
        """{"blocks":{"languageVersion":0,"blocks":[{"type":"aula_start","id":"solution-b","next":{"block":{"type":"aula_turn","id":"turn-b","fields":{"TURN":"Right"}}}}]}}""";

    /// <summary>Terza soluzione: un altro documento ancora, per la terza versione.</summary>
    private const string ThirdSolution =
        """{"blocks":{"languageVersion":0,"blocks":[{"type":"aula_start","id":"solution-c","next":{"block":{"type":"aula_move","id":"move-c","next":{"block":{"type":"aula_turn","id":"turn-c","fields":{"TURN":"Right"}}}}}}]}}""";

    private CodiUseCaseHost _host = null!;
    private Guid _adminId;

    [Fact]
    [Trait("Category", "Integration")]
    public async Task QueryAsync_StatoPubblicazione_SegueBozzaPubblicazioneModificheECopia()
    {
        var exercise = await CreateExerciseAsync();
        await AssertStatusAsync(exercise.Id, "Draft");
        var draft = await SaveAsync(exercise.Id, DefinitionJson(), Workspace, Solution);
        await AssertStatusAsync(exercise.Id, "Draft");
        var first = await PublishAsync(exercise.Id, draft.RowVersion);
        await AssertStatusAsync(exercise.Id, "Available");
        var changed = await SaveAsync(exercise.Id, DefinitionJson(), OtherWorkspace, Solution, draft.RowVersion);
        await AssertStatusAsync(exercise.Id, "UnpublishedChanges");
        await PublishAsync(exercise.Id, changed.RowVersion);
        await AssertStatusAsync(exercise.Id, "Available");
        var copied = await CopyExerciseVersionToDraftAsync(exercise.Id, first.VersionNumber, changed.RowVersion);
        await AssertStatusAsync(exercise.Id, "UnpublishedChanges");
        var restored = await PublishAsync(exercise.Id, copied.RowVersion);
        Assert.Equal(3, restored.VersionNumber);
        Assert.NotEqual(first.Id, restored.Id);
        Assert.Equal(first.WorkspaceJson, restored.WorkspaceJson);
        await AssertStatusAsync(exercise.Id, "Available");
        var repeated = await PublishAsync(exercise.Id, copied.RowVersion);
        Assert.Equal(restored.Id, repeated.Id);
        Assert.Equal(3, (await ListVersionsAsync(exercise.Id)).Count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task QueryAsync_SolaSoluzionePrivataModificata_RichiedePubblicazione()
    {
        var exercise = await CreateExerciseAsync();
        var draft = await SaveAsync(exercise.Id, DefinitionJson(), Workspace, Solution);
        await PublishAsync(exercise.Id, draft.RowVersion);
        await SaveAsync(exercise.Id, DefinitionJson(), Workspace, OtherWorkspace, draft.RowVersion);
        await AssertStatusAsync(exercise.Id, "UnpublishedChanges");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task QueryAsync_BozzaAssenteConVersionePubblicata_RestaDisponibile()
    {
        var exercise = await CreateExerciseAsync();
        var draft = await SaveAsync(exercise.Id, DefinitionJson(), Workspace, Solution);
        await PublishAsync(exercise.Id, draft.RowVersion);
        await DeleteDraftAsync(exercise.Id);
        await AssertStatusAsync(exercise.Id, "Available");
    }

    private async Task AssertStatusAsync(Guid exerciseId, string expected)
    {
        var detail = await _host.QueryOneAsync<ExerciseDto>(GetExerciseQuery.RequestName, new { id = exerciseId });
        var list = await _host.QueryAsync<ExerciseDto>(ListExercisesQuery.RequestName, new { includeArchived = false });
        Assert.Equal(expected, detail.PublicationStatus);
        Assert.Equal(expected, Assert.Single(list, row => row.Id == exerciseId).PublicationStatus);
    }

    public async Task InitializeAsync()
    {
        _host = await CodiUseCaseHost.CreateAsync();
        _adminId = await InsertAdminUserAsync();
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_PublishExerciseVersion_CreaLaPrimaVersioneNumerata()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();
        var draft = await SaveAsync(exercise.Id, DefinitionJson(), Workspace, Solution);

        // Act
        var version = await PublishAsync(exercise.Id, draft.RowVersion);

        // Assert
        Assert.Equal(exercise.Id, version.ExerciseId);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal("gridpath-1", version.EngineVersion);
        Assert.Equal("1", version.SchemaVersion);
        Assert.NotEqual(string.Empty, version.ContentHash);
        // La versione conserva i documenti ricevuti: è la fotografia di ciò che il docente ha
        // verificato, non una loro riscrittura.
        Assert.Equal(Workspace, version.WorkspaceJson);
        Assert.Contains("\"title\":\"Raggiungi la stella\"", version.DefinitionJson, StringComparison.Ordinal);

        Assert.Equal(version, await GetVersionAsync(exercise.Id, 1));
        Assert.Equal(new[] { version }, await ListVersionsAsync(exercise.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_PublishExerciseVersionRipetuta_RestituisceLaStessaVersione()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();
        var draft = await SaveAsync(exercise.Id, DefinitionJson(), Workspace, Solution);
        var first = await PublishAsync(exercise.Id, draft.RowVersion);

        // Act: la stessa pubblicazione, ripetuta — è il caso di una risposta persa e ritentata.
        var second = await PublishAsync(exercise.Id, draft.RowVersion);

        // Assert
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.VersionNumber, second.VersionNumber);
        Assert.Single(await ListVersionsAsync(exercise.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_PublishExerciseVersionConNoteDiverse_CreaUnaNuovaVersione()
    {
        // Arrange: le note appartengono ai contenuti privati della versione, che è immutabile:
        // aggiungerle dopo richiede una versione nuova, non la modifica di quella pubblicata.
        var exercise = await CreateExerciseAsync();
        var draft = await SaveAsync(exercise.Id, DefinitionJson(), Workspace, Solution);
        var first = await PublishAsync(exercise.Id, draft.RowVersion);
        Assert.Null((await ReadPrivateAsync(first.Id))?.AuthorNotes);

        // Act
        var second = await PublishAsync(exercise.Id, draft.RowVersion, authorNotes: "  Usa un ciclo.  ");

        // Assert
        Assert.Equal(2, second.VersionNumber);
        Assert.Equal("Usa un ciclo.", (await ReadPrivateAsync(second.Id))?.AuthorNotes);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task QueryAsync_VersioniPubblicate_RiportanoLeNoteDellAutore()
    {
        // Arrange: le note vivono nella riga compagna privata, che la tabella delle versioni non
        // espone. Chi rilegge le versioni le riceve dal docente e non dagli alunni (§13.1), e una
        // versione senza note resta senza note invece di mostrare una stringa vuota.
        var exercise = await CreateExerciseAsync();
        var draft = await SaveAsync(exercise.Id, DefinitionJson(), Workspace, Solution);
        var first = await PublishAsync(exercise.Id, draft.RowVersion, authorNotes: "  Prima prova in classe.  ");
        await PublishAsync(exercise.Id, draft.RowVersion, authorNotes: "Alzato il limite dei passi.");
        await PublishAsync(exercise.Id, draft.RowVersion);

        // Act
        var versions = await ListVersionsAsync(exercise.Id);
        var withNotes = await GetVersionAsync(exercise.Id, 1);
        var withoutNotes = await GetVersionAsync(exercise.Id, 3);

        // Assert
        Assert.Equal("Prima prova in classe.", first.AuthorNotes);
        Assert.Equal(
            new string?[] { null, "Alzato il limite dei passi.", "Prima prova in classe." },
            versions.Select(version => version.AuthorNotes));
        Assert.Equal("Prima prova in classe.", withNotes.AuthorNotes);
        Assert.Null(withoutNotes.AuthorNotes);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_PublishExerciseVersionConContenutoDiverso_CreaLaSecondaVersione()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();
        var draft = await SaveAsync(exercise.Id, DefinitionJson(), Workspace, Solution);
        var first = await PublishAsync(exercise.Id, draft.RowVersion);

        // Act: la bozza cambia e si pubblica di nuovo.
        var updated = await SaveAsync(exercise.Id, DefinitionJson(), OtherWorkspace, Solution, draft.RowVersion);
        var second = await PublishAsync(exercise.Id, updated.RowVersion);

        // Assert
        Assert.Equal(2, second.VersionNumber);
        Assert.NotEqual(first.ContentHash, second.ContentHash);
        // L'elenco parte dalla più recente.
        Assert.Equal(new[] { second, first }, await ListVersionsAsync(exercise.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_PublishExerciseVersionSenzaVersioneDellaBozza_RifiutaConVersioneRichiesta()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();
        await SaveAsync(exercise.Id, DefinitionJson(), Workspace, Solution);

        // Act
        var failure = await ExpectFailureAsync(
            PublishExerciseVersionCommand.RequestName, new { exerciseId = exercise.Id });

        // Assert
        Assert.Equal(CodiErrorCodes.EntityVersionRequired, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_PublishExerciseVersionConVersioneSuperata_RifiutaConConflitto()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();
        var draft = await SaveAsync(exercise.Id, DefinitionJson(), Workspace, Solution);
        await SaveAsync(exercise.Id, DefinitionJson(), OtherWorkspace, Solution, draft.RowVersion);

        // Act: si pubblica la revisione letta prima dell'ultimo salvataggio.
        var failure = await ExpectFailureAsync(
            PublishExerciseVersionCommand.RequestName,
            new { exerciseId = exercise.Id, baseRowVersion = draft.RowVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) });

        // Assert
        Assert.Equal(CodiErrorCodes.RowVersionMismatch, failure.Code);
        Assert.Empty(await ListVersionsAsync(exercise.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_PublishExerciseVersionSenzaBozza_RifiutaConRisorsaAssente()
    {
        // Arrange: l'esercizio esiste, ma non c'è nulla da pubblicare.
        var exercise = await CreateExerciseAsync();

        // Act
        var failure = await ExpectFailureAsync(
            PublishExerciseVersionCommand.RequestName, new { exerciseId = exercise.Id, baseRowVersion = "1" });

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_PublishExerciseVersionSuEsercizioInesistente_RifiutaConRisorsaAssente()
    {
        // Act
        var failure = await ExpectFailureAsync(
            PublishExerciseVersionCommand.RequestName,
            new { exerciseId = Guid.NewGuid(), baseRowVersion = "1" });

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_PublishExerciseVersionConSoluzioneCheNonParte_RifiutaENonPubblica()
    {
        // Arrange: una soluzione senza punto di ingresso si può salvare — il docente la sta ancora
        // componendo — ma non pubblicare: non permetterebbe di verificare l'esercizio.
        var exercise = await CreateExerciseAsync();
        var withoutEntryPoint = """{"blocks":{"languageVersion":0,"blocks":[{"type":"aula_move","id":"move-1"}]}}""";
        var draft = await SaveAsync(exercise.Id, DefinitionJson(), Workspace, withoutEntryPoint);

        // Act
        var failure = await ExpectFailureAsync(
            PublishExerciseVersionCommand.RequestName,
            new { exerciseId = exercise.Id, baseRowVersion = draft.RowVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) });

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Contains("soluzione di riferimento", failure.Message, StringComparison.Ordinal);
        Assert.Empty(await ListVersionsAsync(exercise.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_PublishExerciseVersion_MantieneLaSoluzionePrivataFuoriDalDto()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();
        var draft = await SaveAsync(exercise.Id, DefinitionJson(), Workspace, Solution);

        // Act
        var version = await PublishAsync(exercise.Id, draft.RowVersion);

        // Assert: la versione esce senza la soluzione, che vive nella tabella privata (§13.1).
        Assert.DoesNotContain("solution-start", version.WorkspaceJson, StringComparison.Ordinal);
        Assert.DoesNotContain("solution-start", version.DefinitionJson, StringComparison.Ordinal);
        Assert.Equal(Solution, (await ReadPrivateAsync(version.Id))?.ReferenceSolutionJson);
        Assert.Equal(_adminId.ToString("D"), await ReadPublisherAsync(version.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task QueryAsync_ListExerciseVersionsSenzaVersioni_RestituisceElencoVuoto()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();

        // Act / Assert
        Assert.Empty(await ListVersionsAsync(exercise.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task QueryAsync_GetExerciseVersionConNumeroInesistente_RifiutaConRisorsaAssente()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();
        var draft = await SaveAsync(exercise.Id, DefinitionJson(), Workspace, Solution);
        await PublishAsync(exercise.Id, draft.RowVersion);

        // Act
        var failure = await _host.ExpectQueryFailureAsync(
            GetExerciseVersionQuery.RequestName, new { exerciseId = exercise.Id, versionNumber = 2 });

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task QueryAsync_GetReferenceSolution_RestituisceLaSoluzioneDellaBozza()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();
        await SaveAsync(exercise.Id, DefinitionJson(), Workspace, Solution);

        // Act
        var solution = await _host.QueryOneAsync<ExerciseReferenceSolutionDto>(
            GetReferenceSolutionQuery.RequestName, new { exerciseId = exercise.Id });

        // Assert
        Assert.Equal(exercise.Id, solution.ExerciseId);
        Assert.Equal(Solution, solution.WorkspaceJson);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task QueryAsync_GetReferenceSolutionSenzaBozza_RifiutaConRisorsaAssente()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();

        // Act
        var failure = await _host.ExpectQueryFailureAsync(
            GetReferenceSolutionQuery.RequestName, new { exerciseId = exercise.Id });

        // Assert: senza bozza non c'è una soluzione da leggere, e il 404 lo dice.
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_PublishDopoSoloModificaDellaSoluzione_NuovaVersioneConImprontaDiversa()
    {
        // Arrange: contenuto identico, cambia solo la soluzione di riferimento.
        var exercise = await CreateExerciseAsync();
        var draft = await SaveAsync(exercise.Id, DefinitionJson(), Workspace, Solution);
        var first = await PublishAsync(exercise.Id, draft.RowVersion);

        var changed = await SaveAsync(exercise.Id, DefinitionJson(), Workspace, OtherSolution, draft.RowVersion);
        await AssertStatusAsync(exercise.Id, "UnpublishedChanges");

        // Act
        var second = await PublishAsync(exercise.Id, changed.RowVersion);

        // Assert: l'impronta del contenuto non cambia — il contenuto non è cambiato — e l'impronta
        // di riferimento sì. Il contenuto è immutato, la pubblicazione nuova.
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, second.VersionNumber);
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.NotNull(first.ReferenceHash);
        Assert.NotNull(second.ReferenceHash);
        Assert.NotEqual(first.ReferenceHash, second.ReferenceHash);
        Assert.Equal(2, (await ListVersionsAsync(exercise.Id)).Count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task QueryAsync_GetReferenceSolutionPerNumero_RestituisceLaSnapshotDellaVersione()
    {
        // Arrange: tre pubblicazioni con la stessa definizione e tre soluzioni diverse, come le
        // versioni GP02 della verifica QA: lo stesso contenuto, soluzioni diverse.
        var exercise = await CreateExerciseAsync();
        var draft = await SaveAsync(exercise.Id, DefinitionJson(), Workspace, Solution);
        var first = await PublishAsync(exercise.Id, draft.RowVersion);
        var secondDraft = await SaveAsync(exercise.Id, DefinitionJson(), Workspace, OtherSolution, draft.RowVersion);
        var second = await PublishAsync(exercise.Id, secondDraft.RowVersion);
        var thirdDraft = await SaveAsync(exercise.Id, DefinitionJson(), Workspace, ThirdSolution, secondDraft.RowVersion);
        await PublishAsync(exercise.Id, thirdDraft.RowVersion);

        // Act
        var firstSolution = await _host.QueryOneAsync<ExerciseReferenceSolutionDto>(
            GetReferenceSolutionQuery.RequestName, new { exerciseId = exercise.Id, versionNumber = "1" });
        var secondSolution = await _host.QueryOneAsync<ExerciseReferenceSolutionDto>(
            GetReferenceSolutionQuery.RequestName, new { exerciseId = exercise.Id, versionNumber = "2" });
        var thirdSolution = await _host.QueryOneAsync<ExerciseReferenceSolutionDto>(
            GetReferenceSolutionQuery.RequestName, new { exerciseId = exercise.Id, versionNumber = "3" });
        var draftSolution = await _host.QueryOneAsync<ExerciseReferenceSolutionDto>(
            GetReferenceSolutionQuery.RequestName, new { exerciseId = exercise.Id });

        // Assert: ogni versione restituisce la propria soluzione, non l'ultima scritta. La lettura
        // senza numero resta quella della bozza, come prima della migrazione.
        Assert.Equal(Solution, firstSolution.WorkspaceJson);
        Assert.Equal(OtherSolution, secondSolution.WorkspaceJson);
        Assert.Equal(ThirdSolution, thirdSolution.WorkspaceJson);
        Assert.Equal(ThirdSolution, draftSolution.WorkspaceJson);
        Assert.NotEqual(first.ReferenceHash, second.ReferenceHash);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task QueryAsync_GetReferenceSolutionSuVersioneInesistente_RifiutaConRisorsaAssente()
    {
        var exercise = await CreateExerciseAsync();
        var draft = await SaveAsync(exercise.Id, DefinitionJson(), Workspace, Solution);
        await PublishAsync(exercise.Id, draft.RowVersion);

        // Act
        var failure = await _host.ExpectQueryFailureAsync(
            GetReferenceSolutionQuery.RequestName, new { exerciseId = exercise.Id, versionNumber = "99" });

        // Assert: non c'è la soluzione di una versione che non esiste.
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_PublishExerciseVersion_LImprontaDiRiferimentoCorrispondeAllaSoluzione()
    {
        var exercise = await CreateExerciseAsync();
        var draft = await SaveAsync(exercise.Id, DefinitionJson(), Workspace, Solution);

        // Act
        var version = await PublishAsync(exercise.Id, draft.RowVersion);

        // Assert: l'impronta esposta è quella SHA-256 del documento canonico della soluzione, in
        // esadecimale minuscola, e la snapshot privata resta il documento completo (§13.1).
        var expected = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Solution)));
        Assert.Equal(expected, version.ReferenceHash);
        Assert.Equal(Solution, (await ReadPrivateAsync(version.Id))?.ReferenceSolutionJson);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ValidateExerciseDraftConBozzaPubblicabile_NonSegnalaProblemi()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();

        // Act
        var validation = await ValidateAsync(exercise.Id, DefinitionJson(), Workspace, Solution);

        // Assert
        Assert.True(validation.IsValid);
        Assert.Empty(validation.Problems);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ValidateExerciseDraftConProgettoNonValido_RiportaIlProblema()
    {
        // Arrange: un blocco che l'esercizio non mette a disposizione.
        var exercise = await CreateExerciseAsync();
        var program = """{"blocks":{"languageVersion":0,"blocks":[{"type":"aula_start","id":"s","next":{"block":{"type":"aula_turn","id":"t","fields":{"TURN":"Left"}}}}]}}""";

        // Act
        var validation = await ValidateAsync(exercise.Id, DefinitionJson(allowTurn: false), program);

        // Assert: la verifica non pubblica e non scrive, ma dice che cosa impedisce di pubblicare.
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Problems, problem => problem.Contains("non mette a disposizione", StringComparison.Ordinal));
        Assert.Empty(await ListVersionsAsync(exercise.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ValidateExerciseDraftConDefinizioneIllegibile_RiportaUnSoloProblema()
    {
        // Arrange: senza una definizione valida non si possono verificare nemmeno i progetti.
        var exercise = await CreateExerciseAsync();

        // Act
        var validation = await ValidateAsync(exercise.Id, "{ non è JSON }", Workspace);

        // Assert
        Assert.False(validation.IsValid);
        Assert.Single(validation.Problems);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ValidateExerciseDraftConSoluzioneCheNonParte_RiportaIlProblema()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();
        var withoutEntryPoint = """{"blocks":{"languageVersion":0,"blocks":[{"type":"aula_move","id":"move-1"}]}}""";

        // Act
        var validation = await ValidateAsync(exercise.Id, DefinitionJson(), Workspace, withoutEntryPoint);

        // Assert
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Problems, problem => problem.Contains("non ha il blocco", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ValidateExerciseDraftSuEsercizioInesistente_RifiutaConRisorsaAssente()
    {
        // Act
        var failure = await ExpectFailureAsync(
            ValidateExerciseDraftCommand.RequestName,
            new { exerciseId = Guid.NewGuid(), definitionJson = DefinitionJson(), workspaceJson = Workspace });

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_CopyExerciseVersionToDraft_RiportaIDocumentiDellaVersione()
    {
        // Arrange: si pubblica, poi la bozza cambia, poi si torna alla versione pubblicata.
        var exercise = await CreateExerciseAsync();
        var draft = await SaveAsync(exercise.Id, DefinitionJson(), Workspace, Solution);
        var version = await PublishAsync(exercise.Id, draft.RowVersion);
        var changed = await SaveAsync(exercise.Id, DefinitionJson(), OtherWorkspace, Solution, draft.RowVersion);

        // Act
        var copied = await CopyExerciseVersionToDraftAsync(exercise.Id, version.VersionNumber, changed.RowVersion);

        // Assert: la bozza torna ai documenti della versione, che resta quella che era.
        Assert.Equal(version.DefinitionJson, copied.DefinitionJson);
        Assert.Equal(version.WorkspaceJson, copied.WorkspaceJson);
        Assert.Equal(changed.RowVersion + 1, copied.RowVersion);
        Assert.Equal(Solution, (await ReadDraftAsync(exercise.Id))?.ReferenceSolutionJson);
        Assert.Equal(new[] { version }, await ListVersionsAsync(exercise.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_CopyExerciseVersionToDraftSenzaBozza_CreaLaBozza()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();
        var draft = await SaveAsync(exercise.Id, DefinitionJson(), Workspace, Solution);
        var version = await PublishAsync(exercise.Id, draft.RowVersion);
        await DeleteDraftAsync(exercise.Id);

        // Act: senza bozza la copia è una creazione, quindi non dichiara una versione.
        var copied = await CopyExerciseVersionToDraftAsync(exercise.Id, version.VersionNumber);

        // Assert
        Assert.Equal(1, copied.RowVersion);
        Assert.Equal(version.WorkspaceJson, copied.WorkspaceJson);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_CopyExerciseVersionToDraftConVersioneInesistente_RifiutaConRisorsaAssente()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();

        // Act
        var failure = await ExpectFailureAsync(
            CopyExerciseVersionToDraftCommand.RequestName, new { exerciseId = exercise.Id, versionNumber = 1 });

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_CopyExerciseVersionToDraftSenzaVersioneDellaBozza_RifiutaConVersioneRichiesta()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();
        var draft = await SaveAsync(exercise.Id, DefinitionJson(), Workspace, Solution);
        var version = await PublishAsync(exercise.Id, draft.RowVersion);

        // Act: la bozza esiste, quindi la copia deve dichiarare la sua versione.
        var failure = await ExpectFailureAsync(
            CopyExerciseVersionToDraftCommand.RequestName, new { exerciseId = exercise.Id, versionNumber = version.VersionNumber });

        // Assert
        Assert.Equal(CodiErrorCodes.EntityVersionRequired, failure.Code);
    }

    private Task<ExerciseDraftValidationDto> ValidateAsync(
        Guid exerciseId,
        string definitionJson,
        string workspaceJson,
        string? referenceSolutionJson = null) =>
        CommandAsync<ExerciseDraftValidationDto>(
            ValidateExerciseDraftCommand.RequestName,
            new
            {
                exerciseId,
                definitionJson,
                workspaceJson,
                referenceSolutionJson,
            });

    private Task<ExerciseVersionDto> PublishAsync(Guid exerciseId, long baseRowVersion, string? authorNotes = null) =>
        CommandAsync<ExerciseVersionDto>(
            PublishExerciseVersionCommand.RequestName,
            new
            {
                exerciseId,
                baseRowVersion = baseRowVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                authorNotes,
            });

    private Task<ExerciseDraftDto> CopyExerciseVersionToDraftAsync(
        Guid exerciseId,
        long versionNumber,
        long? baseRowVersion = null)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["exerciseId"] = exerciseId,
            ["versionNumber"] = versionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        if (baseRowVersion is not null)
        {
            payload["baseRowVersion"] = baseRowVersion.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return CommandAsync<ExerciseDraftDto>(CopyExerciseVersionToDraftCommand.RequestName, payload);
    }

    private Task<IReadOnlyList<ExerciseVersionDto>> ListVersionsAsync(Guid exerciseId) =>
        _host.QueryAsync<ExerciseVersionDto>(ListExerciseVersionsQuery.RequestName, new { exerciseId });

    private Task<ExerciseVersionDto> GetVersionAsync(Guid exerciseId, long versionNumber) =>
        _host.QueryOneAsync<ExerciseVersionDto>(
            GetExerciseVersionQuery.RequestName, new { exerciseId, versionNumber });

    private async Task<ExerciseDto> CreateExerciseAsync() =>
        await _host.CommandAsync<ExerciseDto>(
            CreateExerciseCommand.RequestName, new { exerciseType = "GridPath", title = "Raggiungi la stella" });

    private async Task<ExerciseDraftDto> SaveAsync(
        Guid exerciseId,
        string definitionJson,
        string workspaceJson,
        string? referenceSolutionJson = null,
        long? baseRowVersion = null)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["exerciseId"] = exerciseId,
            ["definitionJson"] = definitionJson,
            ["workspaceJson"] = workspaceJson,
            ["referenceSolutionJson"] = referenceSolutionJson,
        };

        if (baseRowVersion is not null)
        {
            payload["baseRowVersion"] = baseRowVersion.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return await CommandAsync<ExerciseDraftDto>(SaveExerciseDraftCommand.RequestName, payload);
    }

    /// <summary>Esegue un comando della pubblicazione con l'amministratore della credenziale.</summary>
    private async Task<T> CommandAsync<T>(string name, object body)
    {
        using var payload = CodiUseCaseHost.Payload(body);

        return Assert.IsType<T>(
            await _host.Executor.ExecuteAsync(
                CodiRequestKind.Command, name, CodiRequestChannel.Http, payload.RootElement, AdminContext(_adminId), CancellationToken.None));
    }

    private async Task<CodiRequestException> ExpectFailureAsync(string name, object body)
    {
        using var payload = CodiUseCaseHost.Payload(body);

        return await Assert.ThrowsAsync<CodiRequestException>(
            () => _host.Executor.ExecuteAsync(
                CodiRequestKind.Command, name, CodiRequestChannel.Http, payload.RootElement, AdminContext(_adminId), CancellationToken.None).AsTask());
    }

    private static CodiRequestContext AdminContext(Guid adminId) =>
        new(isAuthenticated: true, [CodiPolicy.Admin], adminUserId: adminId);

    private async Task<ExerciseVersionPrivateEntity?> ReadPrivateAsync(Guid versionId)
    {
        await using var work = await _host.Services.GetRequiredService<IUnitOfWorkContext>().CreateAsync();

        return await work.Repo<IExerciseVersionPrivateRepository>().GetAsync(versionId.ToString("D"));
    }

    private async Task<ExerciseDraftEntity?> ReadDraftAsync(Guid exerciseId)
    {
        await using var work = await _host.Services.GetRequiredService<IUnitOfWorkContext>().CreateAsync();

        return await work.Repo<IExerciseDraftRepository>().GetAsync(exerciseId.ToString("D"));
    }

    private async Task<string> ReadPublisherAsync(Guid versionId)
    {
        await using var connection = await _host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT published_by_admin_id FROM exercise_version WHERE id = $id;";
        AddParameter(command, "$id", versionId.ToString("D"));

        return (string)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Rimuove la bozza, per provare la copia che la ricrea.</summary>
    private async Task DeleteDraftAsync(Guid exerciseId)
    {
        await using var connection = await _host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM exercise_draft WHERE exercise_id = $id;";
        AddParameter(command, "$id", exerciseId.ToString("D"));

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Inserisce una riga in <c>admin_user</c>: la composizione dei casi d'uso non registra
    /// l'impronta delle password, ma la chiave esterna della versione richiede l'amministratore.
    /// </summary>
    private async Task<Guid> InsertAdminUserAsync()
    {
        var id = Guid.NewGuid();

        await using var connection = await _host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO admin_user (
                id, username, normalized_username, password_hash, security_stamp,
                is_active, failed_login_count, created_at_utc_ms, updated_at_utc_ms)
            VALUES ($id, 'Maestra', 'MAESTRA-' || $id, 'hash-di-prova', 'stamp-di-prova', 1, 0, 1000, 1000);
            """;
        AddParameter(command, "$id", id.ToString("D"));

        await command.ExecuteNonQueryAsync();

        return id;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    /// <summary>Definizione di §13.2, con la sola differenza dei blocchi messi a disposizione.</summary>
    private static string DefinitionJson(bool allowTurn = true)
    {
        var blocks = allowTurn
            ? """["aula_start","aula_move","aula_repeat","aula_turn"]"""
            : """["aula_start","aula_move","aula_repeat"]""";

        return $$"""
        {
          "schemaVersion": 1,
          "exerciseType": "GridPath",
          "engineVersion": "gridpath-1",
          "title": "Raggiungi la stella",
          "instructions": "Porta il personaggio sulla stella usando i blocchi.",
          "scene": {
            "width": 5,
            "height": 4,
            "start": { "x": 0, "y": 1, "direction": "East" },
            "obstacles": [{ "x": 2, "y": 2 }],
            "goal": { "x": 3, "y": 1 }
          },
          "toolbox": { "allowedBlocks": {{blocks}} },
          "limits": { "maxBlocks": 30, "maxNestingDepth": 8, "maxExecutionSteps": 1000, "maxActiveDurationMs": 120000 },
          "completion": { "type": "ReachCell", "x": 3, "y": 1 },
          "feedback": { "success": "Hai raggiunto la stella!", "collision": "C'è un ostacolo: prova a cambiare percorso." }
        }
        """;
    }
}
