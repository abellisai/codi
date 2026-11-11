using System.Text.Json;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Domain.Exercises;

namespace Qwerty.Codi.Application.Exercises;

/// <summary>
/// Legge un progetto a blocchi di <c>ValueProgram</c> e ne ricava le istruzioni che il motore esegue
/// (§13.9). È la controparte .NET di `value-program-compiler.ts`, e le due devono accettare lo stesso
/// documento: il server corregge il lavoro di un alunno, quindi non può dipendere da ciò che il
/// browser gli manda già interpretato.
///
/// La forma letta è quella che Blockly produce e che il server già verifica al salvataggio: gli
/// ingressi sono proprietà il cui valore è il blocco, e la catena prosegue in <c>next</c>.
///
/// La lettura è **totale**: se qualcosa non si può tradurre, non si restituisce un programma a metà,
/// si produce una diagnosi. Le diagnosi sono in italiano e nominano il blocco, perché chi legge è
/// l'alunno o il docente, non un programma.
/// </summary>
public static class ValueProgramJson
{
    /// <summary>
    /// Compila il progetto. <paramref name="variableNames"/> sono le variabili dichiarate
    /// dall'esercizio: un nome fuori elenco è un programma da correggere.
    /// </summary>
    public static IReadOnlyList<ValueInstruction> Compile(
        string workspaceJson,
        IReadOnlyList<string> variableNames)
    {
        ArgumentNullException.ThrowIfNull(variableNames);

        if (!TryRead(workspaceJson, out var root) || !root.TryGetProperty("blocks", out var container))
        {
            throw Invalid("Il progetto è vuoto: comincia dal blocco «quando si avvia».");
        }

        if (!container.TryGetProperty("blocks", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("Il programma è vuoto: comincia dal blocco «quando si avvia».");
        }

        var avvii = list.EnumerateArray()
            .Where(block => block.ValueKind == JsonValueKind.Object
                && Type(block) == ValueBlockCatalog.Start)
            .ToList();

        if (avvii.Count == 0)
        {
            throw Invalid("Manca il blocco «quando si avvia»: senza, il programma non parte.");
        }

        if (avvii.Count > 1)
        {
            throw Invalid($"Ci sono {avvii.Count} blocchi «quando si avvia»: ne basta uno.");
        }

        var compiler = new Compiler(variableNames);

        return compiler.Sequence(Block(avvii[0], "next"));
    }

    /// <summary>
    /// Esegue il programma su **un caso di prova** e restituisce ciò che ha mostrato. È la
    /// correzione: il server esegue il lavoro dell'alunno invece di fidarsi del browser.
    ///
    /// I valori del caso entrano come parametri, ed è per questo che l'esecuzione è riproducibile:
    /// il motore non legge nulla dall'esterno se non ciò che il caso dichiara.
    /// </summary>
    public static ValueRunState RunOnCase(
        IReadOnlyList<ValueInstruction> program,
        ValueProgramData data,
        ValueTestCase testCase,
        int maxSteps)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(testCase);

        return ValueProgramRunner
            .Start(program, data.VariableNames, data.InitialValues, testCase.Inputs, maxSteps)
            .RunToRest();
    }

    /// <summary>
    /// Esito della correzione su tutti i casi di prova: superato quando ogni caso mostra ciò che
    /// doveva. Il primo caso fallito è il più utile da raccontare, perché è quello da correggere.
    /// </summary>
    public static ValueProgramOutcome Evaluate(
        IReadOnlyList<ValueInstruction> program,
        ValueProgramData data,
        int maxSteps)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(data);

        foreach (var testCase in data.TestCases)
        {
            var state = RunOnCase(program, data, testCase, maxSteps);

            if (state.Status == ValueRunStatus.WaitingForValue)
            {
                // Un caso di prova che lascia la corsa in attesa è un esercizio che nessuno può
                // completare: il programma chiede più valori di quanti il caso ne dichiari. Non è un
                // errore dell'alunno, ed è un errore che il server non deve attribuirgli.
                return new ValueProgramOutcome(
                    false, false,
                    "Il programma chiede un valore che il caso di prova non gli dà.");
            }

            if (state.Status is ValueRunStatus.Refused or ValueRunStatus.BudgetExhausted)
            {
                return new ValueProgramOutcome(false, false, state.PauseReason);
            }

            if (!state.Output.SequenceEqual(testCase.ExpectedOutput, StringComparer.Ordinal))
            {
                return new ValueProgramOutcome(
                    false,
                    true,
                    $"Con i valori {string.Join(", ", testCase.Inputs)} il programma mostra «{string.Join(" ", state.Output)}» invece di «{string.Join(" ", testCase.ExpectedOutput)}».");
            }
        }

        return new ValueProgramOutcome(true, true, string.Empty);
    }

    private static bool TryRead(string json, out JsonElement root)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            root = default;

            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();

            return root.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            root = default;

            return false;
        }
    }

    private static string Type(JsonElement block) =>
        block.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
            ? type.GetString() ?? string.Empty
            : string.Empty;

    private static string Id(JsonElement block) =>
        block.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>
    /// Il blocco collegato a un ingresso o alla catena, se c'è.
    ///
    /// I due nomi vivono in posti diversi, e confonderli è l'errore che rende muto ogni blocco con un
    /// ingresso: <c>next</c> è una proprietà del blocco, mentre un ingresso è una proprietà di
    /// <c>inputs</c>. Il metodo cerca in entrambi i posti, così il chiamante nomina ciò che vuole —
    /// l'ingresso «VALUE» o la catena «next» — e non dove sta.
    /// </summary>
    private static JsonElement? Block(JsonElement owner, string inputName)
    {
        var connection = default(JsonElement);

        if (owner.TryGetProperty("inputs", out var inputs)
            && inputs.ValueKind == JsonValueKind.Object
            && inputs.TryGetProperty(inputName, out var nested))
        {
            connection = nested;
        }
        else if (owner.TryGetProperty(inputName, out var direct))
        {
            connection = direct;
        }
        else
        {
            return null;
        }

        if (connection.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return connection.TryGetProperty("block", out var block) && block.ValueKind == JsonValueKind.Object
            ? block
            : null;
    }

    private static CodiRequestException Invalid(string message) =>
        new(CodiErrorCodes.ValidationFailed, message);

    /// <summary>Compilatore: legge una catena di comandi e le espressioni che vi sono annidate.</summary>
    private sealed class Compiler(IReadOnlyList<string> variableNames)
    {
        public IReadOnlyList<ValueInstruction> Sequence(JsonElement? first)
        {
            var instructions = new List<ValueInstruction>();
            var current = first;
            var guard = 0;

            while (current is { } block && guard < 1_000)
            {
                guard += 1;
                var instruction = Command(block);
                if (instruction is not null)
                {
                    instructions.Add(instruction);
                }

                current = Block(block, "next");
            }

            return instructions;
        }

        private ValueInstruction? Command(JsonElement block)
        {
            var type = Type(block);
            var id = Id(block);

            switch (type)
            {
                case ValueBlockCatalog.Start:
                    // Un secondo avvio dentro la catena non apre il programma: è un blocco appoggiato
                    // dove non si esegue.
                    throw Invalid("Il blocco «quando si avvia» apre il programma: non si attacca in mezzo.");

                case ValueBlockCatalog.Set:
                    return new SetVariable(
                        id, Variable(block), Expression(Block(block, ValueBlockCatalog.ValueInput), id));

                case ValueBlockCatalog.Change:
                    // «cambia Punti di 1»: una variabile da cambiare e una quantità da sommare.
                    return new ChangeVariable(
                        id, Variable(block), Expression(Block(block, ValueBlockCatalog.ChangeField), id));

                case ValueBlockCatalog.Show:
                    return new ShowValue(
                        id, Expression(Block(block, ValueBlockCatalog.ValueInput), id));

                case ValueBlockCatalog.Ask:
                    // «chiedi [domanda?] e attendi»: la domanda è testo libero, non una variabile. Il
                    // valore letto resta nel reporter «risposta» finché il programma non lo memorizza
                    // con «porta Numero a (risposta)».
                    return new AskValue(id, null, Field(block, ValueBlockCatalog.QuestionField));

                case ValueBlockCatalog.If:
                    {
                        var condition = Comparison(Block(block, ValueBlockCatalog.ValueInput), id);

                        return new IfElse(
                            id,
                            condition,
                            Sequence(Block(block, ValueBlockCatalog.ThenInput)),
                            Sequence(Block(block, ValueBlockCatalog.ElseInput)));
                    }

                case ValueBlockCatalog.Repeat:
                    {
                        var count = Number(block, ValueBlockCatalog.RepeatCountField, "quante volte ripetere");
                        if (count < 1)
                        {
                            throw Invalid("Un ciclo deve ripetere almeno una volta: scrivi un numero da 1 in su.");
                        }

                        return new RepeatTimes(
                            id, (int)count, Sequence(Block(block, ValueBlockCatalog.RepeatBodyInput)));
                    }

                default:
                    throw Invalid($"Il blocco «{type}» non è un comando di questo esercizio.");
            }
        }

        private ValueExpression Expression(JsonElement? block, string parentId)
        {
            if (block is not { } value)
            {
                throw Invalid("Manca il valore dentro il blocco: trascinaci un numero o una variabile.");
            }

            var type = Type(value);
            var id = Id(value);

            switch (type)
            {
                case ValueBlockCatalog.Number:
                    {
                        var number = Number(value, ValueBlockCatalog.NumberField, "il numero");
                        if (Math.Abs(number) > ValueProgramEngine.MaxInteger)
                        {
                            throw Invalid($"Il numero supera il massimo ammesso ({ValueProgramEngine.MaxInteger}).");
                        }

                        return new LiteralValue((int)number);
                    }

                case ValueBlockCatalog.Variable:
                    return new VariableValue(Variable(value));

                case ValueBlockCatalog.Answer:
                    // Il reporter «risposta»: non ha campi, perché la risposta non si sceglie.
                    return new AnswerValue();

                case ValueBlockCatalog.Add:
                    return new AddValue(
                        Expression(Block(value, ValueBlockCatalog.ValueInput), id),
                        Expression(Block(value, ValueBlockCatalog.OtherInput), id));

                case ValueBlockCatalog.Compare:
                    // Un confronto non è un valore: sta in un «se». Il motore lo rifiuterebbe dopo;
                    // qui si dice prima, e il messaggio nomina il blocco.
                    throw Invalid("Un confronto decide un ramo: non si può sommare né mostrare.");

                default:
                    throw Invalid($"Il blocco «{type}» non produce un valore in questo esercizio.");
            }
        }

        private ValueComparison Comparison(JsonElement? block, string parentId)
        {
            if (block is not { } value || Type(value) != ValueBlockCatalog.Compare)
            {
                throw Invalid("Il blocco «se» vuole un confronto: trascinaci dentro «confronta».");
            }

            var id = Id(value);
            var left = Expression(Block(value, ValueBlockCatalog.ValueInput), id);
            var right = Expression(Block(value, ValueBlockCatalog.OtherInput), id);
            var relation = Field(value, ValueBlockCatalog.CompareField);

            var op = relation switch
            {
                nameof(ComparisonOperator.Greater) => ComparisonOperator.Greater,
                nameof(ComparisonOperator.Less) => ComparisonOperator.Less,
                nameof(ComparisonOperator.Equal) => ComparisonOperator.Equal,
                _ => throw Invalid($"La relazione «{relation}» non è fra quelle previste."),
            };

            return new ValueComparison(left, op, right);
        }

        private string Variable(JsonElement block)
        {
            var name = Field(block, ValueBlockCatalog.VariableField);

            if (name.Length == 0)
            {
                throw Invalid("Il blocco non dice su quale variabile lavorare: scrivine il nome.");
            }

            if (!variableNames.Contains(name, StringComparer.Ordinal))
            {
                throw Invalid(
                    $"La variabile «{name}» non è fra quelle dell'esercizio: {string.Join(", ", variableNames)}.");
            }

            return name;
        }

        private static long Number(JsonElement block, string field, string what)
        {
            if (!block.TryGetProperty("fields", out var fields)
                || !fields.TryGetProperty(field, out var value))
            {
                throw Invalid($"Manca {what}.");
            }

            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt64(out var number) => number,
                JsonValueKind.String when long.TryParse(
                    value.GetString(), out var parsed) => parsed,
                _ => throw Invalid($"Il valore di {what} non è un numero."),
            };
        }

        /// <summary>Testo di un campo, o vuoto: un campo assente è un problema che si nomina dopo.</summary>
        private static string Field(JsonElement block, string field) =>
            block.TryGetProperty("fields", out var fields)
                && fields.TryGetProperty(field, out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
    }
}

/// <summary>
/// Esito della correzione sui casi di prova. <see cref="ProgramValid"/> distingue un programma che
/// non funziona da un programma che non si esegue: sono due cose diverse per chi deve correggere, e
/// dirle con la stessa frase lascerebbe l'alunno a cercare l'errore nel posto sbagliato.
/// </summary>
public sealed record ValueProgramOutcome(bool Passed, bool ProgramValid, string Problem);
