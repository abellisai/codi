using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Exercises;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Exercises;

/// <summary>
/// Casi d'uso della bozza di autoraggio (§10.4, §10.5) attraverso il percorso reale: registro,
/// canale, policy, deserializzazione, mediator, handler, repository e database migrato.
///
/// La bozza ha due esiti che non si vedono altrove nel catalogo: il primo salvataggio la crea e non
/// dichiara una versione, i successivi la richiedono. I casi qui sotto separano i due percorsi,
/// perché confonderli significherebbe accettare una scrittura su una versione mai letta.
/// </summary>
public sealed class ExerciseDraftUseCaseTests : IAsyncLifetime
{
    /// <summary>
    /// Progetto a blocchi di prova. È un oggetto JSON senza blocchi: la bozza può esistere prima che
    /// il docente abbia composto il programma, e il progetto vuoto è la sua condizione di partenza.
    /// La verifica dei tipi, dei campi e dei collegamenti è nella classe dedicata.
    /// </summary>
    private const string Workspace = "{}";

    private CodiUseCaseHost _host = null!;

    public async Task InitializeAsync() => _host = await CodiUseCaseHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SaveExerciseDraftSuEsercizioNuovo_CreaLaBozzaEGetExerciseDraftLaRestituisce()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();

        // Act
        var saved = await SaveAsync(exercise.Id, DefinitionJson());

        // Assert
        Assert.Equal(exercise.Id, saved.ExerciseId);
        Assert.Equal(1, saved.RowVersion);
        Assert.Equal(saved, await GetDraftAsync(exercise.Id));
        Assert.Contains(
            "\"instructions\":\"Porta il personaggio sulla stella usando i blocchi.\"",
            saved.DefinitionJson,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SaveExerciseDraft_MemorizzaIlDocumentoCanonico()
    {
        // Arrange: titolo con spazi esterni e campi in ordine diverso da quello di §13.2.
        var exercise = await CreateExerciseAsync();
        var shuffled = $$"""
            {
              "instructions": "Porta il personaggio sulla stella usando i blocchi.",
              "title": "  Raggiungi la stella  ",
              "engineVersion": "gridpath-1",
              "exerciseType": "GridPath",
              "schemaVersion": 1,
              "feedback": { "success": "Bravo!", "collision": "Ostacolo." },
              "completion": { "type": "ReachCell", "x": 3, "y": 1 },
              "limits": { "maxBlocks": 30, "maxNestingDepth": 8, "maxExecutionSteps": 1000, "maxActiveDurationMs": 120000 },
              "toolbox": { "allowedBlocks": ["aula_start", "aula_move"] },
              "scene": {
                "width": 5,
                "height": 4,
                "start": { "x": 0, "y": 1, "direction": "East" },
                "obstacles": [],
                "goal": { "x": 3, "y": 1 }
              }
            }
            """;

        // Act
        var saved = await SaveAsync(exercise.Id, shuffled);

        // Assert: ciò che è memorizzato è il documento prodotto dalla verifica, non il testo
        // ricevuto. Il titolo ripulito e l'ordine stabile sono la prova che è stato riscritto.
        Assert.StartsWith("{\"schemaVersion\":1,", saved.DefinitionJson, StringComparison.Ordinal);
        Assert.Contains("\"title\":\"Raggiungi la stella\"", saved.DefinitionJson, StringComparison.Ordinal);
        Assert.Contains("\"allowedBlocks\":[\"aula_start\",\"aula_move\"]", saved.DefinitionJson, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SaveExerciseDraft_IlDocumentoCanonicoRestaQuelloPubblicato()
    {
        // Arrange: la definizione di §13.2, con i suoi due blocchi.
        var exercise = await CreateExerciseAsync();
        var definition = """
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
                "obstacles": [],
                "goal": { "x": 3, "y": 1 }
              },
              "toolbox": { "allowedBlocks": ["aula_start", "aula_move"] },
              "limits": { "maxBlocks": 30, "maxNestingDepth": 8, "maxExecutionSteps": 1000, "maxActiveDurationMs": 120000 },
              "completion": { "type": "ReachCell", "x": 3, "y": 1 },
              "feedback": { "success": "Bravo!", "collision": "Ostacolo." }
            }
            """;

        // Act
        var saved = await SaveAsync(exercise.Id, definition);

        // Assert: il documento canonico è esattamente quello di §13.2, con le proprietà generali in
        // testa e i dati di `GridPath` in coda. Questa uguaglianza è la garanzia che il refactoring
        // del registro dei motori non ha spostato un byte: l'impronta di contenuto di un esercizio
        // già pubblicato si calcola su questo testo, e una proprietà spostata la cambierebbe,
        // facendo sembrare nuova una versione identica (§13.8).
        Assert.Equal(
            """
            {"schemaVersion":1,"exerciseType":"GridPath","engineVersion":"gridpath-1","title":"Raggiungi la stella","instructions":"Porta il personaggio sulla stella usando i blocchi.","scene":{"width":5,"height":4,"start":{"x":0,"y":1,"direction":"East"},"obstacles":[],"goal":{"x":3,"y":1}},"toolbox":{"allowedBlocks":["aula_start","aula_move"]},"limits":{"maxBlocks":30,"maxNestingDepth":8,"maxExecutionSteps":1000,"maxActiveDurationMs":120000},"completion":{"type":"ReachCell","x":3,"y":1},"feedback":{"success":"Bravo!","collision":"Ostacolo."}}
            """,
            saved.DefinitionJson);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SaveExerciseDraftSuBozzaEsistente_UsaLaVersioneDichiarataEUnicaLaChiave()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();
        var first = await SaveAsync(exercise.Id, DefinitionJson());

        // Act
        var second = await SaveAsync(
            exercise.Id, DefinitionJson(title: "Raggiungi il sole"), first.RowVersion);

        // Assert
        Assert.Equal(first.RowVersion + 1, second.RowVersion);
        // La bozza è una sola per esercizio: la chiave è l'esercizio, non una riga nuova.
        Assert.Equal(second, await GetDraftAsync(exercise.Id));
        Assert.Contains(
            "\"title\":\"Raggiungi il sole\"", second.DefinitionJson, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SaveExerciseDraftSuBozzaEsistenteSenzaVersione_RifiutaConVersioneRichiesta()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();
        await SaveAsync(exercise.Id, DefinitionJson());

        // Act
        var failure = await _host.ExpectFailureAsync(
            SaveExerciseDraftCommand.RequestName, Payload(exercise.Id, DefinitionJson()));

        // Assert
        Assert.Equal(CodiErrorCodes.EntityVersionRequired, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SaveExerciseDraftConVersioneSuperata_RifiutaConConflitto()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();
        var first = await SaveAsync(exercise.Id, DefinitionJson());
        await SaveAsync(exercise.Id, DefinitionJson(title: "Raggiungi il sole"), first.RowVersion);

        // Act: la versione letta prima dell'ultima scrittura è superata.
        var failure = await _host.ExpectFailureAsync(
            SaveExerciseDraftCommand.RequestName,
            Payload(exercise.Id, DefinitionJson(title: "Raggiungi la luna"), first.RowVersion));

        // Assert
        Assert.Equal(CodiErrorCodes.RowVersionMismatch, failure.Code);
        // Il rifiuto non ha scritto: il documento resta quello dell'ultimo salvataggio riuscito.
        var draft = await GetDraftAsync(exercise.Id);
        Assert.Contains("\"title\":\"Raggiungi il sole\"", draft.DefinitionJson, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SaveExerciseDraftConVersioneSuBozzaInesistente_RifiutaConConflitto()
    {
        // Arrange: un chiamante che dichiara una versione crede che la bozza esista.
        var exercise = await CreateExerciseAsync();

        // Act
        var failure = await _host.ExpectFailureAsync(
            SaveExerciseDraftCommand.RequestName, Payload(exercise.Id, DefinitionJson(), baseRowVersion: 3));

        // Assert
        Assert.Equal(CodiErrorCodes.RowVersionMismatch, failure.Code);
        // Nessuna scrittura: la bozza continua a non esistere.
        var missing = await _host.ExpectQueryFailureAsync(
            GetExerciseDraftQuery.RequestName, new { exerciseId = exercise.Id });
        Assert.Equal(CodiErrorCodes.ResourceNotFound, missing.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SaveExerciseDraftSuEsercizioInesistente_RifiutaConRisorsaAssente()
    {
        // Act
        var failure = await _host.ExpectFailureAsync(
            SaveExerciseDraftCommand.RequestName, Payload(Guid.NewGuid(), DefinitionJson()));

        // Assert: la bozza appartiene a un esercizio: un identificativo sbagliato è un 404 e non
        // un errore di chiave esterna.
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SaveExerciseDraftConDefinizioneNonValida_RifiutaENonScriveNulla()
    {
        // Arrange: griglia 1×1, fuori dall'intervallo di §13.2.
        var exercise = await CreateExerciseAsync();
        var invalid = DefinitionJson().Replace("\"width\": 5", "\"width\": 1", StringComparison.Ordinal);

        // Act
        var failure = await _host.ExpectFailureAsync(
            SaveExerciseDraftCommand.RequestName, Payload(exercise.Id, invalid));

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        // Nessuna scrittura: la bozza non esiste, quindi l'editor riparte dalla definizione iniziale.
        var missing = await _host.ExpectQueryFailureAsync(
            GetExerciseDraftQuery.RequestName, new { exerciseId = exercise.Id });
        Assert.Equal(CodiErrorCodes.ResourceNotFound, missing.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SaveExerciseDraftConCompletamentoDiversoDalTraguardo_Rifiuta()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();
        var incoherent = DefinitionJson()
            .Replace("\"type\": \"ReachCell\", \"x\": 3, \"y\": 1", "\"type\": \"ReachCell\", \"x\": 4, \"y\": 3", StringComparison.Ordinal);

        // Act
        var failure = await _host.ExpectFailureAsync(
            SaveExerciseDraftCommand.RequestName, Payload(exercise.Id, incoherent));

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SaveExerciseDraftConDocumentoMalformato_RifiutaConPayloadNonValido()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();

        // Act
        var failure = await _host.ExpectFailureAsync(
            SaveExerciseDraftCommand.RequestName, Payload(exercise.Id, "{ non è JSON }"));

        // Assert
        Assert.Equal(CodiErrorCodes.InvalidPayload, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SaveExerciseDraftConProgettoNonOggetto_RifiutaConValidazione()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();

        // Act
        var failure = await _host.ExpectFailureAsync(
            SaveExerciseDraftCommand.RequestName,
            new
            {
                exerciseId = exercise.Id,
                definitionJson = DefinitionJson(),
                workspaceJson = "[1, 2, 3]",
            });

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SaveExerciseDraft_MemorizzaLaSoluzionePrivataSenzaEsporlaNelDto()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();
        var solution = """{"blocks":{"languageVersion":0,"blocks":[]}}""";

        // Act
        var saved = await SaveAsync(exercise.Id, DefinitionJson(), referenceSolutionJson: solution);

        // Assert: il DTO che l'editor carica non porta la soluzione (§13.1); la riga privata la
        // conserva, in una tabella separata, per il docente che la chiede con GetReferenceSolution.
        Assert.DoesNotContain("languageVersion", saved.DefinitionJson, StringComparison.Ordinal);

        var unitOfWork = _host.Services.GetRequiredService<IUnitOfWorkContext>();
        await using var work = await unitOfWork.CreateAsync();
        var stored = await work.Repo<IExerciseDraftRepository>()
            .GetAsync(exercise.Id.ToString("D"));

        Assert.Equal(solution, stored?.ReferenceSolutionJson);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task QueryAsync_GetExerciseDraftSenzaBozza_RifiutaConRisorsaAssente()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();

        // Act
        var failure = await _host.ExpectQueryFailureAsync(
            GetExerciseDraftQuery.RequestName, new { exerciseId = exercise.Id });

        // Assert: l'editor interpreta il 404 come «bozza da creare».
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SaveExerciseDraftSenzaUnaParteDellaDefinizione_RifiutaConValidazione()
    {
        // Arrange: manca del tutto la parte dei limiti.
        var exercise = await CreateExerciseAsync();
        var incomplete = DefinitionJson()
            .Replace(
                "\"limits\": { \"maxBlocks\": 30, \"maxNestingDepth\": 8, \"maxExecutionSteps\": 1000, \"maxActiveDurationMs\": 120000 },",
                string.Empty,
                StringComparison.Ordinal);

        // Act
        var failure = await _host.ExpectFailureAsync(
            SaveExerciseDraftCommand.RequestName, Payload(exercise.Id, incomplete));

        // Assert: il messaggio dice quale parte manca, invece di parlare di un valore nullo.
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Contains("manca", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SaveExerciseDraftConUnCampoCheQuestaVersioneNonConosce_RifiutaConPayloadNonValido()
    {
        // Arrange: un campo in più è un formato più nuovo, non un dettaglio da scartare (§13.3).
        var exercise = await CreateExerciseAsync();
        var future = DefinitionJson()
            .Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"sprite\": \"robot\",", StringComparison.Ordinal);

        // Act
        var failure = await _host.ExpectFailureAsync(
            SaveExerciseDraftCommand.RequestName, Payload(exercise.Id, future));

        // Assert
        Assert.Equal(CodiErrorCodes.InvalidPayload, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SaveExerciseDraftConOrientamentoSconosciuto_RifiutaConValidazione()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();
        var unknown = DefinitionJson()
            .Replace("\"direction\": \"East\"", "\"direction\": \"Up\"", StringComparison.Ordinal);

        // Act
        var failure = await _host.ExpectFailureAsync(
            SaveExerciseDraftCommand.RequestName, Payload(exercise.Id, unknown));

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SaveExerciseDraftConCondizioneSconosciuta_RifiutaConValidazione()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();
        var unknown = DefinitionJson()
            .Replace("\"type\": \"ReachCell\"", "\"type\": \"CollectAll\"", StringComparison.Ordinal);

        // Act
        var failure = await _host.ExpectFailureAsync(
            SaveExerciseDraftCommand.RequestName, Payload(exercise.Id, unknown));

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SaveExerciseDraftConDefinizioneOltreIlLimite_RifiutaConValidazione()
    {
        // Arrange: la consegna gonfiata porta il documento oltre i 64 KiB ammessi.
        var exercise = await CreateExerciseAsync();
        var oversized = DefinitionJson().Replace(
            "Porta il personaggio sulla stella usando i blocchi.",
            new string('a', ExerciseDefinitionJson.MaxDefinitionLength + 1),
            StringComparison.Ordinal);

        // Act
        var failure = await _host.ExpectFailureAsync(
            SaveExerciseDraftCommand.RequestName, Payload(exercise.Id, oversized));

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SaveExerciseDraftSenzaProgettoABlocchi_RifiutaConValidazione()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();

        // Act
        var failure = await _host.ExpectFailureAsync(
            SaveExerciseDraftCommand.RequestName,
            new
            {
                exerciseId = exercise.Id,
                definitionJson = DefinitionJson(),
                workspaceJson = string.Empty,
            });

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SaveExerciseDraftConProgettoOltreIlLimite_RifiutaConValidazione()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();

        // Act
        var failure = await _host.ExpectFailureAsync(
            SaveExerciseDraftCommand.RequestName,
            new
            {
                exerciseId = exercise.Id,
                definitionJson = DefinitionJson(),
                workspaceJson = new string('a', ExerciseWorkspaceJson.MaxLength + 1),
            });

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SaveExerciseDraftConSoluzioneDiRiferimentoMalformata_RifiutaConPayloadNonValido()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();

        // Act
        var failure = await _host.ExpectFailureAsync(
            SaveExerciseDraftCommand.RequestName,
            Payload(exercise.Id, DefinitionJson(), referenceSolutionJson: "{ non è JSON }"));

        // Assert
        Assert.Equal(CodiErrorCodes.InvalidPayload, failure.Code);
    }

    private static object Payload(
        Guid exerciseId,
        string definitionJson,
        long? baseRowVersion = null,
        string? referenceSolutionJson = null)
    {
        // `baseRowVersion` assente quando è nullo: è il caso della creazione, e il dispatcher
        // distingue «non dichiarata» da «dichiarata nulla».
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["exerciseId"] = exerciseId,
            ["definitionJson"] = definitionJson,
            ["workspaceJson"] = Workspace,
            ["referenceSolutionJson"] = referenceSolutionJson,
        };

        if (baseRowVersion is not null)
        {
            payload["baseRowVersion"] = baseRowVersion.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return payload;
    }

    private async Task<ExerciseDto> CreateExerciseAsync()
    {
        var exercise = await _host.CommandAsync<ExerciseDto>(
            CreateExerciseCommand.RequestName, new { exerciseType = "GridPath", title = "Raggiungi la stella" });

        return exercise;
    }

    private Task<ExerciseDraftDto> SaveAsync(
        Guid exerciseId,
        string definitionJson,
        long? baseRowVersion = null,
        string? referenceSolutionJson = null) =>
        _host.CommandAsync<ExerciseDraftDto>(
            SaveExerciseDraftCommand.RequestName,
            Payload(exerciseId, definitionJson, baseRowVersion, referenceSolutionJson));

    private Task<ExerciseDraftDto> GetDraftAsync(Guid exerciseId) =>
        _host.QueryOneAsync<ExerciseDraftDto>(
            GetExerciseDraftQuery.RequestName, new { exerciseId });

    /// <summary>Definizione di §13.2, con i due soli valori che i casi cambiano.</summary>
    private static string DefinitionJson(string title = "Raggiungi la stella") =>
        $$"""
        {
          "schemaVersion": 1,
          "exerciseType": "GridPath",
          "engineVersion": "gridpath-1",
          "title": "{{title}}",
          "instructions": "Porta il personaggio sulla stella usando i blocchi.",
          "scene": {
            "width": 5,
            "height": 4,
            "start": { "x": 0, "y": 1, "direction": "East" },
            "obstacles": [{ "x": 2, "y": 2 }],
            "goal": { "x": 3, "y": 1 }
          },
          "toolbox": { "allowedBlocks": ["aula_start", "aula_move", "aula_turn", "aula_repeat"] },
          "limits": { "maxBlocks": 30, "maxNestingDepth": 8, "maxExecutionSteps": 1000, "maxActiveDurationMs": 120000 },
          "completion": { "type": "ReachCell", "x": 3, "y": 1 },
          "feedback": { "success": "Hai raggiunto la stella!", "collision": "C'è un ostacolo: prova a cambiare percorso." }
        }
        """;
}
