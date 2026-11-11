using System.Text.Json;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Domain.Exercises;

namespace Qwerty.Codi.Application.Exercises;

/// <summary>
/// Verifica di un progetto a blocchi Blockly (§13.3): il server controlla dimensione, profondità,
/// numero di blocchi, tipi, campi e collegamenti **prima** di conservare o distribuire un
/// workspace, così un progetto che non è interpretabile dalla versione dichiarata dalla definizione
/// non entra nella bozza.
///
/// Il formato del workspace è quello di Blockly e non il nostro (§13.3): la verifica legge le
/// proprietà che descrivono i blocchi — tipo, identificativo, campi, ingressi e catena — e ignora i
/// metadati di presentazione che Blockly aggiunge da sé, come la posizione o lo stato di
/// riduzione. Un blocco che la versione del motore non conosce o che l'esercizio non mette a
/// disposizione viene invece rifiutato, e non interpretato come qualcosa di simile.
///
/// La presenza di <c>aula_start</c> non è richiesta qui: §13.4 dice che la sua mancanza «impedisce
/// la prova», non il salvataggio, e il docente deve poter mettere via un progetto incompleto. Il
/// salvataggio verifica che ciò che c'è sia interpretabile; eseguibilità e pubblicazione
/// aggiungono le rispettive condizioni.
/// </summary>
public static class ExerciseWorkspaceJson
{
    /// <summary>Limite del progetto a blocchi, allineato ai 256 KiB che §15.5 dà alle anteprime.</summary>
    public const int MaxLength = 262_144;

    /// <summary>Proprietà che contiene i blocchi nella serializzazione di Blockly.</summary>
    private const string Blocks = "blocks";

    private static readonly JsonDocumentOptions Reading = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
    };

    /// <summary>
    /// Verifica un progetto a blocchi e lo restituisce com'è stato ricevuto: il formato è di Blockly,
    /// quindi non viene riscritto — riscriverlo significherebbe interpretarlo, e l'interpretazione
    /// appartiene all'interprete (§13.5).
    ///
    /// Le regole dipendono dalla definizione ricevuta nello stesso salvataggio: è la definizione a
    /// dichiarare versione del motore, blocchi ammessi e limiti, mentre un progetto non ha un modo
    /// proprio di dichiararli.
    ///
    /// <paramref name="requireEntryPoint"/> è vero per i progetti che devono poter partire, come la
    /// soluzione di riferimento: §13.4 lega la mancanza di <c>aula_start</c> alla prova, e la prova
    /// di una soluzione è la sua esecuzione.
    ///
    /// <paramref name="label"/> è l'etichetta del documento **con il suo articolo** — «il progetto a
    /// blocchi», «la soluzione di riferimento» — perché compare all'inizio delle frasi e un articolo
    /// scritto a mano non si accorderebbe con tutte.
    /// </summary>
    public static string Validate(
        string json,
        ExerciseDefinition definition,
        string requestName,
        string label,
        bool requireEntryPoint = false)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new CodiRequestException(CodiErrorCodes.ValidationFailed, $"Manca {label}.");
        }

        if (json.Length > MaxLength)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed, $"{Capitalize(label)} supera i {MaxLength} caratteri.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, Reading);
        }
        catch (JsonException failure)
        {
            throw CodiRequestException.InvalidPayload(requestName, failure);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new CodiRequestException(
                    CodiErrorCodes.ValidationFailed, $"{Capitalize(label)} deve essere un oggetto JSON.");
            }

            ValidateRoot(root, definition, label, requireEntryPoint);
        }

        return json;
    }

    /// <summary>Verifica i blocchi di primo livello, se il workspace ne ha.</summary>
    private static void ValidateRoot(
        JsonElement root,
        ExerciseDefinition definition,
        string label,
        bool requireEntryPoint)
    {
        if (!root.TryGetProperty(Blocks, out var container))
        {
            // Un workspace vuoto è `{}`: non c'è ancora nulla da eseguire, ma non c'è nemmeno
            // qualcosa di sbagliato. È la condizione di partenza dell'editor.
            EnsureEntryPoint(context: null, label, requireEntryPoint);

            return;
        }

        if (container.ValueKind != JsonValueKind.Object)
        {
            throw Malformed(label);
        }

        if (!container.TryGetProperty(Blocks, out var list))
        {
            // Il contenitore c'è ma non elenca blocchi: è la forma che Blockly produce per un
            // workspace con la sola versione del formato.
            EnsureEntryPoint(context: null, label, requireEntryPoint);

            return;
        }

        if (list.ValueKind != JsonValueKind.Array)
        {
            throw Malformed(label);
        }

        var context = new BlockContext(definition, label);
        foreach (var block in list.EnumerateArray())
        {
            ValidateBlock(block, context, depth: 1);
        }

        if (context.Starts > 1)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                $"{Capitalize(label)} ha {context.Starts} blocchi «{context.EntryPointBlock}»: il punto di ingresso è uno solo.");
        }

        EnsureEntryPoint(context, label, requireEntryPoint);
    }

    /// <summary>
    /// Verifica che il progetto abbia un punto di ingresso, quando è richiesto. Il blocco atteso è
    /// quello della versione del motore, che il contesto conosce: senza contesto la versione non è
    /// registrata, e il progetto non si può verificare affatto.
    /// </summary>
    private static void EnsureEntryPoint(
        BlockContext? context,
        string label,
        bool requireEntryPoint)
    {
        if (!requireEntryPoint || (context is not null && context.Starts > 0))
        {
            return;
        }

        if (context is null)
        {
            throw new InvalidOperationException(
                "Un progetto si verifica solo contro la descrizione di una versione registrata.");
        }

        throw new CodiRequestException(
            CodiErrorCodes.ValidationFailed,
            $"{Capitalize(label)} non ha il blocco «{context.EntryPointBlock}»: senza punto di ingresso il programma non parte.");
    }

    /// <summary>Prima lettera in maiuscola: l'etichetta apre la frase e porta con sé il suo articolo.</summary>
    private static string Capitalize(string label) =>
        label.Length == 0 ? label : char.ToUpperInvariant(label[0]) + label[1..];

    /// <summary>
    /// Verifica un blocco e i blocchi che contiene. La ricorsione è limitata dai limiti stessi
    /// dell'esercizio: superata la profondità o il numero di blocchi ammessi la verifica si ferma,
    /// quindi un documento patologico non può far esplorare uno stack senza fine.
    /// </summary>
    private static void ValidateBlock(JsonElement block, BlockContext context, int depth)
    {
        if (block.ValueKind != JsonValueKind.Object)
        {
            throw Malformed(context.Label);
        }

        context.EnsureDepth(depth);

        if (!block.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed, $"{Capitalize(context.Label)} contiene un blocco senza tipo.");
        }

        var type = typeElement.GetString() ?? string.Empty;
        context.CountBlock(type);

        if (block.TryGetProperty("id", out var identifier) && identifier.ValueKind == JsonValueKind.String)
        {
            context.RegisterIdentifier(identifier.GetString() ?? string.Empty);
        }

        ValidateFields(block, type, context);

        if (block.TryGetProperty("inputs", out var inputs))
        {
            if (inputs.ValueKind != JsonValueKind.Object)
            {
                throw Malformed(context.Label);
            }

            // Gli ingressi ammessi non sono scritti qui: arrivano dal catalogo della versione del
            // motore, che li dichiara per ogni blocco. Il percorso ha un solo ingresso di sequenza;
            // il programma di valori ha ingressi di valore, in cui si annida un'espressione, e
            // ingressi di sequenza, in cui si annida un comando. La regola è la stessa per entrambi:
            // un ingresso che il catalogo non dichiara è un progetto da correggere.
            var specification = context.Block(type);
            var statements = specification?.StatementInputs ?? [];
            var values = specification?.ValueInputs ?? [];

            foreach (var input in inputs.EnumerateObject())
            {
                if (statements.Contains(input.Name, StringComparer.Ordinal))
                {
                    ValidateConnection(input.Value, type, input.Name, context, depth + 1, expectExpression: false);
                }
                else if (values.Contains(input.Name, StringComparer.Ordinal))
                {
                    ValidateConnection(input.Value, type, input.Name, context, depth, expectExpression: true);
                }
                else
                {
                    throw new CodiRequestException(
                        CodiErrorCodes.ValidationFailed,
                        $"Il blocco «{type}» non ha l'ingresso «{input.Name}».");
                }
            }
        }

        if (block.TryGetProperty("next", out var next))
        {
            // La catena prosegue allo stesso livello di istruzione e contiene comandi: è la sequenza
            // che si esegue, non un valore da calcolare.
            ValidateConnection(next, type, "next", context, depth, expectExpression: false);
        }
    }

    /// <summary>
    /// Verifica un collegamento — ingresso o catena — e prosegue sul blocco collegato.
    ///
    /// <paramref name="expectExpression"/> dice che cosa l'ingresso deve contenere: un'espressione da
    /// calcolare, oppure un comando da eseguire. La verifica non è formale: un'espressione messa in
    /// catena è un programma che non parte, e un comando messo dentro un valore è un valore che non si
    /// sa calcolare. Entrambi si scoprono qui, non davanti a un bambino.
    /// </summary>
    private static void ValidateConnection(
        JsonElement connection,
        string type,
        string name,
        BlockContext context,
        int depth,
        bool expectExpression)
    {
        if (connection.ValueKind != JsonValueKind.Object)
        {
            throw Malformed(context.Label);
        }

        if (connection.TryGetProperty("shadow", out _))
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                $"{DescribeConnection(type, name)} è un'ombra: i collegamenti del progetto devono contenere blocchi.");
        }

        if (!connection.TryGetProperty("block", out var block) || block.ValueKind == JsonValueKind.Null)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                $"{DescribeConnection(type, name)} non contiene un blocco.");
        }

        var kind = ReadKind(block);
        if (kind is not null && context.Block(kind) is { } inner)
        {
            if (expectExpression && inner.Kind != BlockKind.Expression)
            {
                throw new CodiRequestException(
                    CodiErrorCodes.ValidationFailed,
                    $"{DescribeConnection(type, name)} contiene il comando «{kind}»: serve un valore da calcolare.");
            }

            if (!expectExpression && inner.Kind == BlockKind.Expression)
            {
                throw new CodiRequestException(
                    CodiErrorCodes.ValidationFailed,
                    $"{DescribeConnection(type, name)} contiene l'espressione «{kind}»: serve un comando da eseguire.");
            }
        }

        ValidateBlock(block, context, depth);
    }

    /// <summary>Tipo dichiarato da un blocco collegato, quando c'è.</summary>
    private static string? ReadKind(JsonElement block) =>
        block.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
            ? type.GetString()
            : null;

    private static string DescribeConnection(string type, string name) =>
        string.Equals(name, "next", StringComparison.Ordinal)
            ? $"La catena del blocco «{type}»"
            : $"L'ingresso «{name}» del blocco «{type}»";

    /// <summary>
    /// Verifica i campi secondo il catalogo della versione (§13.4): i blocchi senza campi non ne
    /// accettano, quelli con campi li richiedono con un valore ammesso.
    ///
    /// Le regole non sono scritte qui: arrivano dalla descrizione della versione del motore, che le
    /// dichiara per ogni blocco del proprio catalogo. Questo metodo traduce soltanto il documento
    /// Blockly nella forma che le regole verificano, così aggiungere un blocco con campi non
    /// costringe a modificare il validatore.
    /// </summary>
    private static void ValidateFields(JsonElement block, string type, BlockContext context)
    {
        var hasFields = block.TryGetProperty("fields", out var fields);
        if (hasFields && fields.ValueKind != JsonValueKind.Object)
        {
            throw Malformed(context.Label);
        }

        var present = hasFields
            ? fields.EnumerateObject().ToDictionary(
                field => field.Name, field => FieldValue(field.Value), StringComparer.Ordinal)
            : [];

        var specification = context.Block(type);
        if (specification is null)
        {
            // Un blocco che la versione non conosce è già stato rifiutato dal conteggio: qui non
            // resta nulla da verificare, e inventare una regola sarebbe peggio che non averla.
            return;
        }

        var known = specification.FieldNames.ToHashSet(StringComparer.Ordinal);
        ThrowIfExtraFields(present.Keys.ToHashSet(StringComparer.Ordinal), type, [.. known]);

        try
        {
            foreach (var rule in specification.Fields)
            {
                rule.Validate(type, present.TryGetValue(rule.Name, out var value) ? value : null);
            }

            // I campi di testo libero si verificano nella **forma**: devono esserci e devono essere
            // testo non vuoto. Il contenuto no: la domanda di «chiedi … e attendi» è la frase che il
            // bambino legge, e verificarla contro un elenco direbbe al docente quali domande può fare.
            foreach (var name in specification.TextFields)
            {
                if (!present.TryGetValue(name, out var value)
                    || value.Text.Trim().Length == 0)
                {
                    throw new CodiRequestException(
                        CodiErrorCodes.ValidationFailed,
                        $"Il blocco «{type}» richiede il campo «{name}»: è un testo da scrivere.");
                }
            }
        }
        catch (ArgumentException failure)
        {
            // Le regole del dominio nominano il campo per chi legge il codice; qui il messaggio va a
            // chi compone l'esercizio, quindi il riferimento al parametro si toglie.
            var marker = failure.Message.IndexOf(" (Parameter", StringComparison.Ordinal);

            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                marker > 0 ? failure.Message[..marker] : failure.Message);
        }
    }

    /// <summary>
    /// Traduce un campo del documento Blockly nel valore che le regole della versione verificano.
    /// Un campo di tipo inatteso resta senza valore numerico e senza scelta, così la regola lo
    /// rifiuta con il proprio messaggio invece di accettarlo per somiglianza.
    /// </summary>
    private static BlockFieldValue FieldValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetInt64(out var number) =>
            BlockFieldValue.NumberValue(number),
        JsonValueKind.String => BlockFieldValue.TextValue(value.GetString() ?? string.Empty),
        _ => new BlockFieldValue(Describe(value), null, null, null),
    };

    private static void ThrowIfExtraFields(IReadOnlySet<string> present, string type, IReadOnlyList<string> allowed)
    {
        foreach (var name in present)
        {
            if (!allowed.Contains(name, StringComparer.Ordinal))
            {
                throw new CodiRequestException(
                    CodiErrorCodes.ValidationFailed, $"Il blocco «{type}» non ha il campo «{name}».");
            }
        }
    }

    /// <summary>Testo di un valore JSON: il valore sbagliato va mostrato a chi lo ha scritto.</summary>
    private static string Describe(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.Null => "null",
        _ => value.ValueKind.ToString(),
    };

    private static CodiRequestException Malformed(string label) =>
        new(CodiErrorCodes.ValidationFailed, $"{Capitalize(label)} non ha la forma di un progetto a blocchi Blockly.");

    /// <summary>
    /// Stato di una verifica in corso: la definizione che stabilisce le regole e i contatori che i
    /// limiti dell'esercizio chiudono.
    ///
    /// La descrizione del motore esiste sempre: una <see cref="ExerciseDefinition"/> non si costruisce
    /// senza una versione registrata, e il progetto si verifica contro la definizione della versione
    /// pubblicata. Se mancasse sarebbe un errore di programmazione, e va detto subito invece di
    /// ripiegare sul punto di ingresso di un altro motore — che è esattamente il difetto che il
    /// registro ha eliminato.
    /// </summary>
    private sealed class BlockContext
    {
        private readonly ExerciseDefinition _definition;
        private readonly IExerciseEngineDescriptor _engine;
        private readonly HashSet<string> _known;
        private readonly HashSet<string> _allowed;
        private readonly HashSet<string> _identifiers = new(StringComparer.Ordinal);

        public BlockContext(ExerciseDefinition definition, string label)
        {
            _definition = definition;
            _engine =
                ExerciseBlockCatalog.Descriptor(definition.EngineVersion)
                ?? throw new InvalidOperationException(
                    $"La definizione dichiara il motore «{definition.EngineVersion}», che non è registrato: il progetto non può essere verificato.");
            _known = [.. _engine.Blocks.Select(block => block.Type)];
            _allowed = [.. definition.AllowedBlocks];
            Label = label;
        }

        /// <summary>Blocchi incontrati finora, compresi quelli scollegati dalla catena eseguibile.</summary>
        public int Count { get; private set; }

        /// <summary>Blocchi di ingresso incontrati: più di uno è un errore, nessuno è una bozza incompleta.</summary>
        public int Starts { get; private set; }

        public string Label { get; }

        /// <summary>Descrizione del blocco nel catalogo della versione, oppure <c>null</c> se non c'è.</summary>
        public ExerciseBlockSpec? Block(string type) => _engine.Block(type);

        /// <summary>Blocco che apre la catena eseguibile in questa versione del motore.</summary>
        public string EntryPointBlock => _engine.EntryPointBlock;

        /// <summary>Conta il blocco e verifica tipo, numero e appartenenza all'esercizio.</summary>
        public void CountBlock(string type)
        {
            if (!_known.Contains(type))
            {
                throw new CodiRequestException(
                    CodiErrorCodes.ValidationFailed,
                    $"{Capitalize(Label)} usa il blocco «{type}», che la versione del motore «{_definition.EngineVersion}» non conosce.");
            }

            if (!_allowed.Contains(type))
            {
                throw new CodiRequestException(
                    CodiErrorCodes.ValidationFailed,
                    $"{Capitalize(Label)} usa il blocco «{type}», che questo esercizio non mette a disposizione.");
            }

            Count += 1;
            if (Count > _definition.Limits.MaxBlocks)
            {
                throw new CodiRequestException(
                    CodiErrorCodes.ValidationFailed,
                    $"{Capitalize(Label)} ha più di {_definition.Limits.MaxBlocks} blocchi, il limite dell'esercizio.");
            }

            if (string.Equals(type, EntryPointBlock, StringComparison.Ordinal))
            {
                Starts += 1;
            }
        }

        /// <summary>Registra l'identificativo del blocco, se Blockly ne ha salvato uno.</summary>
        public void RegisterIdentifier(string identifier)
        {
            if (!_identifiers.Add(identifier))
            {
                throw new CodiRequestException(
                    CodiErrorCodes.ValidationFailed,
                    $"{Capitalize(Label)} contiene due blocchi con lo stesso identificativo «{identifier}».");
            }
        }

        /// <summary>
        /// Verifica che il livello raggiunto sia entro il limite di annidamento. La profondità è
        /// quella dei blocchi dentro una sequenza ripetuta: la catena di istruzioni allo stesso
        /// livello non è annidamento.
        /// </summary>
        public void EnsureDepth(int depth)
        {
            if (depth > _definition.Limits.MaxNestingDepth)
            {
                throw new CodiRequestException(
                    CodiErrorCodes.ValidationFailed,
                    $"{Capitalize(Label)} annida i blocchi oltre il limite di {_definition.Limits.MaxNestingDepth} livelli.");
            }
        }
    }
}
