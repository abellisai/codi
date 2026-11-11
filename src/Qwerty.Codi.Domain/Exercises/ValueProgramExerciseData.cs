namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Descrizione della versione <c>valueprogram-1</c> (§13.9): un programma a blocchi che conserva
/// valori, decide con un confronto e mostra un risultato legge valori dall'esterno.
///
/// È la terza voce del registro e il **secondo modello di esecuzione** del prodotto. Il catalogo ha
/// due famiglie di blocchi: i comandi, che stanno in una catena e si eseguono in ordine, e le
/// espressioni, che si annidano dentro un ingresso di valore e producono un valore. Il punto di
/// ingresso apre la catena, come nel percorso; ma dove il percorso ha una scena da attraversare, qui
/// ci sono variabili da riempire e casi di prova da superare, e la corsa può fermarsi in attesa di un
/// valore che l'alunno deve fornire.
/// </summary>
public sealed class ValueProgramExerciseData : IExerciseEngineDescriptor
{
    /// <summary>Blocchi di <c>valueprogram-1</c>, nell'ordine in cui si offrono nell'editor.</summary>
    private static readonly IReadOnlyList<ExerciseBlockSpec> ValueProgramOneBlocks =
    [
        // Comandi: si concatenano sotto il punto di ingresso e possono contenere sequenze. Il valore
        // da assegnare o da mostrare arriva invece da un ingresso di valore, cioè un'espressione.
        ExerciseBlockSpec.WithoutFields(ValueBlockCatalog.Start),
        new ExerciseBlockSpec(
            ValueBlockCatalog.Set,
            [BlockFieldRule.Variable(ValueBlockCatalog.VariableField)])
        {
            ValueInputs = [ValueBlockCatalog.ValueInput],
        },
        new ExerciseBlockSpec(
            ValueBlockCatalog.Change,
            [BlockFieldRule.Variable(ValueBlockCatalog.VariableField)])
        {
            ValueInputs = [ValueBlockCatalog.ChangeField],
        },
        new ExerciseBlockSpec(ValueBlockCatalog.Show, [])
        {
            ValueInputs = [ValueBlockCatalog.ValueInput],
        },
        new ExerciseBlockSpec(ValueBlockCatalog.Ask, [])
        {
            // Il blocco «chiedi … e attendi» del corso: la domanda è testo libero che il bambino legge,
            // e la risposta si usa con il reporter «risposta», non assegnandola qui.
            TextFields = [ValueBlockCatalog.QuestionField],
            ValueInputs = [],
        },
        new ExerciseBlockSpec(ValueBlockCatalog.If, [])
        {
            ValueInputs = [ValueBlockCatalog.ValueInput, ValueBlockCatalog.OtherInput],
            StatementInputs = [ValueBlockCatalog.ThenInput, ValueBlockCatalog.ElseInput],
        },
        new ExerciseBlockSpec(
            ValueBlockCatalog.Repeat,
            [BlockFieldRule.Integer(
                ValueBlockCatalog.RepeatCountField,
                ValueProgramEngine.MinRepeatCount,
                ValueProgramEngine.MaxRepeatCount)])
        {
            StatementInputs = [ValueBlockCatalog.RepeatBodyInput],
        },

        // Espressioni: si annidano dentro gli ingressi di valore dei comandi.
        new ExerciseBlockSpec(
            ValueBlockCatalog.Number,
            [BlockFieldRule.Integer(
                ValueBlockCatalog.NumberField,
                -ValueProgramEngine.MaxInteger,
                ValueProgramEngine.MaxInteger)])
        {
            Kind = BlockKind.Expression,
        },
        new ExerciseBlockSpec(
            ValueBlockCatalog.Variable,
            [BlockFieldRule.Variable(ValueBlockCatalog.VariableField)])
        {
            Kind = BlockKind.Expression,
        },
        new ExerciseBlockSpec(ValueBlockCatalog.Answer, [])
        {
            // Il reporter «risposta»: non ha campi, perché la risposta non si sceglie — è ciò che
            // l'ultima lettura ha prodotto.
            Kind = BlockKind.Expression,
        },
        new ExerciseBlockSpec(ValueBlockCatalog.Add, [])
        {
            Kind = BlockKind.Expression,
            ValueInputs = [ValueBlockCatalog.ValueInput, ValueBlockCatalog.OtherInput],
        },
        new ExerciseBlockSpec(
            ValueBlockCatalog.Compare,
            [BlockFieldRule.Choice(ValueBlockCatalog.CompareField, ValueBlockCatalog.ComparisonValues)])
        {
            Kind = BlockKind.Expression,
            ValueInputs = [ValueBlockCatalog.ValueInput, ValueBlockCatalog.OtherInput],
        },
    ];

    /// <inheritdoc />
    public string ExerciseType => ValueProgramEngine.ExerciseType;

    /// <inheritdoc />
    public string Version => ValueProgramEngine.Version;

    /// <inheritdoc />
    public int SchemaVersion => ValueProgramEngine.SchemaVersion;

    /// <summary>
    /// Il checkpoint di <c>valueprogram-1</c> porta l'ambiente delle variabili, l'uscita, i valori
    /// ancora da consumare e il percorso di esecuzione: è lo schema 1 di questa versione del motore,
    /// che non ha nulla a che vedere con lo schema 1 di <c>gridpath-1</c> (§13.7).
    /// </summary>
    public int CheckpointSchemaVersion => ValueProgramEngine.CheckpointSchemaVersion;

    /// <inheritdoc />
    public IReadOnlyList<ExerciseBlockSpec> Blocks => ValueProgramOneBlocks;

    /// <inheritdoc />
    public string EntryPointBlock => ValueBlockCatalog.Start;

    /// <inheritdoc />
    public bool KnowsBlock(string blockType) =>
        ValueProgramOneBlocks.Any(block => string.Equals(block.Type, blockType, StringComparison.Ordinal));

    /// <inheritdoc />
    public ExerciseBlockSpec? Block(string blockType) =>
        ValueProgramOneBlocks.FirstOrDefault(block => string.Equals(block.Type, blockType, StringComparison.Ordinal));

    /// <summary>
    /// I dati del tipo sono le variabili dichiarate e i casi di prova. Le parti arrivano dal
    /// documento e sono già verificate singolarmente: questa versione le riunisce nella forma che le
    /// è propria, e la costruzione rifiuta un esercizio incoerente.
    /// </summary>
    public IExerciseTypeData CreateData(object data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data is not ValueProgramParts parts)
        {
            throw new ArgumentException(
                $"La versione «{Version}» descrive un esercizio {ExerciseType}: le parti ricevute non hanno la forma attesa (variabili, casi di prova e messaggi di esito).",
                nameof(data));
        }

        return ValueProgramData.Create(parts.Variables, parts.TestCases, parts.Feedback);
    }

    /// <summary>
    /// Le regole di questa versione che non riguardano i campi dei blocchi: che i dati siano i suoi, e
    /// che ogni caso di prova non chieda più valori di quanti il programma possa leggerne.
    /// </summary>
    public void ValidateData(IExerciseTypeData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data is not ValueProgramData program)
        {
            throw new ArgumentException(
                $"La versione «{Version}» descrive un esercizio {ExerciseType}: i dati da verificare sono di un altro tipo.",
                nameof(data));
        }

        // Un caso di prova con più valori di quanti il programma ne legge lascerebbe una corsa ferma
        // in attesa per sempre: è un esercizio che nessun alunno può completare, e va fermato qui e
        // non davanti a un bambino.
        foreach (var caso in program.TestCases)
        {
            if (caso.Inputs.Count > ValueProgramEngine.MaxParameters)
            {
                throw new ArgumentException(
                    $"Un caso di prova chiede {caso.Inputs.Count} valori, ma un esercizio può leggerne al massimo {ValueProgramEngine.MaxParameters}.");
            }
        }
    }
}
