using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Exercises;
using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.Domain.Exercises;
using Qwerty.Codi.IntegrationTests.Realtime;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Sessions;

/// <summary>
/// Percorso completo di un esercizio che **conserva valori** (§13.9): il docente lo compone con le
/// variabili e i casi di prova, l'alunno compone il programma a blocchi, lo salva e legge dal server
/// l'esito della correzione.
///
/// È il caso che dimostra perché questo motore non è un percorso travestito: il lavoro del tentativo
/// è un programma che conserva valori, la correzione è un'**esecuzione** sul server, e il docente non
/// scrive la soluzione — scrive che cosa il programma deve produrre.
/// </summary>
public sealed class ValueProgramUseCaseTests : IAsyncLifetime
{
    private OutboxScenario scenario = null!;
    private Seed seed = null!;
    private JoinSessionOutcome joined = null!;
    private ParticipantSnapshotDto snapshot = null!;
    private AttemptDto attempt = null!;

    private CodiRequestContext Context => new(
        isAuthenticated: true,
        [CodiPolicy.AdmittedParticipant, CodiPolicy.ActiveParticipantConnection],
        joined.Joined.CredentialSessionId,
        participantId: joined.Joined.ParticipantId);

    public async Task InitializeAsync()
    {
        scenario = await OutboxScenario.CreateAsync();
        seed = await scenario.StartedSessionAsync();
        joined = await scenario.JoinAsync(seed, "Luca");

        var exercise = await scenario.Host.CommandAsAsync<ExerciseDto>(
            CreateExerciseCommand.RequestName,
            new { title = "Somma uno", exerciseType = "ValueProgram" },
            scenario.RegiaContext());
        var draft = await scenario.Host.CommandAsAsync<ExerciseDraftDto>(
            SaveExerciseDraftCommand.RequestName,
            new
            {
                exerciseId = exercise.Id,
                definitionJson = Definition(),
                workspaceJson = EmptyWorkspace(),
            },
            scenario.RegiaContext());
        var version = await scenario.Host.CommandAsAsync<ExerciseVersionDto>(
            PublishExerciseVersionCommand.RequestName,
            new { exerciseId = exercise.Id, baseRowVersion = draft.RowVersion },
            scenario.RegiaContext());

        await AssignAsync(version.Id);
        await SynchronizeAsync();
        attempt = await scenario.Host.QueryOneAsAsync<AttemptDto>(
            GetAttemptQuery.RequestName, new { }, Context);
    }

    public async Task DisposeAsync() => await scenario.DisposeAsync();

    [Fact]
    public async Task Assegnato_IlLavoroInizialeEUnProgettoVuoto()
    {
        var exercise = await scenario.Host.QueryOneAsAsync<AssignedExerciseDto>(
            GetAssignedExerciseQuery.RequestName, new { }, Context);

        Assert.Equal(ValueProgramEngine.ExerciseType, exercise.ExerciseType);
        Assert.Equal(ValueProgramEngine.Version, exercise.EngineVersion);
        Assert.Equal(EmptyWorkspace(), attempt.WorkspaceJson);
        // I casi di prova escono con la definizione: l'alunno deve sapere che cosa il programma deve
        // produrre, e la correzione è l'esecuzione, non un segreto da nascondere.
        Assert.Contains("\"testCases\"", exercise.DefinitionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProgrammaCorretto_IlServerLoEsegueESuperaICasi()
    {
        // «chiedi Numero», «porta Punti a Numero + 1», «mostra Punti»: il programma giusto.
        await SaveProgramAsync(SommaUno(), revision: 2);

        var outcome = await OutcomeAsync();

        Assert.True(outcome.Passed);
        Assert.Equal("Bravo!", outcome.SuccessMessage);
        Assert.Equal(string.Empty, outcome.Problem);
    }

    [Fact]
    public async Task ProgrammaCheSommaDue_IlServerLoEsegueERaccontaIlCasoFallito()
    {
        // Lo stesso programma con «+ 2» invece di «+ 1»: è l'errore che un bambino fa davvero, e la
        // correzione deve dirgli che cosa il programma ha mostrato e che cosa doveva mostrare.
        await SaveProgramAsync(SommaUno().Replace("\"NUM\":1", "\"NUM\":2", StringComparison.Ordinal), revision: 2);

        var outcome = await OutcomeAsync();

        Assert.False(outcome.Passed);
        Assert.Equal("Riprova.", outcome.WrongMessage);
        Assert.Contains("mostra", outcome.Problem, StringComparison.Ordinal);
        Assert.Contains("invece di", outcome.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProgrammaConUnaVariabileNonDichiarata_LaDiagnosiNominaIlBlocco()
    {
        await SaveProgramAsync(
            SommaUno().Replace("\"VAR\":\"Punti\"", "\"VAR\":\"Sconosciuta\"", StringComparison.Ordinal),
            revision: 2);

        var outcome = await OutcomeAsync();

        Assert.False(outcome.Passed);
        // Un programma che non si esegue non è un programma sbagliato: la diagnosi dice che cosa
        // correggere, e non fa cercare l'errore nel posto sbagliato.
        Assert.Contains("Sconosciuta", outcome.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnProgettoDiUnAltroTipo_LaDiagnosiDiceCheIlProgrammaNonSiLegge()
    {
        // Il verbale del tipo dichiarativo non è un progetto a blocchi: la diagnosi lo dice come
        // problema del programma, e non come un esito negativo che farebbe cercare l'errore
        // nell'esecuzione invece che nella forma.
        await SaveProgramAsync("{\"answer\":\"6\"}", revision: 2);

        var outcome = await OutcomeAsync();

        Assert.False(outcome.Passed);
        Assert.Contains("quando si avvia", outcome.Problem, StringComparison.Ordinal);
    }

    private async Task SaveProgramAsync(string program, long revision)
    {
        var request = new SaveAttemptWorkspaceCommand(
            attempt.AttemptId, Guid.NewGuid(), snapshot.ConnectionEpoch, attempt.RowVersion, revision,
            program, ExerciseContentHash.ComputeWorkspace(program));

        await scenario.Host.CommandAsAsync<AttemptSaveDto>(
            SaveAttemptWorkspaceCommand.RequestName, request, Context);
    }

    private Task<ValueProgramOutcomeDto> OutcomeAsync() =>
        scenario.Host.QueryOneAsAsync<ValueProgramOutcomeDto>(
            GetValueProgramOutcomeQuery.RequestName, new { }, Context);

    private async Task AssignAsync(Guid versionId) =>
        await scenario.Host.ExecuteAsAsync(
            CodiRequestKind.Command, AssignExerciseCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await scenario.ScalarAsync<long>(
                    "SELECT session_revision FROM lesson_session;"),
                exerciseVersionId = versionId,
            },
            scenario.RegiaContext());

    private async Task SynchronizeAsync()
    {
        snapshot = await scenario.Host.CommandAsAsync<ParticipantSnapshotDto>(
            InitializeParticipantConnectionCommand.RequestName,
            new
            {
                protocolVersion = 1,
                clientInstanceId = Guid.NewGuid(),
                appVersion = "test",
                engineVersion = ValueProgramEngine.Version,
            },
            Context);
        var registry = scenario.Host.Services.GetRequiredService<IParticipantConnectionRegistry>();
        registry.Acquire(
            new(
                joined.Joined.ParticipantId, seed.SessionId, Guid.NewGuid(), "client",
                snapshot.ConnectionEpoch, snapshot.EffectiveRevision),
            CodiUseCaseHost.Now);
        registry.MarkSnapshotApplied(
            new(
                joined.Joined.ParticipantId, "client", snapshot.ConnectionEpoch,
                snapshot.EffectiveRevision),
            CodiUseCaseHost.Now);
    }

    /// <summary>Definizione: due variabili e due casi di prova che chiedono un valore e ne mostrano uno in più.</summary>
    private static string Definition() =>
        """
        {
          "schemaVersion": 1,
          "exerciseType": "ValueProgram",
          "engineVersion": "valueprogram-1",
          "title": "Somma uno",
          "instructions": "Leggi un numero, somma uno e mostra il risultato.",
          "variables": [{ "name": "Numero", "initialValue": 0 }, { "name": "Punti", "initialValue": 0 }],
          "testCases": [
            { "inputs": [6], "expectedOutput": ["7"] },
            { "inputs": [3], "expectedOutput": ["4"] }
          ],
          "toolbox": { "allowedBlocks": ["val_start","val_set","val_change","val_show","val_ask","val_if","val_repeat","val_number","val_variable","val_answer","val_add","val_compare"] },
          "limits": { "maxBlocks": 40, "maxNestingDepth": 8, "maxExecutionSteps": 1000, "maxActiveDurationMs": 120000 },
          "feedback": { "success": "Bravo!", "collision": "Riprova." }
        }
        """;

    private static string EmptyWorkspace() =>
        "{\"blocks\":{\"languageVersion\":0,\"blocks\":[]}}";

    /// <summary>
    /// «chiedi [Quanto vale?] e attendi», «porta Numero a (risposta)», «porta Punti a Numero + 1»,
    /// «dire (Punti)»: il vocabolario del corso, dove la domanda è un testo e la risposta è un
    /// reporter che si inserisce nei fori dei blocchi.
    /// </summary>
    private static string SommaUno() =>
        new JsonObject
        {
            ["blocks"] = new JsonObject
            {
                ["languageVersion"] = 0,
                ["blocks"] = new JsonArray(Start(Chain(
                    Ask("ask", "Quanto vale?"),
                    Set("memo", "Numero", Answer("risposta")),
                    Set("set", "Punti", Add(Variable("v", "Numero"), Number("n", 1))),
                    Show("show", Variable("vend", "Punti"))))),
            },
        }.ToJsonString();

    // ---------------------------------------------------------------- composizione del documento

    private static JsonObject Connection(JsonObject block) => new() { ["block"] = block };

    private static JsonObject Block(string type, string id) => new() { ["type"] = type, ["id"] = id };

    private static JsonObject Chain(params JsonObject[] blocks)
    {
        for (var index = blocks.Length - 2; index >= 0; index--)
        {
            blocks[index]["next"] = Connection(blocks[index + 1]);
        }

        return blocks[0];
    }

    /// <summary>Il blocco di avvio con la catena appesa: è la forma che Blockly produce.</summary>
    private static JsonObject Start(JsonObject body)
    {
        var start = Block("val_start", "s");
        start["next"] = Connection(body);

        return start;
    }

    private static JsonObject Field(JsonObject block, string name, JsonNode value)
    {
        block["fields"] = new JsonObject { [name] = value };

        return block;
    }

    private static JsonObject Input(JsonObject block, string name, JsonObject inner)
    {
        var inputs = block["inputs"] as JsonObject ?? new JsonObject();
        inputs[name] = Connection(inner);
        block["inputs"] = inputs;

        return block;
    }

    private static JsonObject Number(string id, int value) =>
        Field(Block("val_number", id), "NUM", JsonValue.Create(value));

    private static JsonObject Variable(string id, string name) =>
        Field(Block("val_variable", id), "VAR", JsonValue.Create(name));

    private static JsonObject Add(JsonObject left, JsonObject right) =>
        Input(Input(Block("val_add", "add"), "VALUE", left), "OTHER", right);

    private static JsonObject Set(string id, string variable, JsonObject value) =>
        Input(Field(Block("val_set", id), "VAR", JsonValue.Create(variable)), "VALUE", value);

    private static JsonObject Show(string id, JsonObject value) =>
        Input(Block("val_show", id), "VALUE", value);

    /** «chiedi [domanda?] e attendi»: la domanda è il testo che il bambino legge. */
    private static JsonObject Ask(string id, string question) =>
        Field(Block("val_ask", id), "QUESTION", JsonValue.Create(question));

    /** Il reporter «risposta»: non ha campi, perché la risposta non si sceglie. */
    private static JsonObject Answer(string id) => Block("val_answer", id);
}
