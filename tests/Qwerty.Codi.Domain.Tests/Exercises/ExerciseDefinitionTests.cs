using Qwerty.Codi.Domain.Exercises;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Exercises;

/// <summary>
/// Regole della definizione didattica (§13.2): schema, tipo, versione del motore, scena, catalogo
/// dei blocchi, limiti e coerenza fra completamento e traguardo. La definizione è autosufficiente e
/// immutabile, quindi ogni caso qui è un rifiuto alla costruzione o un valore conservato.
///
/// L'esempio usato come definizione valida è quello di §13.2: se la specifica cambia esempio, il
/// caso continua a descrivere lo stesso contratto.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ExerciseDefinitionTests
{
    [Fact]
    public void Create_ConDefinizioneDiSezione132_ConservaOgniParte()
    {
        var definition = Definition();
        var gridPath = Assert.IsType<GridPathData>(definition.Data);

        Assert.Equal(GridPathEngine.SchemaVersion, definition.SchemaVersion);
        Assert.Equal(GridPathEngine.ExerciseType, definition.ExerciseType);
        Assert.Equal(GridPathEngine.Version, definition.EngineVersion);
        Assert.Equal("Raggiungi la stella", definition.Title);
        Assert.Equal("Porta il personaggio sulla stella usando i blocchi.", definition.Instructions);
        Assert.Equal(5, gridPath.Scene.Width);
        Assert.Equal(4, gridPath.Scene.Height);
        Assert.Equal(new GridPosition(0, 1), gridPath.Scene.Start);
        Assert.Equal(GridDirection.East, gridPath.Scene.StartDirection);
        Assert.Equal([new GridPosition(2, 2)], gridPath.Scene.Obstacles);
        Assert.Equal(new GridPosition(3, 1), gridPath.Scene.Goal);
        Assert.Equal(
            [
                ExerciseBlockCatalog.CodiStart,
                ExerciseBlockCatalog.CodiMove,
                ExerciseBlockCatalog.CodiTurn,
                ExerciseBlockCatalog.CodiRepeat,
            ],
            definition.AllowedBlocks);
        Assert.Equal(30, definition.Limits.MaxBlocks);
        Assert.Equal(8, definition.Limits.MaxNestingDepth);
        Assert.Equal(1000, definition.Limits.MaxExecutionSteps);
        Assert.Equal(120000, definition.Limits.MaxActiveDurationMs);
        Assert.Equal(ExerciseCompletionKind.ReachCell, gridPath.Completion.Kind);
        Assert.Equal(new GridPosition(3, 1), gridPath.Completion.Target);
        Assert.Equal("Hai raggiunto la stella!", gridPath.Feedback.Success);
        Assert.Equal("C'è un ostacolo: prova a cambiare percorso.", gridPath.Feedback.Collision);
    }

    [Fact]
    public void Create_ConTipoDiEsercizioNonPrevisto_Rifiuta()
    {
        var exception = Assert.Throws<ArgumentException>(() => Definition(exerciseType: "ScratchProject"));

        Assert.Equal("exerciseType", exception.ParamName);
    }

    [Fact]
    public void Create_ConVersioneDelMotoreSconosciuta_Rifiuta()
    {
        var exception = Assert.Throws<ArgumentException>(() => Definition(engineVersion: "gridpath-2"));

        Assert.Equal("engineVersion", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ConTitoloInutilizzabile_Rifiuta(string title)
    {
        var exception = Assert.Throws<ArgumentException>(() => Definition(title: title));

        Assert.Equal("title", exception.ParamName);
    }

    [Fact]
    public void Create_ConTitoloOltreIlLimiteDellAnagrafica_Rifiuta()
    {
        var tooLong = new string('a', Exercise.MaxTitleLength + 1);

        var exception = Assert.Throws<ArgumentException>(() => Definition(title: tooLong));

        Assert.Equal("title", exception.ParamName);
    }

    [Fact]
    public void Create_ConTitoloAlLimiteDellAnagrafica_Accetta()
    {
        // Il titolo non è la consegna: i due limiti sono diversi e non vanno confusi.
        var title = new string('a', Exercise.MaxTitleLength);

        var definition = Definition(title: title);

        Assert.Equal(Exercise.MaxTitleLength, definition.Title.Length);
    }

    [Fact]
    public void Create_ConConsegnaVuota_Rifiuta()
    {
        var exception = Assert.Throws<ArgumentException>(() => Definition(instructions: "   "));

        Assert.Equal("instructions", exception.ParamName);
    }

    [Fact]
    public void Create_ConConsegnaOltreIlLimite_Rifiuta()
    {
        var tooLong = new string('a', ExerciseDefinition.MaxInstructionsLength + 1);

        var exception = Assert.Throws<ArgumentException>(() => Definition(instructions: tooLong));

        Assert.Equal("instructions", exception.ParamName);
    }

    [Fact]
    public void Create_SenzaBlocchiAmmessi_Rifiuta()
    {
        var exception = Assert.Throws<ArgumentException>(() => Definition(allowedBlocks: []));

        Assert.Equal("allowedBlocks", exception.ParamName);
    }

    [Fact]
    public void Create_ConBloccoFuoriDalCatalogoDellaVersione_Rifiuta()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Definition(allowedBlocks: [ExerciseBlockCatalog.CodiStart, "aula_jump"]));

        Assert.Equal("allowedBlocks", exception.ParamName);
    }

    [Fact]
    public void Create_SenzaPuntoDiIngresso_Rifiuta()
    {
        // Il diniego deve arrivare al docente che compone l'esercizio, non all'alunno (§13.4).
        var exception = Assert.Throws<ArgumentException>(
            () => Definition(allowedBlocks: [ExerciseBlockCatalog.CodiMove, ExerciseBlockCatalog.CodiTurn]));

        Assert.Equal("allowedBlocks", exception.ParamName);
        Assert.Contains(ExerciseBlockCatalog.CodiStart, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_ConBloccoRipetuto_Rifiuta()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Definition(allowedBlocks: [ExerciseBlockCatalog.CodiStart, ExerciseBlockCatalog.CodiStart]));

        Assert.Equal("allowedBlocks", exception.ParamName);
    }

    [Fact]
    public void Create_ConCompletamentoSuUnaCellaDiversaDalTraguardo_Rifiuta()
    {
        // La regola è della versione del motore, che riceve i dati del tipo già riuniti: il
        // parametro nominato è quindi l'insieme dei dati, non il singolo completamento.
        var exception = Assert.Throws<ArgumentException>(
            () => Definition(
                completion: ExerciseCompletion.Create(ExerciseCompletionKind.ReachCell, new GridPosition(4, 3))));

        Assert.Equal("data", exception.ParamName);
        Assert.Contains("traguardo", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_ConBloccoIntrodottoDopoLaVersioneDichiarata_NonLoAmmessoAQuellaVersione()
    {
        // Il catalogo è per versione: un blocco che apparterrà a una versione futura non può
        // comparire in una definizione di gridpath-1.
        var catalogue = ExerciseBlockCatalog.ForEngine(GridPathEngine.Version);

        Assert.Equal(4, catalogue.Count);
        Assert.DoesNotContain("aula_wait", catalogue);
        Assert.Empty(ExerciseBlockCatalog.ForEngine("gridpath-2"));
        Assert.Empty(ExerciseBlockCatalog.ForEngine(null));
    }

    [Fact]
    public void Create_ConLaDefinizioneDiSezione132_LaVersioneDelMotoreVieneDallaDescrizione()
    {
        // La definizione non riceve più schema e versione come parametri propri: li prende dalla
        // descrizione del motore. Se i due divergessero, un esercizio pubblicato verrebbe
        // interpretato con regole che non sono le sue.
        var definition = Definition();
        var engine = Assert.IsAssignableFrom<IExerciseEngineDescriptor>(
            ExerciseEngineRegistry.Find(GridPathEngine.Version));

        Assert.Equal(engine.SchemaVersion, definition.SchemaVersion);
        Assert.Equal(engine.Version, definition.EngineVersion);
        Assert.Equal(engine.ExerciseType, definition.ExerciseType);
    }

    [Fact]
    public void Create_ConDatiDiUnTipoCheIlMotoreNonConosce_Rifiuta()
    {
        // Il registro sceglie la descrizione dalla versione: una definizione che porta dati di un
        // altro tipo con il motore sbagliato è un errore di programmazione, non un esercizio.
        var engine = ExerciseEngineRegistry.Require(GridPathEngine.Version, "engineVersion");

        var exception = Assert.Throws<ArgumentException>(
            () => ExerciseDefinition.Create(
                engine,
                engine.ExerciseType,
                "Raggiungi la stella",
                "Porta il personaggio sulla stella usando i blocchi.",
                [ExerciseBlockCatalog.CodiStart],
                ExerciseLimits.Create(30, 8, 1000, 120000),
                new AltriDati()));

        Assert.Equal("data", exception.ParamName);
    }

    /// <summary>Dati di un tipo che questa build non conosce: servono a provare il rifiuto.</summary>
    private sealed record AltriDati : IExerciseTypeData;

    private static ExerciseScene Scene() =>
        ExerciseScene.Create(
            5, 4, new GridPosition(0, 1), GridDirection.East, [new GridPosition(2, 2)], new GridPosition(3, 1));

    /// <summary>
    /// Costruisce la definizione di §13.2 sostituendo soltanto la parte che il caso vuole mettere
    /// alla prova: così un caso che rifiuta il completamento non dipende dal resto della validità.
    ///
    /// Lo schema dichiarato è quello della versione registrata: una definizione non può dichiarare
    /// uno schema che la sua versione non conosce, perché è la versione a scegliere le regole.
    /// </summary>
    private static ExerciseDefinition Definition(
        string? exerciseType = null,
        string? engineVersion = null,
        string? title = null,
        string? instructions = null,
        ExerciseScene? scene = null,
        IReadOnlyList<string>? allowedBlocks = null,
        ExerciseLimits? limits = null,
        ExerciseCompletion? completion = null,
        ExerciseFeedback? feedback = null)
    {
        var engine = ExerciseEngineRegistry.Require(
            engineVersion ?? GridPathEngine.Version, nameof(engineVersion));

        return ExerciseDefinition.Create(
            engine,
            exerciseType ?? engine.ExerciseType,
            title ?? "Raggiungi la stella",
            instructions ?? "Porta il personaggio sulla stella usando i blocchi.",
            allowedBlocks
                ??
                [
                    ExerciseBlockCatalog.CodiStart,
                    ExerciseBlockCatalog.CodiMove,
                    ExerciseBlockCatalog.CodiTurn,
                    ExerciseBlockCatalog.CodiRepeat,
                ],
            limits ?? ExerciseLimits.Create(30, 8, 1000, 120000),
            new GridPathData(
                scene ?? Scene(),
                completion ?? ExerciseCompletion.Create(ExerciseCompletionKind.ReachCell, Scene().Goal),
                feedback ?? ExerciseFeedback.Create(
                    "Hai raggiunto la stella!", "C'è un ostacolo: prova a cambiare percorso.")));
    }
}
