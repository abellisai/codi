using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Exercises;
using Qwerty.Codi.Domain.Exercises;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Exercises;

/// <summary>
/// Verifica del progetto di un esercizio che conserva valori (§13.9), attraverso il percorso reale:
/// registro, canale, policy, deserializzazione, mediator, handler e database migrato.
///
/// Questi casi difendono la novità di questo motore, che nessun altro tipo ha: le espressioni si
/// **annidano** dentro i comandi. Un progetto in cui un'espressione sta in catena, o un comando sta
/// dentro un valore, è un programma che non si sa eseguire — e va rifiutato al salvataggio, non
/// scoperto davanti a un bambino.
/// </summary>
public sealed class ValueProgramWorkspaceValidationTests : IAsyncLifetime
{
    private CodiUseCaseHost _host = null!;

    public async Task InitializeAsync() => _host = await CodiUseCaseHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConEspressioneAnnidata_AccettaEConservaIlProgetto()
    {
        // «porta Punti a (valore di Punti) + 1»: il comando contiene un'addizione, che contiene a sua
        // volta due espressioni. È la profondità che il percorso non ha.
        var exercise = await CreateExerciseAsync();
        var programma = Workspace(
            $"[{Start("s")},{Set("set", "Punti", Add("add", Variable("v"), Number("n", 1)))}]");

        var salvato = await SaveAsync(exercise.Id, DefinitionJson(), programma);

        Assert.Equal(programma, salvato.WorkspaceJson);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConConfrontoNelSe_Accetta()
    {
        // «se (valore di Numero) > 5 allora mostra ... altrimenti mostra ...»: il confronto è
        // un'espressione dentro un ingresso di valore, e i due rami sono sequenze di comandi.
        var exercise = await CreateExerciseAsync();
        var programma = Workspace($"[{Start("s")},{If("if", Compare("cmp", Variable("v"), 5), Show("t", Number("n1", 1)), Show("e", Number("n2", 0)))}]");

        var salvato = await SaveAsync(exercise.Id, DefinitionJson(), programma);

        Assert.Equal(programma, salvato.WorkspaceJson);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConUnComandoDentroUnValore_Rifiuta()
    {
        // «mostra (fai un passo)» non vuol dire nulla: un comando non produce un valore.
        var exercise = await CreateExerciseAsync();
        var programma = Workspace($"[{Start("s")},{ShowRawValue("show", Block("val_start", "dentro"))}]");

        var eccezione = await Assert.ThrowsAsync<CodiRequestException>(
            () => SaveAsync(exercise.Id, DefinitionJson(), programma));

        Assert.Contains("serve un valore da calcolare", eccezione.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConUnEspressioneInCatena_Rifiuta()
    {
        // Un numero appoggiato sotto il punto di ingresso non è un'istruzione: non si esegue.
        var exercise = await CreateExerciseAsync();
        var programma = Workspace($"[{StartWithNext("s", Number("n", 3))}]");

        var eccezione = await Assert.ThrowsAsync<CodiRequestException>(
            () => SaveAsync(exercise.Id, DefinitionJson(), programma));

        Assert.Contains("serve un comando da eseguire", eccezione.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConUnaVariabileNonDichiarata_RestaUnProgettoValido()
    {
        // La definizione dichiara le variabili; il nome scritto in un blocco è un valore del campo.
        // Il progetto è formalmente valido: il motore rifiuterà l'esecuzione con una diagnosi, ed è
        // l'alunno a correggere il nome, non il server a rifiutare il salvataggio.
        var exercise = await CreateExerciseAsync();
        var programma = Workspace($"[{Start("s")},{Set("set", "Sconosciuta", Number("n", 1))}]");

        var salvato = await SaveAsync(exercise.Id, DefinitionJson(), programma);

        Assert.Equal(programma, salvato.WorkspaceJson);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConUnIngressoInventato_Rifiuta()
    {
        var exercise = await CreateExerciseAsync();
        var programma = Workspace($"[{Start("s")},{BlockWithExtraInput("show")}]");

        var eccezione = await Assert.ThrowsAsync<CodiRequestException>(
            () => SaveAsync(exercise.Id, DefinitionJson(), programma));

        Assert.Contains("non ha l'ingresso", eccezione.Message, StringComparison.Ordinal);
    }

    private async Task<ExerciseDto> CreateExerciseAsync() =>
        await _host.CommandAsync<ExerciseDto>(
            CreateExerciseCommand.RequestName,
            new { exerciseType = "ValueProgram", title = "Quanto fa il programma" });

    private Task<ExerciseDraftDto> SaveAsync(Guid exerciseId, string definitionJson, string workspaceJson) =>
        _host.CommandAsync<ExerciseDraftDto>(
            SaveExerciseDraftCommand.RequestName,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["exerciseId"] = exerciseId,
                ["definitionJson"] = definitionJson,
                ["workspaceJson"] = workspaceJson,
            });

    /// <summary>Definizione di un esercizio che conserva valori: due variabili e due casi di prova.</summary>
    private static string DefinitionJson() =>
        """
        {
          "schemaVersion": 1,
          "exerciseType": "ValueProgram",
          "engineVersion": "valueprogram-1",
          "title": "Quanto fa il programma",
          "instructions": "Leggi un numero, somma uno ai punti e mostra il risultato.",
          "variables": [{ "name": "Numero", "initialValue": 0 }, { "name": "Punti", "initialValue": 0 }],
          "testCases": [
            { "inputs": [6], "expectedOutput": ["7"] },
            { "inputs": [3], "expectedOutput": ["4"] }
          ],
          "toolbox": { "allowedBlocks": ["val_start","val_set","val_show","val_ask","val_if","val_repeat","val_number","val_variable","val_add","val_compare"] },
          "limits": { "maxBlocks": 40, "maxNestingDepth": 8, "maxExecutionSteps": 500, "maxActiveDurationMs": 120000 },
          "feedback": { "success": "Giusto!", "collision": "Riprova." }
        }
        """;

    private static string Workspace(string blocks) =>
        "{\"blocks\":{\"languageVersion\":0,\"blocks\":" + blocks + "}}";

    private static string Block(string type, string id) => $"{{\"type\":\"{type}\",\"id\":\"{id}\"}}";

    private static string Start(string id) => Block("val_start", id);

    private static string Number(string id, int value) =>
        $"{Block("val_number", id)[..^1]},\"fields\":{{\"{ValueBlockCatalog.NumberField}\":{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}}}";

    private static string Variable(string id) =>
        $"{Block("val_variable", id)[..^1]},\"fields\":{{\"{ValueBlockCatalog.VariableField}\":\"Punti\"}}}}";

    private static string Add(string id, string left, string right) =>
        $"{Block("val_add", id)[..^1]},\"inputs\":{{\"{ValueBlockCatalog.ValueInput}\":{{\"block\":{left}}},\"{ValueBlockCatalog.OtherInput}\":{{\"block\":{right}}}}}}}";

    private static string Compare(string id, string left, int right) =>
        $"{Block("val_compare", id)[..^1]},\"fields\":{{\"{ValueBlockCatalog.CompareField}\":\"Greater\"}},\"inputs\":{{\"{ValueBlockCatalog.ValueInput}\":{{\"block\":{left}}},\"{ValueBlockCatalog.OtherInput}\":{{\"block\":{Number("num", right)}}}}}}}";

    private static string Set(string id, string variable, string value) =>
        $"{Block("val_set", id)[..^1]},\"fields\":{{\"{ValueBlockCatalog.VariableField}\":\"{variable}\"}},\"inputs\":{{\"{ValueBlockCatalog.ValueInput}\":{{\"block\":{value}}}}}}}";

    private static string Show(string id, string value) =>
        $"{Block("val_show", id)[..^1]},\"inputs\":{{\"{ValueBlockCatalog.ValueInput}\":{{\"block\":{value}}}}}}}";

    /// <summary>Mostra un blocco qualsiasi: serve a provare che cosa il validatore rifiuta.</summary>
    private static string ShowRawValue(string id, string value) =>
        $"{Block("val_show", id)[..^1]},\"inputs\":{{\"{ValueBlockCatalog.ValueInput}\":{{\"block\":{value}}}}}}}";

    private static string If(string id, string condition, string then, string otherwise) =>
        $"{Block("val_if", id)[..^1]},\"inputs\":{{\"{ValueBlockCatalog.ValueInput}\":{{\"block\":{condition}}},\"{ValueBlockCatalog.ThenInput}\":{{\"block\":{then}}},\"{ValueBlockCatalog.ElseInput}\":{{\"block\":{otherwise}}}}}}}";
    private static string StartWithNext(string id, string next) =>
        $"{Block("val_start", id)[..^1]},\"next\":{{\"block\":{next}}}}}";

    private static string BlockWithExtraInput(string id) =>
        $"{Block("val_show", id)[..^1]},\"inputs\":{{\"INVENTATO\":{{\"block\":{Number("n", 1)}}}}}}}";
}
