using System.Text.Json.Serialization;

namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Stato di una corsa di un programma che conserva valori.
///
/// I nomi dei campi sono **fissati** dagli attributi, e non lasciati alla convenzione del
/// serializzatore: le chiavi di <c>Variables</c> sono i nomi delle variabili scelti dal docente, e una
/// convenzione che li riscrivesse — `Punti` diventerebbe `punti` — romperebbe il formato di trasporto
/// fra browser e server, che è un confine fra due build e non può dipendere da un'impostazione.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ValueRunStatus>))]
public enum ValueRunStatus
{
    /// <summary>Ferma: non è ancora partita, oppure è stata ripresa e attende valori.</summary>
    Ready,

    /// <summary>
    /// Ferma **in attesa di un valore** che l'alunno deve fornire: il programma ha eseguito tutto
    /// quello che poteva fare senza quel valore. È lo stato che il percorso non ha, perché una corsa
    /// sulla griglia non chiede nulla a chi la guarda.
    /// </summary>
    WaitingForValue,

    /// <summary>Conclusa: il programma ha mostrato il suo risultato senza chiedere altri valori.</summary>
    Finished,

    /// <summary>
    /// Interrotta perché il programma non è valido per questa versione del motore — una variabile
    /// non dichiarata, un confronto usato come valore — oppure perché il budget di passi è finito.
    /// I due casi si distinguono dal motivo, che è la diagnosi mostrata a chi legge.
    /// </summary>
    Refused,

    /// <summary>Interrotta perché il budget di passi è finito: il programma non termina.</summary>
    BudgetExhausted,
}

/// <summary>
/// Un fotogramma dello stack di esecuzione: dove siamo dentro una sequenza, e da quale fotogramma
/// si proviene. Il percorso salvato conserva solo identificativo e indice; il legame con il genitore
/// si ricostruisce dalla struttura del programma.
/// </summary>
public sealed record ValueFrame(
    [property: JsonPropertyName("blockId")] string SourceBlockId,
    [property: JsonPropertyName("index")] int Index);

/// <summary>
/// Stato serializzabile di una corsa: è il checkpoint di <c>valueprogram-1</c> (§13.7). Contiene
/// **l'ambiente delle variabili**, **la risposta** dell'ultima lettura, l'uscita prodotta, i valori
/// ancora da consumare, lo stack di esecuzione e lo stato. Non contiene il programma: un checkpoint
/// vale per il progetto da cui è nato, e riprende dall'esatta istruzione grazie al percorso degli
/// indici.
/// </summary>
public sealed record ValueRunState(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("engineVersion")] string EngineVersion,
    [property: JsonPropertyName("status")] ValueRunStatus Status,
    [property: JsonPropertyName("frames")] IReadOnlyList<ValueFrame> Frames,
    [property: JsonPropertyName("variables")] IReadOnlyDictionary<string, int> Variables,
    [property: JsonPropertyName("answer")] int Answer,
    [property: JsonPropertyName("output")] IReadOnlyList<string> Output,
    [property: JsonPropertyName("pendingValues")] IReadOnlyList<int> PendingValues,
    [property: JsonPropertyName("consumedSteps")] int ConsumedSteps,
    [property: JsonPropertyName("executions")] int Executions,
    [property: JsonPropertyName("activeBlockId")] string ActiveBlockId,
    [property: JsonPropertyName("pauseReason")] string PauseReason)
{
    /// <summary>Vero se la corsa può ancora avanzare, con o senza un valore dall'esterno.</summary>
    public bool CanContinue => Status is ValueRunStatus.Ready or ValueRunStatus.WaitingForValue;
}

/// <summary>Un passo di esecuzione: lo stato raggiunto e la diagnosi, vuota quando è andato bene.</summary>
public sealed record ValueStepResult(ValueRunState State, string Problem);

/// <summary>
/// Esegue un programma che conserva valori, **un passo alla volta**.
///
/// Il passo è l'unità di osservazione e di pausa: chi governa la corsa — il worker del player, la
/// prova locale dell'alunno, la correzione del server — chiede un passo e riceve lo stato, senza
/// conoscere l'interprete. Ogni operazione consuma budget, anche quando non produce nulla di
/// visibile: così un ciclo vuoto non aggira il limite (§13.5).
///
/// La differenza che conta rispetto al percorso è la **sospensione**: quando l'istruzione chiede un
/// valore che non c'è, la corsa si ferma lì, con le variabili come sono, e riprende dallo stesso
/// punto quando il valore arriva. Non si ricomincia: è il senso di conservare un valore.
/// </summary>
public sealed class ValueProgramRunner
{
    private readonly IReadOnlyList<string> _variableNames;
    private readonly IReadOnlyDictionary<string, int> _defaults;
    private readonly int _maxSteps;

    private readonly List<MutableFrame> _frames = [];
    private readonly Dictionary<string, int> _variables = new(StringComparer.Ordinal);
    private readonly HashSet<string> _assigned = new(StringComparer.Ordinal);
    private readonly List<string> _output = [];
    private readonly List<int> _pending;

    private ValueRunStatus _status = ValueRunStatus.Ready;
    private int _answer;
    private int _consumed;
    private int _executions;
    private string _activeBlockId = string.Empty;
    private string _pauseReason = string.Empty;

    private ValueProgramRunner(
        IReadOnlyList<string> variableNames,
        IReadOnlyDictionary<string, int> defaults,
        IReadOnlyList<int> parameters,
        int maxSteps)
    {
        _variableNames = variableNames;
        _defaults = defaults;
        _maxSteps = maxSteps;
        _pending = [.. parameters];
        _pending.Reverse();
    }

    /// <summary>Avvia una corsa nuova da zero.</summary>
    public static ValueProgramRunner Start(
        IReadOnlyList<ValueInstruction> program,
        IReadOnlyList<string> variableNames,
        IReadOnlyDictionary<string, int>? defaults,
        IReadOnlyList<int> parameters,
        int maxSteps)
    {
        ArgumentNullException.ThrowIfNull(program);

        var runner = new ValueProgramRunner(
            variableNames, defaults ?? new Dictionary<string, int>(StringComparer.Ordinal),
            parameters, maxSteps);
        runner.Reset(program);

        return runner;
    }

    /// <summary>Riprende una corsa da uno stato salvato, ricostruendo lo stack dal percorso.</summary>
    public static ValueProgramRunner Resume(
        IReadOnlyList<ValueInstruction> program,
        IReadOnlyList<string> variableNames,
        IReadOnlyDictionary<string, int>? defaults,
        ValueRunState state,
        int maxSteps)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(state);

        var runner = new ValueProgramRunner(
            variableNames, defaults ?? new Dictionary<string, int>(StringComparer.Ordinal),
            state.PendingValues, maxSteps);

        foreach (var pair in state.Variables)
        {
            runner._variables[pair.Key] = pair.Value;
            // Tutte le variabili di uno stato ripreso sono state assegnate: altrimenti non sarebbero
            // lì. Un valore mai impostato non entra in un checkpoint, perché la corsa si ferma prima.
            runner._assigned.Add(pair.Key);
        }

        runner._answer = state.Answer;
        runner._answer = state.Answer;
        runner._output.AddRange(state.Output);
        runner._consumed = state.ConsumedSteps;
        runner._executions = state.Executions;
        runner._pauseReason = state.PauseReason;
        // La corsa riprende **in cammino**: uno stato di attesa con i valori disponibili non è più
        // un'attesa, altrimenti il primo passo si rifiuterebbe di muoversi.
        runner._status = state.Status == ValueRunStatus.WaitingForValue && runner._pending.Count > 0
            ? ValueRunStatus.Ready
            : state.Status;
        runner.ReEnter(program, state.Frames);

        return runner;
    }

    /// <summary>Stato corrente, pronto per essere osservato o salvato.</summary>
    public ValueRunState State => new(
        ValueProgramEngine.CheckpointSchemaVersion,
        ValueProgramEngine.Version,
        _status,
        [.. _frames.Select(frame => new ValueFrame(frame.SourceBlockId, frame.Index))],
        new Dictionary<string, int>(_variables, StringComparer.Ordinal),
        _answer,
        [.. _output],
        [.. _pending],
        _consumed,
        _executions,
        _activeBlockId,
        _pauseReason);

    /// <summary>
    /// Un passo. Lo stato raggiunto è sempre osservabile; la diagnosi è vuota quando il passo è
    /// andato a buon fine, perché un passo riuscito non è un problema da spiegare.
    ///
    /// Un passo consuma budget **solo se esegue qualcosa**: chiudere una sequenza conclusa è
    /// contabilità dello stack, non esecuzione, e non va addebitata al programma. Vale a dire che un
    /// ciclo di tre giri costa nove passi — tre per giro, quanti ne esegue — e non uno in più per
    /// ogni ritorno al genitore.
    /// </summary>
    public ValueStepResult Step()
    {
        if (!State.CanContinue)
        {
            return new ValueStepResult(State, _pauseReason);
        }

        if (_consumed >= _maxSteps)
        {
            _status = ValueRunStatus.BudgetExhausted;
            _pauseReason = "Il programma ha consumato tutti i passi a disposizione.";

            return new ValueStepResult(State, _pauseReason);
        }

        while (true)
        {
            var frame = _frames[^1];
            if (frame.Index >= frame.Instructions.Count)
            {
                // La sequenza è finita: si torna al genitore, ripetendo il corpo se era un ciclo.
                if (!Leave(frame))
                {
                    return new ValueStepResult(State, _pauseReason);
                }

                continue;
            }

            _consumed += 1;
            var problem = Execute(frame.Instructions[frame.Index], frame);

            return new ValueStepResult(State, problem);
        }
    }

    /// <summary>Esegue fino a fermarsi: alla conclusione, all'attesa di un valore o a un rifiuto.</summary>
    public ValueRunState RunToRest()
    {
        while (_status == ValueRunStatus.Ready)
        {
            Step();
        }

        return State;
    }

    private void Reset(IReadOnlyList<ValueInstruction> program)
    {
        _frames.Clear();
        _variables.Clear();
        _output.Clear();
        _status = ValueRunStatus.Ready;
        _consumed = 0;
        _executions = 0;
        _activeBlockId = string.Empty;
        _pauseReason = string.Empty;
        _answer = 0;

        foreach (var name in _variableNames)
        {
            _variables[name] = _defaults.TryGetValue(name, out var value) ? value : 0;
        }

        _assigned.Clear();
        foreach (var name in _defaults.Keys)
        {
            // Un valore iniziale dichiarato dalla definizione è un valore assegnato: la definizione
            // ha scelto di partire da lì, e non è un valore che manca.
            if (_variables.ContainsKey(name))
            {
                _assigned.Add(name);
            }
        }

        _frames.Add(MutableFrame.ForSequence(program, string.Empty, FrameKind.Root, 0, 0));
    }

    /// <summary>
    /// Ricostruisce lo stack da un percorso salvato: scende di sequenza in sequenza seguendo gli
    /// indici e i rami, così la corsa riprende dall'esatta istruzione invece che dall'inizio.
    ///
    /// L'indice di ritorno non si conserva: si ricalcola, perché dipende dalla struttura del
    /// programma e non dall'esecuzione. Conservarlo avrebbe voluto dire poterlo trovare divergente.
    /// </summary>
    private void ReEnter(IReadOnlyList<ValueInstruction> program, IReadOnlyList<ValueFrame> path)
    {
        _frames.Clear();

        if (path.Count == 0)
        {
            _frames.Add(MutableFrame.ForSequence(program, string.Empty, FrameKind.Root, 0, 0));

            return;
        }

        IReadOnlyList<ValueInstruction> instructions = program;
        var returnIndex = 0;

        for (var depth = 0; depth < path.Count; depth += 1)
        {
            var step = path[depth];
            if (step.Index < 0 || step.Index >= instructions.Count)
            {
                throw new ArgumentException(
                    "Il checkpoint indica un'istruzione che non esiste in questo progetto.", nameof(path));
            }

            var instruction = instructions[step.Index];
            _frames.Add(MutableFrame.ForSequence(
                instructions, step.SourceBlockId, KindOf(step.SourceBlockId), returnIndex, step.Index));

            returnIndex = step.Index + 1;
            instructions = Next(instruction, step.SourceBlockId);
        }

        if (_frames.Count == 0)
        {
            _frames.Add(MutableFrame.ForSequence(program, string.Empty, FrameKind.Root, 0, 0));
        }
    }

    /// <summary>La sequenza che un'istruzione contiene, se ne contiene una.</summary>
    private static IReadOnlyList<ValueInstruction> Next(ValueInstruction instruction, string frameId) =>
        instruction switch
        {
            IfElse branch when ValueFrameId.IsOtherwise(frameId, branch) => branch.Otherwise,
            IfElse branch => branch.Then,
            RepeatTimes loop => loop.Body,
            _ => [],
        };

    private static FrameKind KindOf(string frameId) =>
        frameId.EndsWith(":else", StringComparison.Ordinal) ? FrameKind.Otherwise
        : frameId.Length == 0 ? FrameKind.Root
        : FrameKind.Body;

    /// <summary>
    /// Esegue un'istruzione e aggiorna lo stack. Restituisce la diagnosi, vuota quando è andata bene.
    /// </summary>
    private string Execute(ValueInstruction instruction, MutableFrame frame)
    {
        _activeBlockId = instruction.SourceBlockId;

        switch (instruction)
        {
            case SetVariable set:
                return Assign(set.Variable, set.Value, frame);

            case ChangeVariable change:
                {
                    // «cambia Punti di 1»: legge, somma e riscrive. La variabile deve esistere **e** avere
                    // già un valore, altrimenti l'incremento partirebbe da zero: è la stessa regola della
                    // somma, applicata all'istruzione che il corso usa per contare.
                    if (!_variables.ContainsKey(change.Variable))
                    {
                        return Refuse($"La variabile «{change.Variable}» non è dichiarata nell'esercizio.");
                    }

                    var delta = Evaluate(change.Delta);
                    if (delta.Problem.Length > 0)
                    {
                        return Refuse(delta.Problem);
                    }

                    var corrente = Evaluate(new VariableValue(change.Variable));
                    if (corrente.Problem.Length > 0)
                    {
                        return Refuse(corrente.Problem);
                    }

                    _variables[change.Variable] = corrente.Value + delta.Value;
                    _assigned.Add(change.Variable);
                    frame.Index += 1;

                    return string.Empty;
                }

            case ShowValue show:
                {
                    var evaluation = Evaluate(show.Value);
                    if (evaluation.Problem.Length > 0)
                    {
                        return Refuse(evaluation.Problem);
                    }

                    _output.Add(evaluation.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    frame.Index += 1;

                    return string.Empty;
                }

            case AskValue ask:
                return Ask(ask, frame);

            case IfElse branch:
                return Branch(branch, frame);

            case RepeatTimes loop:
                return Repeat(loop, frame);

            default:
                return Refuse($"L'istruzione «{instruction.SourceBlockId}» non è prevista da questo motore.");
        }
    }

    private string Assign(string variable, ValueExpression expression, MutableFrame frame)
    {
        if (!_variables.ContainsKey(variable))
        {
            // Una variabile che la definizione non dichiara non esiste: il programma non può
            // inventarne una, perché è la definizione a dire che cosa si può nominare.
            return Refuse($"La variabile «{variable}» non è dichiarata nell'esercizio.");
        }

        var evaluation = Evaluate(expression);
        if (evaluation.Problem.Length > 0)
        {
            return Refuse(evaluation.Problem);
        }

        _variables[variable] = evaluation.Value;
        _assigned.Add(variable);
        frame.Index += 1;

        return string.Empty;
    }

    /// <summary>
    /// Chiede un valore. Se un parametro è ancora disponibile lo consuma e prosegue; altrimenti la
    /// corsa si ferma **su questa istruzione**, senza avanzare, e riprenderà da qui.
    ///
    /// La risposta resta a parte dall'ambiente delle variabili, perché è un reporter suo: chiedere di
    /// nuovo la sostituisce, mentre le variabili conservano i valori memorizzati.
    /// </summary>
    private string Ask(AskValue ask, MutableFrame frame)
    {
        if (ask.Variable is { Length: > 0 } variable && !_variables.ContainsKey(variable))
        {
            return Refuse($"La variabile «{variable}» non è dichiarata nell'esercizio.");
        }

        if (_pending.Count == 0)
        {
            _status = ValueRunStatus.WaitingForValue;
            _pauseReason = ask.Question.Length > 0
                ? $"In attesa della risposta a «{ask.Question}»."
                : "In attesa di un valore.";

            return string.Empty;
        }

        _answer = _pending[^1];
        _pending.RemoveAt(_pending.Count - 1);

        if (ask.Variable is { Length: > 0 } destinazione)
        {
            _variables[destinazione] = _answer;
            _assigned.Add(destinazione);
        }

        frame.Index += 1;

        return string.Empty;
    }

    private string Branch(IfElse branch, MutableFrame frame)
    {
        var left = Evaluate(branch.Condition.Left);
        if (left.Problem.Length > 0)
        {
            return Refuse(left.Problem);
        }

        var right = Evaluate(branch.Condition.Right);
        if (right.Problem.Length > 0)
        {
            return Refuse(right.Problem);
        }

        var taken = branch.Condition.Operator switch
        {
            ComparisonOperator.Greater => left.Value > right.Value,
            ComparisonOperator.Less => left.Value < right.Value,
            ComparisonOperator.Equal => left.Value == right.Value,
            _ => false,
        };

        // L'istruzione è compiuta per il genitore: si avanza qui e si ricorda **dove** riprendere
        // quando il ramo sarà concluso. Il fotogramma del ramo «altrimenti» porta un identificativo
        // distinto, così il percorso salvato dice quale ramo si stava eseguendo.
        frame.Index += 1;
        var returnIndex = frame.Index;

        if (taken)
        {
            Push(branch.Then, branch.SourceBlockId, FrameKind.Body, returnIndex);
        }
        else if (branch.Otherwise.Count > 0)
        {
            Push(branch.Otherwise, ValueFrameId.Otherwise(branch), FrameKind.Otherwise, returnIndex);
        }

        return string.Empty;
    }

    private string Repeat(RepeatTimes loop, MutableFrame frame)
    {
        // Come per il ramo: il genitore avanza subito e ricorda dove riprendere. Il corpo vive in un
        // fotogramma proprio, che riparte a ogni giro senza toccare il genitore: avanzare anche alla
        // chiusura di ogni giro salterebbe l'istruzione che segue il ciclo.
        frame.Index += 1;
        var returnIndex = frame.Index;

        if (loop.Count <= 0)
        {
            return string.Empty;
        }

        Push(loop.Body, loop.SourceBlockId, FrameKind.Loop, returnIndex);
        _frames[^1].Remaining = loop.Count;

        return string.Empty;
    }

    /// <summary>
    /// Chiude un fotogramma concluso. Un corpo di ciclo con giri ancora da fare riparte dall'inizio;
    /// altrimenti si torna al genitore **all'indice di ritorno** — l'istruzione che segue quella che
    /// aveva aperto questa sequenza — e non all'istruzione dopo quella corrente. Restituisce falso
    /// quando la corsa è finita: non c'è più nulla da eseguire.
    /// </summary>
    private bool Leave(MutableFrame frame)
    {
        _frames.RemoveAt(_frames.Count - 1);

        if (frame.Kind == FrameKind.Loop && frame.Remaining > 0)
        {
            frame.Remaining -= 1;
            if (frame.Remaining > 0)
            {
                // Un ciclo: il corpo è concluso una volta e deve ripetersi dall'inizio.
                frame.Index = 0;
                _frames.Add(frame);

                return true;
            }
        }

        if (_frames.Count == 0)
        {
            _status = ValueRunStatus.Finished;
            _pauseReason = string.Empty;

            return false;
        }

        _frames[^1].Index = frame.ReturnIndex;

        return true;
    }

    private void Push(IReadOnlyList<ValueInstruction> body, string sourceBlockId, FrameKind kind, int returnIndex) =>
        _frames.Add(MutableFrame.ForSequence(body, sourceBlockId, kind, returnIndex, 0));

    private string Refuse(string problem)
    {
        _status = ValueRunStatus.Refused;
        _pauseReason = problem;

        return problem;
    }

    /// <summary>Valuta un'espressione; un problema è un testo vuoto quando è andata a buon fine.</summary>
    private (int Value, string Problem) Evaluate(ValueExpression expression)
    {
        _consumed += 1;

        switch (expression)
        {
            case LiteralValue literal:
                return (literal.Value, string.Empty);

            case AnswerValue:
                // Il reporter «risposta»: l'ultimo valore letto. Non è una variabile del docente, e
                // vale quello che l'ultima lettura ha prodotto — anche se nel frattempo il programma
                // ha memorizzato quella risposta in una variabile.
                return (_answer, string.Empty);

            case VariableValue variable:
                return _variables.TryGetValue(variable.Variable, out var value)
                    ? _assigned.Contains(variable.Variable)
                        ? (value, string.Empty)
                        // Un valore mai impostato non vale zero: valere zero in silenzio darebbe un
                        // risultato sbagliato con l'aria di essere giusto, che è il peggior esito
                        // didattico possibile. Il motore dice che cosa manca.
                        : (0, $"La variabile «{variable.Variable}» non ha ancora un valore: il programma deve prima leggerlo o assegnarlo.")
                    : (0, $"La variabile «{variable.Variable}» non è dichiarata nell'esercizio.");

            case AddValue sum:
                {
                    var left = Evaluate(sum.Left);
                    if (left.Problem.Length > 0)
                    {
                        return left;
                    }

                    var right = Evaluate(sum.Right);

                    return right.Problem.Length > 0
                        ? right
                        : (left.Value + right.Value, string.Empty);
                }

            default:
                // Un confronto non è un valore: sommarlo o mostrarlo non vuol dire nulla, e il motore
                // lo dice invece di scegliere un numero a caso.
                return (0, "Un confronto non è un valore: non si può sommare né mostrare.");
        }
    }

    /// <summary>Che cosa rappresenta un fotogramma: serve a sapere come chiuderlo.</summary>
    private enum FrameKind
    {
        Root,
        Body,
        Otherwise,
        Loop,
    }

    /// <summary>Fotogramma modificabile: il percorso salvato ne conserva solo indice e provenienza.</summary>
    private sealed class MutableFrame
    {
        private MutableFrame(
            IReadOnlyList<ValueInstruction> instructions,
            string sourceBlockId,
            FrameKind kind,
            int returnIndex,
            int index)
        {
            Instructions = instructions;
            SourceBlockId = sourceBlockId;
            Kind = kind;
            ReturnIndex = returnIndex;
            Index = index;
        }

        public IReadOnlyList<ValueInstruction> Instructions { get; }

        public string SourceBlockId { get; }

        public FrameKind Kind { get; }

        /// <summary>
        /// Dove riprende il genitore quando questa sequenza è conclusa: l'istruzione che segue quella
        /// che l'ha aperta. Non è «l'indice dopo quello corrente», perché un ciclo riparte più volte.
        /// </summary>
        public int ReturnIndex { get; }

        public int Index { get; set; }

        /// <summary>Giri ancora da fare quando il fotogramma è il corpo di un ciclo.</summary>
        public int Remaining { get; set; }

        public static MutableFrame ForSequence(
            IReadOnlyList<ValueInstruction> instructions,
            string sourceBlockId,
            FrameKind kind,
            int returnIndex,
            int index) =>
            new(instructions, sourceBlockId, kind, returnIndex, index);
    }
}

/// <summary>Gli identificativi dei fotogrammi che non corrispondono a un blocco solo.</summary>
internal static class ValueFrameId
{
    public static string Otherwise(IfElse branch) => $"{branch.SourceBlockId}:else";

    public static bool IsOtherwise(string frameId, IfElse branch) =>
        string.Equals(frameId, Otherwise(branch), StringComparison.Ordinal);
}
