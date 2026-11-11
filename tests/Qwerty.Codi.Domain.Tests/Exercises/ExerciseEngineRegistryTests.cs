using Qwerty.Codi.Domain.Exercises;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Exercises;

/// <summary>
/// Il registro dei motori è il punto in cui il prodotto impara che cosa sa interpretare. Questi
/// casi difendono la proprietà che lo rende utile: una versione registrata si descrive da sé —
/// schema, catalogo, campi dei blocchi, punto di ingresso, schema del checkpoint — e una versione
/// che non c'è non viene interpretata come se ci fosse.
///
/// Le prove non nominano i tipi futuri: verificano che il registro risponda **per versione** e che
/// una versione sconosciuta non produca mai un catalogo vuoto usato come se fosse valido.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ExerciseEngineRegistryTests
{
    [Fact]
    public void Registro_ContieneLaVersionePubblicata_ConLaSuaDescrizione()
    {
        var engine = Assert.IsAssignableFrom<IExerciseEngineDescriptor>(
            ExerciseEngineRegistry.Find(GridPathEngine.Version));

        Assert.Equal(GridPathEngine.Version, engine.Version);
        Assert.Equal(GridPathEngine.ExerciseType, engine.ExerciseType);
        Assert.Equal(GridPathEngine.SchemaVersion, engine.SchemaVersion);
        Assert.Equal(ExerciseBlockCatalog.CodiStart, engine.EntryPointBlock);
    }

    [Fact]
    public void Registro_VersioneSconosciuta_NonHaDescrizione()
    {
        // Una versione futura non viene interpretata con le regole di quella corrente: è il difetto
        // che ha reso necessario il registro.
        Assert.Null(ExerciseEngineRegistry.Find("gridpath-2"));
        Assert.Null(ExerciseEngineRegistry.Find(null));
        Assert.Null(ExerciseEngineRegistry.Find(string.Empty));

        var exception = Assert.Throws<ArgumentException>(
            () => ExerciseEngineRegistry.Require("gridpath-2", "engineVersion"));

        Assert.Equal("engineVersion", exception.ParamName);
        Assert.Contains(GridPathEngine.Version, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registro_DichiaraEsattamenteLeVersioniCheIlClientConosce()
    {
        // Il registro del server e quello della build Angular devono elencare le stesse coppie
        // tipo/versione: se divergessero, il server distribuirebbe un esercizio che il browser non
        // sa aprire, e l'alunno vedrebbe soltanto un messaggio di errore.
        //
        // La prova non può leggere il registro del client — è un'altra build, in un altro
        // linguaggio — quindi lo dichiara: aggiungere una versione qui **e** in
        // `libraries/exercise-engine/engine-registry.ts` è l'unico modo per farla passare.
        var clientRegistry = new[]
        {
            (Type: "GridPath", Version: "gridpath-1"),
            (Type: "Quiz", Version: "quiz-1"),
            (Type: "ValueProgram", Version: "valueprogram-1"),
        };

        Assert.Equal(
            clientRegistry,
            ExerciseEngineRegistry.KnownVersions
                .Select(version => (
                    Type: ExerciseEngineRegistry.Require(version, "version").ExerciseType,
                    Version: version))
                .ToArray());
    }

    [Fact]
    public void Catalogo_DichiaraICampiDiOgniBlocco_NonSoloINomi()
    {
        var engine = ExerciseEngineRegistry.Require(GridPathEngine.Version, "engineVersion");

        var turn = engine.Block(ExerciseBlockCatalog.CodiTurn);
        Assert.NotNull(turn);
        var turnRule = Assert.Single(turn.Fields);
        Assert.Equal(ExerciseBlockCatalog.TurnField, turnRule.Name);
        Assert.Equal($"{nameof(GridTurn.Left)} e {nameof(GridTurn.Right)}", turnRule.DisplayLabel);

        var repeat = engine.Block(ExerciseBlockCatalog.CodiRepeat);
        Assert.NotNull(repeat);
        var repeatRule = Assert.Single(repeat.Fields);
        Assert.Equal(ExerciseBlockCatalog.RepeatCountField, repeatRule.Name);

        Assert.Empty(engine.Block(ExerciseBlockCatalog.CodiStart)!.Fields);
        Assert.Null(engine.Block("aula_jump"));
    }

    [Fact]
    public void Campi_NumeroFuoriIntervallo_RifiutatoConIlCampoNominato()
    {
        var repeat = ExerciseEngineRegistry.Require(GridPathEngine.Version, "engineVersion")
            .Block(ExerciseBlockCatalog.CodiRepeat)!;

        var rule = Assert.Single(repeat.Fields);

        rule.Validate(ExerciseBlockCatalog.CodiRepeat, BlockFieldValue.NumberValue(GridPathEngine.MinRepeatCount));
        rule.Validate(ExerciseBlockCatalog.CodiRepeat, BlockFieldValue.NumberValue(GridPathEngine.MaxRepeatCount));

        var tooMany = Assert.Throws<ArgumentException>(
            () => rule.Validate(
                ExerciseBlockCatalog.CodiRepeat,
                BlockFieldValue.NumberValue(GridPathEngine.MaxRepeatCount + 1)));
        Assert.Equal(ExerciseBlockCatalog.RepeatCountField, tooMany.ParamName);

        var missing = Assert.Throws<ArgumentException>(
            () => rule.Validate(ExerciseBlockCatalog.CodiRepeat, null));
        Assert.Equal(ExerciseBlockCatalog.RepeatCountField, missing.ParamName);
    }

    [Fact]
    public void Campi_SceltaNonPrevista_Rifiutata()
    {
        var turn = ExerciseEngineRegistry.Require(GridPathEngine.Version, "engineVersion")
            .Block(ExerciseBlockCatalog.CodiTurn)!;

        var rule = Assert.Single(turn.Fields);

        // Il confronto non distingue le maiuscole: il campo arriva dal documento Blockly, dove il
        // valore è già quello del catalogo.
        rule.Validate(ExerciseBlockCatalog.CodiTurn, BlockFieldValue.ChoiceValue("Left"));
        rule.Validate(ExerciseBlockCatalog.CodiTurn, BlockFieldValue.ChoiceValue("right"));

        var wrong = Assert.Throws<ArgumentException>(
            () => rule.Validate(ExerciseBlockCatalog.CodiTurn, BlockFieldValue.ChoiceValue("Up")));

        Assert.Equal(ExerciseBlockCatalog.TurnField, wrong.ParamName);
        Assert.Contains("Left e Right", wrong.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Checkpoint_OgniVersioneDichiaraIlProprioSchema()
    {
        // Il numero non è un valore globale del prodotto: appartiene alla versione del motore che
        // produce il checkpoint, e il server lo chiede alla descrizione invece di fissarlo.
        var engine = ExerciseEngineRegistry.Require(GridPathEngine.Version, "engineVersion");

        Assert.Equal(1, engine.CheckpointSchemaVersion);

        // Due motori possono dichiarare lo stesso numero e avere formati diversi: il numero vale
        // **dentro** la versione, e per questo si chiede alla versione e non al prodotto.
        var valori = ExerciseEngineRegistry.Require(ValueProgramEngine.Version, "engineVersion");
        Assert.Equal(1, valori.CheckpointSchemaVersion);
        Assert.NotEqual(
            ExerciseEngineRegistry.Require(GridPathEngine.Version, "engineVersion").ExerciseType,
            valori.ExerciseType);
    }

    [Fact]
    public void Dati_CompostiDallePartiDichiarate_RiunitiNellaFormaDelTipo()
    {
        var engine = ExerciseEngineRegistry.Require(GridPathEngine.Version, "engineVersion");
        var scene = ExerciseScene.Create(
            4, 4, new GridPosition(0, 0), GridDirection.East, [], new GridPosition(3, 3));

        var data = engine.CreateData(new ExerciseDefinitionParts(
            scene,
            ExerciseCompletion.Create(ExerciseCompletionKind.ReachCell, new GridPosition(3, 3)),
            ExerciseFeedback.Create("Bravo!", "Ostacolo.")));

        var gridPath = Assert.IsType<GridPathData>(data);
        Assert.Same(scene, gridPath.Scene);
    }

    [Fact]
    public void Dati_PartiDiUnaFormaDiversa_Rifiutate()
    {
        var engine = ExerciseEngineRegistry.Require(GridPathEngine.Version, "engineVersion");

        var exception = Assert.Throws<ArgumentException>(() => engine.CreateData("non sono le parti attese"));

        Assert.Equal("data", exception.ParamName);
    }

    [Fact]
    public void Dati_CompletamentoLontanoDalTraguardo_Rifiutato()
    {
        var engine = ExerciseEngineRegistry.Require(GridPathEngine.Version, "engineVersion");
        var data = engine.CreateData(new ExerciseDefinitionParts(
            ExerciseScene.Create(4, 4, new GridPosition(0, 0), GridDirection.East, [], new GridPosition(3, 3)),
            ExerciseCompletion.Create(ExerciseCompletionKind.ReachCell, new GridPosition(2, 2)),
            ExerciseFeedback.Create("Bravo!", "Ostacolo.")));

        var exception = Assert.Throws<ArgumentException>(() => engine.ValidateData(data));

        Assert.Equal("data", exception.ParamName);
    }
}
