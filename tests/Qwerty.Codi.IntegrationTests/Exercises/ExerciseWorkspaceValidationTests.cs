using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Exercises;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Exercises;

/// <summary>
/// Verifica del progetto a blocchi al salvataggio della bozza (§13.3, §13.4) attraverso il percorso
/// reale: registro, canale, policy, deserializzazione, mediator, handler e database migrato.
///
/// Il progetto è quello che produce Blockly: il contenitore <c>blocks</c> con la versione del
/// formato e l'elenco dei blocchi, la catena in <c>next</c> e la sequenza ripetuta in
/// <c>inputs.DO</c>. Questi casi fissano ciò che il server accetta e ciò che rifiuta, perché è la
/// verifica che rende attendibile un progetto conservato senza interpretarlo.
/// </summary>
public sealed class ExerciseWorkspaceValidationTests : IAsyncLifetime
{
    /// <summary>
    /// Serializzazione **reale** prodotta da Blockly 13.3 per il catalogo `gridpath-1`: i quattro
    /// blocchi collegati fra loro — ingresso, ripetizione con il passo dentro, rotazione.
    ///
    /// Gli identificativi sono quelli generati da Blockly e contengono caratteri arbitrari; il
    /// documento contiene anche `x` e `y`, che Blockly aggiunge ai blocchi appoggiati. È il caso che
    /// dimostra che la verifica del server accetta ciò che l'editor produce davvero, invece di
    /// accettare soltanto una forma scritta a mano per assomigliargli. Lo stesso documento è
    /// confrontato con le definizioni dei blocchi da `grid-path-workspace.spec.ts`.
    /// </summary>
    private const string BlocklyWorkspace =
        "{\"blocks\":{\"languageVersion\":0,\"blocks\":[{\"type\":\"aula_start\",\"id\":\"A2Vjnu*gTSRXY_a}KBm@\",\"x\":0,\"y\":0,\"next\":{\"block\":{\"type\":\"aula_repeat\",\"id\":\"]USR4@$3X-Eo(cQZ{jE.\",\"fields\":{\"COUNT\":3},\"inputs\":{\"DO\":{\"block\":{\"type\":\"aula_move\",\"id\":\"p;M4QW)L=lSZGC?gv[jg\"}}},\"next\":{\"block\":{\"type\":\"aula_turn\",\"id\":\"nEDrTeX)ODU?vf`PiJX_\",\"fields\":{\"TURN\":\"Left\"}}}}}}]}}";

    private CodiUseCaseHost _host = null!;

    public async Task InitializeAsync() => _host = await CodiUseCaseHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConProgettoCompostoDaBlockly_AccettaEConservaIlProgetto()
    {
        // Arrange: è il documento che l'editor invia quando il docente compone il programma.
        var exercise = await CreateExerciseAsync();

        // Act
        var saved = await SaveAsync(exercise.Id, DefinitionJson(), BlocklyWorkspace);

        // Assert: il progetto è conservato com'è, perché il formato è di Blockly e riscriverlo
        // significherebbe interpretarlo.
        Assert.Equal(BlocklyWorkspace, saved.WorkspaceJson);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConProgettoEseguibile_ConservaIlProgettoRicevuto()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();
        var program = Program(
            Start("start"),
            Move("move-1"),
            Turn("turn-1"),
            Repeat("repeat-1", 3, [Move("move-2")]));

        // Act
        var saved = await SaveAsync(exercise.Id, DefinitionJson(), program);

        // Assert: il progetto è conservato come è stato ricevuto, non riscritto: il formato è di
        // Blockly e riscriverlo significherebbe interpretarlo.
        Assert.Equal(program, saved.WorkspaceJson);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConProgettoVuoto_AccettaLaBozzaIncompleta()
    {
        // Arrange: la bozza può esistere prima che il docente abbia composto il programma.
        var exercise = await CreateExerciseAsync();

        // Act
        var saved = await SaveAsync(exercise.Id, DefinitionJson(), "{}");

        // Assert
        Assert.Equal("{}", saved.WorkspaceJson);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SenzaPuntoDiIngresso_AccettaIlSalvataggio()
    {
        // Arrange: §13.4 dice che la mancanza di `aula_start` impedisce la prova, non il salvataggio.
        var exercise = await CreateExerciseAsync();
        var program = Program(Move("move-1"));

        // Act
        var saved = await SaveAsync(exercise.Id, DefinitionJson(), program);

        // Assert
        Assert.Equal(program, saved.WorkspaceJson);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConBloccoCheLaVersioneNonConosce_Rifiuta()
    {
        // Arrange: un blocco ignoto è un formato che non sappiamo eseguire, non un dettaglio da
        // scartare (§13.3).
        var exercise = await CreateExerciseAsync();
        var program = Program(Start("start"), """{"type":"aula_jump","id":"jump-1"}""");

        // Act
        var failure = await ExpectRejectedAsync(exercise.Id, DefinitionJson(), program);

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Contains("aula_jump", failure.Message, StringComparison.Ordinal);
        await AssertNoDraftAsync(exercise.Id);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConBloccoNonMessoADisposizione_Rifiuta()
    {
        // Arrange: la definizione ammette solo l'ingresso e il passo, ma il progetto gira.
        var exercise = await CreateExerciseAsync();
        var program = Program(Start("start"), Turn("turn-1"));

        // Act
        var failure = await ExpectRejectedAsync(
            exercise.Id,
            DefinitionJson("""["aula_start","aula_move"]"""),
            program);

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Contains("non mette a disposizione", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConDuePuntiDiIngresso_Rifiuta()
    {
        // Arrange: il punto di ingresso è uno solo (§13.4).
        var exercise = await CreateExerciseAsync();
        var program = Program(Start("start-1"), Start("start-2"));

        // Act
        var failure = await ExpectRejectedAsync(exercise.Id, DefinitionJson(), program);

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Contains("uno solo", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConVersoDiRotazioneNonAmmesso_Rifiuta()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();
        var program = Program(Start("start"), """{"type":"aula_turn","id":"turn-1","fields":{"TURN":"Up"}}""");

        // Act
        var failure = await ExpectRejectedAsync(exercise.Id, DefinitionJson(), program);

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Contains("TURN", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConCampoDiRotazioneMancante_Rifiuta()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();
        var program = Program(Start("start"), """{"type":"aula_turn","id":"turn-1"}""");

        // Act
        var failure = await ExpectRejectedAsync(exercise.Id, DefinitionJson(), program);

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Contains("richiede il campo", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConRipetizioneFuoriIntervallo_Rifiuta()
    {
        // Arrange: il conteggio ammesso va da 1 a 20 (§13.4).
        var exercise = await CreateExerciseAsync();
        var program = Program(Start("start"), Repeat("repeat-1", 21, [Move("move-1")]));

        // Act
        var failure = await ExpectRejectedAsync(exercise.Id, DefinitionJson(), program);

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Contains("COUNT", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConCampoSuUnBloccoCheNonNeHa_Rifiuta()
    {
        // Arrange: `aula_move` è un passo, non ha un verso da dichiarare.
        var exercise = await CreateExerciseAsync();
        var program = Program(
            Start("start"),
            """{"type":"aula_move","id":"move-1","fields":{"DIRECTION":"avanti"}}""");

        // Act
        var failure = await ExpectRejectedAsync(exercise.Id, DefinitionJson(), program);

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Contains("non ha il campo", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConIngressoNonPrevisto_Rifiuta()
    {
        // Arrange: solo la ripetizione ha una sequenza interna.
        var exercise = await CreateExerciseAsync();
        var program = Program(
            Start("start"),
            """{"type":"aula_move","id":"move-1","inputs":{"DO":{"block":{"type":"aula_move","id":"move-2"}}}}""");

        // Act
        var failure = await ExpectRejectedAsync(exercise.Id, DefinitionJson(), program);

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Contains("DO", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConIngressoVuoto_Rifiuta()
    {
        // Arrange: un ingresso presente contiene un blocco, altrimenti è un collegamento incompleto.
        var exercise = await CreateExerciseAsync();
        var program = Program(
            Start("start"),
            """{"type":"aula_repeat","id":"repeat-1","fields":{"COUNT":2},"inputs":{"DO":{}}}""");

        // Act
        var failure = await ExpectRejectedAsync(exercise.Id, DefinitionJson(), program);

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Contains("non contiene un blocco", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConOmbraAlPostoDiUnBlocco_Rifiuta()
    {
        // Arrange: i blocchi di questa versione non definiscono ombre.
        var exercise = await CreateExerciseAsync();
        var program = Program(
            Start("start"),
            """{"type":"aula_repeat","id":"repeat-1","fields":{"COUNT":2},"inputs":{"DO":{"shadow":{"type":"aula_move","id":"move-1"}}}}""");

        // Act
        var failure = await ExpectRejectedAsync(exercise.Id, DefinitionJson(), program);

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Contains("ombra", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConOltreIlNumeroMassimoDiBlocchi_Rifiuta()
    {
        // Arrange: anche i blocchi scollegati contano (§13.4), altrimenti il workspace sarebbe
        // illimitato.
        var exercise = await CreateExerciseAsync();
        var blocks = new List<string> { Start("start") };
        blocks.AddRange(Enumerable.Range(0, 30).Select(index => Move(
            "move-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        var program = Program([.. blocks]);

        // Act
        var failure = await ExpectRejectedAsync(exercise.Id, DefinitionJson(), program);

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Contains("blocchi", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConAnnidamentoOltreIlLimite_Rifiuta()
    {
        // Arrange: limite di annidamento 2 e tre livelli di sequenze ripetute.
        var exercise = await CreateExerciseAsync();
        var program = Program(
            Start("start"),
            Repeat("repeat-1", 2, [Repeat("repeat-2", 2, [Repeat("repeat-3", 2, [Move("move-1")])])]));

        // Act
        var failure = await ExpectRejectedAsync(exercise.Id, DefinitionJson(maxNestingDepth: 2), program);

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Contains("annida", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConIdentificativiDuplicati_Rifiuta()
    {
        // Arrange
        var exercise = await CreateExerciseAsync();
        var program = Program(Start("start"), Move("move-1"), Move("move-1"));

        // Act
        var failure = await ExpectRejectedAsync(exercise.Id, DefinitionJson(), program);

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Contains("identificativo", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConProgettoNonInterpretabile_RifiutaConValidazione()
    {
        // Arrange: il contenitore dei blocchi non è un elenco.
        var exercise = await CreateExerciseAsync();
        var program = """{"blocks":{"languageVersion":0,"blocks":{"type":"aula_move"}}}""";

        // Act
        var failure = await ExpectRejectedAsync(exercise.Id, DefinitionJson(), program);

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("[]")]
    [InlineData("""{"blocks":[]}""")]
    [InlineData("""{"blocks":{"languageVersion":0,"blocks":[1]}}""")]
    [InlineData("""{"blocks":{"languageVersion":0,"blocks":[{"id":"senza-tipo"}]}}""")]
    [InlineData("""{"blocks":{"languageVersion":0,"blocks":[{"type":"aula_move","fields":[]}]}}""")]
    [InlineData("""{"blocks":{"languageVersion":0,"blocks":[{"type":"aula_repeat","inputs":[]}]}}""")]
    [InlineData("""{"blocks":{"languageVersion":0,"blocks":[{"type":"aula_move","next":[]}]}}""")]
    [InlineData("""{"blocks":{"languageVersion":0,"blocks":[{"type":"aula_move","next":{}}]}}""")]
    public async Task ExecuteAsync_ConStrutturaCheNonHaLaFormaDelProgetto_RifiutaConValidazione(string program)
    {
        // Arrange: il progetto è JSON valido ma non ha la forma che Blockly produce. Rifiutarlo è
        // meglio che conservarlo e scoprirlo al momento della prova.
        var exercise = await CreateExerciseAsync();

        // Act
        var failure = await ExpectRejectedAsync(exercise.Id, DefinitionJson(), program);

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("""{"type":"aula_repeat","id":"repeat-1","fields":{"COUNT":2,"EXTRA":1}}""")]
    [InlineData("""{"type":"aula_turn","id":"turn-1","fields":{"TURN":3}}""")]
    [InlineData("""{"type":"aula_repeat","id":"repeat-1","fields":{"COUNT":"tre"}}""")]
    [InlineData("""{"type":"aula_start","id":"start-1","fields":{"TURN":null}}""")]
    public async Task ExecuteAsync_ConCampiNonAmmessi_RifiutaConValidazione(string block)
    {
        // Arrange: i campi sbagliati mostrano il valore ricevuto, non solo che «qualcosa» non va.
        var exercise = await CreateExerciseAsync();

        // Act
        var failure = await ExpectRejectedAsync(exercise.Id, DefinitionJson(), Program(Start("s"), block));

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConSoluzioneDiRiferimentoNonEseguibile_Rifiuta()
    {
        // Arrange: la soluzione privata è un progetto e passa dalle stesse regole del progetto
        // iniziale, altrimenti il docente si troverebbe una soluzione che il motore non esegue.
        var exercise = await CreateExerciseAsync();
        var solution = Program(Start("start"), """{"type":"aula_jump","id":"jump-1"}""");

        // Act
        var failure = await _host.ExpectFailureAsync(
            SaveExerciseDraftCommand.RequestName,
            Payload(exercise.Id, DefinitionJson(), "{}", solution));

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Contains("soluzione di riferimento", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Salva e si aspetta un rifiuto, verificando che nulla sia stato scritto.</summary>
    private async Task<CodiRequestException> ExpectRejectedAsync(Guid exerciseId, string definitionJson, string program)
    {
        var failure = await _host.ExpectFailureAsync(
            SaveExerciseDraftCommand.RequestName,
            Payload(exerciseId, definitionJson, program));

        await AssertNoDraftAsync(exerciseId);

        return failure;
    }

    private async Task AssertNoDraftAsync(Guid exerciseId)
    {
        var missing = await _host.ExpectQueryFailureAsync(
            GetExerciseDraftQuery.RequestName, new { exerciseId });

        Assert.Equal(CodiErrorCodes.ResourceNotFound, missing.Code);
    }

    private static object Payload(
        Guid exerciseId,
        string definitionJson,
        string workspaceJson,
        string? referenceSolutionJson = null) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["exerciseId"] = exerciseId,
            ["definitionJson"] = definitionJson,
            ["workspaceJson"] = workspaceJson,
            ["referenceSolutionJson"] = referenceSolutionJson,
        };

    private async Task<ExerciseDto> CreateExerciseAsync() =>
        await _host.CommandAsync<ExerciseDto>(
            CreateExerciseCommand.RequestName,
            new { exerciseType = "GridPath", title = "Raggiungi la stella" });

    private Task<ExerciseDraftDto> SaveAsync(Guid exerciseId, string definitionJson, string workspaceJson) =>
        _host.CommandAsync<ExerciseDraftDto>(
            SaveExerciseDraftCommand.RequestName,
            Payload(exerciseId, definitionJson, workspaceJson));

    /// <summary>Definizione di §13.2, con i soli valori che i casi cambiano.</summary>
    private static string DefinitionJson(
        string allowedBlocks = """["aula_start","aula_move","aula_turn","aula_repeat"]""",
        int maxBlocks = 30,
        int maxNestingDepth = 8) =>
        $$"""
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
          "toolbox": { "allowedBlocks": {{allowedBlocks}} },
          "limits": { "maxBlocks": {{maxBlocks}}, "maxNestingDepth": {{maxNestingDepth}}, "maxExecutionSteps": 1000, "maxActiveDurationMs": 120000 },
          "completion": { "type": "ReachCell", "x": 3, "y": 1 },
          "feedback": { "success": "Hai raggiunto la stella!", "collision": "C'è un ostacolo: prova a cambiare percorso." }
        }
        """;

    /// <summary>Progetto a blocchi con i blocchi indicati, concatenati in quest'ordine.</summary>
    private static string Program(params string[] blocks)
    {
        string? tail = null;
        for (var index = blocks.Length - 1; index >= 0; index--)
        {
            tail = tail is null ? blocks[index] : Insert(blocks[index], tail);
        }

        return Workspace($"[{tail}]");
    }

    /// <summary>Inserisce la catena verso il blocco successivo, prima della parentesi di chiusura.</summary>
    private static string Insert(string block, string next) =>
        $"{block[..^1]},\"next\":{{\"block\":{next}}}}}";

    private static string Workspace(string blocks) =>
        "{\"blocks\":{\"languageVersion\":0,\"blocks\":" + blocks + "}}";

    private static string Start(string id) => Block("aula_start", id);

    private static string Move(string id) => Block("aula_move", id);

    private static string Turn(string id) =>
        $"{Block("aula_turn", id)[..^1]},\"fields\":{{\"TURN\":\"Left\"}}}}";

    private static string Repeat(string id, int count, string[] body) =>
        $"{Block("aula_repeat", id)[..^1]},\"fields\":{{\"COUNT\":{count.ToString(System.Globalization.CultureInfo.InvariantCulture)}}},\"inputs\":{{\"DO\":{{\"block\":{body[0]}}}}}}}";

    private static string Block(string type, string id) =>
        $"{{\"type\":\"{type}\",\"id\":\"{id}\"}}";
}
