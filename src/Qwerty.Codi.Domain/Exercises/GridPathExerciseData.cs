namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Descrizione della versione <c>gridpath-1</c> (§13.1–§13.4): un personaggio su una griglia con
/// ostacoli e traguardo, quattro blocchi di movimento e ripetizione, e un completamento che coincide
/// con il traguardo della scena.
///
/// È la prima voce del registro dei motori. Le sue regole erano sparse fra la definizione, il
/// catalogo dei blocchi e il validatore del progetto; qui stanno insieme, perché sono la stessa
/// cosa: che cosa significa «esercizio» per questa versione del motore.
/// </summary>
public sealed class GridPathExerciseData : IExerciseEngineDescriptor
{
    /// <summary>Blocchi di <c>gridpath-1</c> (§13.4), nell'ordine in cui si offrono nell'editor.</summary>
    private static readonly IReadOnlyList<ExerciseBlockSpec> GridPathOneBlocks =
    [
        ExerciseBlockSpec.WithoutFields(ExerciseBlockCatalog.CodiStart),
        ExerciseBlockSpec.WithoutFields(ExerciseBlockCatalog.CodiMove),
        ExerciseBlockSpec.WithChoiceField(
            ExerciseBlockCatalog.CodiTurn,
            ExerciseBlockCatalog.TurnField,
            [nameof(GridTurn.Left), nameof(GridTurn.Right)]),
        new ExerciseBlockSpec(
            ExerciseBlockCatalog.CodiRepeat,
            [BlockFieldRule.Integer(
                ExerciseBlockCatalog.RepeatCountField,
                GridPathEngine.MinRepeatCount,
                GridPathEngine.MaxRepeatCount)])
        {
            // La ripetizione contiene una sequenza: il suo ingresso è di comandi, non di valori.
            StatementInputs = [ExerciseBlockCatalog.RepeatBodyInput],
        },
    ];

    /// <inheritdoc />
    public string ExerciseType => GridPathEngine.ExerciseType;

    /// <inheritdoc />
    public string Version => GridPathEngine.Version;

    /// <inheritdoc />
    public int SchemaVersion => GridPathEngine.SchemaVersion;

    /// <summary>
    /// Il checkpoint di <c>gridpath-1</c> porta posizione, orientamento, stack dei frame e budget
    /// consumato: è lo schema 1 che il player scrive e il server accetta (§13.7).
    /// </summary>
    public int CheckpointSchemaVersion => 1;

    /// <inheritdoc />
    public IReadOnlyList<ExerciseBlockSpec> Blocks => GridPathOneBlocks;

    /// <inheritdoc />
    public string EntryPointBlock => ExerciseBlockCatalog.CodiStart;

    /// <inheritdoc />
    public bool KnowsBlock(string blockType) =>
        GridPathOneBlocks.Any(block => string.Equals(block.Type, blockType, StringComparison.Ordinal));

    /// <inheritdoc />
    public ExerciseBlockSpec? Block(string blockType) =>
        GridPathOneBlocks.FirstOrDefault(block => string.Equals(block.Type, blockType, StringComparison.Ordinal));

    /// <summary>
    /// I dati di <c>GridPath</c> sono la scena da percorrere, la condizione che conclude
    /// l'esercizio e i messaggi con cui il gioco risponde. Le parti arrivano dal documento e sono
    /// già verificate singolarmente: questa versione le riunisce nella forma che le è propria.
    /// </summary>
    public IExerciseTypeData CreateData(object data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data is not ExerciseDefinitionParts parts)
        {
            throw new ArgumentException(
                $"La versione «{Version}» descrive un esercizio {ExerciseType}: le parti ricevute non hanno la forma attesa (scena, completamento e messaggi di esito).",
                nameof(data));
        }

        return new GridPathData(parts.Scene, parts.Completion, parts.Feedback);
    }

    /// <summary>
    /// La condizione di completamento deve coincidere con il traguardo della scena: due valori che
    /// devono essere uguali non vanno lasciati liberi di divergere (§13.2). Per una versione futura
    /// questa regola può non valere, ed è per questo che vive nella descrizione della versione.
    ///
    /// Dati di un altro tipo sono un errore di programmazione — il registro sceglie la descrizione
    /// dalla versione — ma l'errore va detto come tale, non come un cast fallito che nessuno legge.
    /// </summary>
    public void ValidateData(IExerciseTypeData data)
    {
        if (data is not GridPathData gridPath)
        {
            throw new ArgumentException(
                $"La versione «{Version}» descrive un esercizio {ExerciseType}: i dati da verificare sono di un altro tipo.",
                nameof(data));
        }

        if (gridPath.Completion.Target != gridPath.Scene.Goal)
        {
            throw new ArgumentException(
                $"Il completamento {gridPath.Completion.Target} non coincide con il traguardo {gridPath.Scene.Goal} della scena.",
                nameof(data));
        }
    }
}
