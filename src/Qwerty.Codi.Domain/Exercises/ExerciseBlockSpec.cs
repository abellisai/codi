namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Descrizione di un blocco del catalogo di una versione del motore (§13.4).
///
/// Il catalogo non è un elenco di nomi: un blocco ha campi e ingressi, e la sua forma va verificata
/// **prima** che il progetto entri in una bozza. Questa descrizione è il posto in cui quella forma
/// vive, così il server può verificare i campi di un blocco che non conosce senza una regola cablata
/// per nome: aggiungere un blocco a una versione nuova significa descriverlo qui, non aprire un `if`
/// altrove.
///
/// Un blocco senza campi si descrive con <see cref="Fields"/> vuoto e non accetta alcun campo: un
/// valore in più è un formato che non conosciamo, non un dettaglio da scartare.
///
/// Gli ingressi sono di due nature, e la differenza conta. <see cref="StatementInputs"/> contengono
/// **comandi**: sequenze che si eseguono in ordine. <see cref="ValueInputs"/> contengono
/// **espressioni**: valori da calcolare, che non si eseguono. È la distinzione fra un blocco che sta
/// in catena e uno che si annida dentro un valore, e un motore che ha entrambe la dichiara qui.
/// </summary>
public sealed record ExerciseBlockSpec(string Type, IReadOnlyList<BlockFieldRule> Fields)
{
    /// <summary>Ingressi che contengono comandi: sequenze da eseguire.</summary>
    public IReadOnlyList<string> StatementInputs { get; init; } = [];

    /// <summary>Ingressi che contengono espressioni: valori da calcolare.</summary>
    public IReadOnlyList<string> ValueInputs { get; init; } = [];

    /// <summary>
    /// Campi di **testo libero**: il contenuto lo scrive il docente e non c'è un elenco di valori
    /// ammessi. La domanda di «chiedi … e attendi» è l'unico caso, ed è il testo che il bambino legge:
    /// verificarla contro un elenco significherebbe dire al docente quali domande può fare.
    ///
    /// Un campo di testo è comunque verificato **nella forma**: se compare deve essere un testo, e non
    /// può essere vuoto. Ciò che non si verifica è il contenuto.
    /// </summary>
    public IReadOnlyList<string> TextFields { get; init; } = [];

    /// <summary>
    /// Che cosa è il blocco: un comando che si esegue, o un'espressione che produce un valore. Un
    /// blocco di espressione non apre una catena, e un comando non sta dentro un valore.
    /// </summary>
    public BlockKind Kind { get; init; } = BlockKind.Command;

    /// <summary>Tutti gli ingressi dichiarati, di sequenza e di valore.</summary>
    public IReadOnlyList<string> Inputs => [.. StatementInputs, .. ValueInputs];

    /// <summary>Tutti i nomi dei campi dichiarati, di regola e di testo libero.</summary>
    public IReadOnlyList<string> FieldNames => [.. Fields.Select(rule => rule.Name), .. TextFields];

    /// <summary>Blocco senza campi e senza ingressi.</summary>
    public static ExerciseBlockSpec WithoutFields(string type) => new(type, []);

    /// <summary>Blocco con un campo numerico intero in un intervallo chiuso.</summary>
    public static ExerciseBlockSpec WithIntegerField(string type, string field, long min, long max) =>
        new(type, [BlockFieldRule.Integer(field, min, max)]);

    /// <summary>Blocco con un campo a scelta fra i valori previsti, confrontati senza distinzione di maiuscole.</summary>
    public static ExerciseBlockSpec WithChoiceField(
        string type,
        string field,
        IReadOnlyList<string> choices) =>
        new(type, [BlockFieldRule.Choice(field, choices)]);
}

/// <summary>
/// Che cosa è un blocco del catalogo: un comando che si esegue, o un'espressione che produce un
/// valore. La distinzione non è decorativa — un'espressione non può aprire una catena, un comando non
/// può stare dentro un valore — e i motori la dichiarano nel proprio catalogo.
/// </summary>
public enum BlockKind
{
    /// <summary>Comando: sta in una catena e si esegue in ordine.</summary>
    Command,

    /// <summary>Espressione: si annida dentro un ingresso di valore e produce un valore.</summary>
    Expression,
}

/// <summary>
/// Regola di un campo di un blocco: nome, tipo e messaggio con cui si spiega un valore rifiutato.
/// Il messaggio è composto qui perché deve nominare il campo, il valore ricevuto e ciò che sarebbe
/// ammesso: tre cose che chi legge il documento non conosce da sé.
///
/// Un campo può anche contenere **il nome di una variabile**. In quel caso la regola verifica che il
/// valore sia un nome scritto correttamente, non che sia fra quelli dichiarati: i nomi ammessi
/// dipendono dall'esercizio e non dalla versione del motore, e li conosce solo chi ha in mano la
/// definizione. La regola dichiara l'esigenza, chi legge il documento la soddisfa.
/// </summary>
public sealed class BlockFieldRule
{
    private BlockFieldRule(
        string name,
        FieldKind kind,
        long min,
        long max,
        IReadOnlyList<string> choices,
        string displayLabel)
    {
        Name = name;
        Kind = kind;
        Min = min;
        Max = max;
        Choices = choices;
        DisplayLabel = displayLabel;
    }

    /// <summary>Nome del campo, quello che compare nel documento Blockly.</summary>
    public string Name { get; }

    /// <summary>Nomi ammessi, per un campo a scelta: compaiono nel messaggio di rifiuto.</summary>
    public string DisplayLabel { get; }

    private FieldKind Kind { get; }

    private long Min { get; }

    private long Max { get; }

    private IReadOnlyList<string> Choices { get; }

    /// <summary>Campo numerico intero in un intervallo chiuso.</summary>
    public static BlockFieldRule Integer(string name, long min, long max) =>
        new(name, FieldKind.Integer, min, max, [], string.Empty);

    /// <summary>Campo numerico a scelta fra i valori previsti; il confronto ignora le maiuscole.</summary>
    public static BlockFieldRule Choice(string name, IReadOnlyList<string> choices) =>
        new(name, FieldKind.Choice, 0, 0, choices, string.Join(
            " e ",
            choices.Count == 0 ? ["nessun valore"] : choices.ToArray()));

    /// <summary>
    /// Campo con il nome di una variabile. La forma del nome si verifica qui; l'appartenenza
    /// all'elenco dichiarato la verifica chi conosce la definizione.
    /// </summary>
    public static BlockFieldRule Variable(string name) => new(name, FieldKind.Name, 0, 0, [], string.Empty);

    /// <summary>
    /// Verifica un valore. Un campo assente, di tipo sbagliato, fuori intervallo o non previsto
    /// produce un <see cref="ArgumentException"/> che nomina il campo: chi legge il documento deve
    /// poter correggere la bozza, non indovinare che cosa manca.
    /// </summary>
    public void Validate(string type, BlockFieldValue? value)
    {
        if (value is null)
        {
            throw new ArgumentException($"Il blocco «{type}» richiede il campo «{Name}».", Name);
        }

        if (Kind == FieldKind.Integer)
        {
            if (value.Value.Number is not { } number || number < Min || number > Max)
            {
                throw new ArgumentException(
                    $"Il blocco «{type}» ha il campo «{Name}» con valore «{value.Value.Text}»: è un numero intero da {Min} a {Max}.",
                    Name);
            }

            return;
        }

        if (Kind == FieldKind.Name)
        {
            var nome = value.Value.Name ?? value.Value.Text;
            if (!ValueText.IsUsableName(nome))
            {
                throw new ArgumentException(
                    $"Il blocco «{type}» ha il campo «{Name}» con valore «{value.Value.Text}»: è il nome di una variabile, una lettera iniziale, poi lettere, cifre o trattino basso.",
                    Name);
            }

            return;
        }

        if (value.Value.Choice is null
            || !Choices.Contains(value.Value.Choice, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Il blocco «{type}» ha il campo «{Name}» con valore «{value.Value.Text}»: sono ammessi {DisplayLabel}.",
                Name);
        }
    }

    private enum FieldKind
    {
        Integer,
        Choice,
        Name,
    }
}
