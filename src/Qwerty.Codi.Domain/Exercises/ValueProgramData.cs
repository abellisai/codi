namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Un caso di prova di un esercizio che conserva valori: i valori che il programma leggerà, e
/// l'uscita che deve produrre.
///
/// Prende il posto della scena del percorso. Là il traguardo era una cella della griglia e la corsa
/// finiva quando il personaggio ci arrivava; qui non c'è nulla da raggiungere: c'è un risultato da
/// calcolare a partire da valori diversi, e il caso di prova è il modo di dirlo. Più casi servono a
/// distinguere un programma che funziona da uno che funziona per caso.
/// </summary>
public sealed record ValueTestCase(IReadOnlyList<int> Inputs, IReadOnlyList<string> ExpectedOutput);

/// <summary>Dichiarazione di una variabile del programma: nome e valore di partenza.</summary>
public sealed record ValueVariable(string Name, int InitialValue);

/// <summary>
/// Dati propri del tipo <c>ValueProgram</c>: le variabili che il programma può nominare e i casi di
/// prova che deve superare.
///
/// Le variabili sono dichiarate **qui** e non inventate dal programma: la definizione dice che cosa
/// si può nominare, e un nome fuori da questo elenco è un programma da correggere, non una variabile
/// nuova. I valori di partenza sono dichiarati allo stesso modo, perché un valore iniziale che
/// l'alunno non vede è una regola nascosta.
///
/// I messaggi di esito fanno parte dei dati come negli altri tipi: l'esercizio spiega il risultato con
/// le parole del docente, e il server non ne inventa di proprie.
/// </summary>
public sealed record ValueProgramData(
    IReadOnlyList<ValueVariable> Variables,
    IReadOnlyList<ValueTestCase> TestCases,
    ExerciseFeedback Feedback) : IExerciseTypeData
{
    /// <summary>Costruisce e verifica i dati: un esercizio incoerente non entra nel sistema.</summary>
    public static ValueProgramData Create(
        IReadOnlyList<ValueVariable> variables,
        IReadOnlyList<ValueTestCase> testCases,
        ExerciseFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(testCases);
        ArgumentNullException.ThrowIfNull(feedback);

        if (variables.Count == 0)
        {
            throw new ArgumentException(
                "Un esercizio che conserva valori deve dichiarare almeno una variabile.", nameof(variables));
        }

        if (variables.Count > ValueProgramEngine.MaxVariables)
        {
            throw new ArgumentException(
                $"Un esercizio può dichiarare al massimo {ValueProgramEngine.MaxVariables} variabili.",
                nameof(variables));
        }

        var nomi = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variable in variables)
        {
            if (!ValueText.IsUsableName(variable.Name))
            {
                throw new ArgumentException(
                    $"Il nome «{variable.Name}» non è utilizzabile: una lettera iniziale, poi lettere, cifre o trattino basso, al massimo {ValueProgramEngine.MaxVariableNameLength} caratteri.",
                    nameof(variables));
            }

            if (!nomi.Add(variable.Name))
            {
                throw new ArgumentException(
                    $"La variabile «{variable.Name}» è dichiarata due volte.", nameof(variables));
            }

            if (Math.Abs((long)variable.InitialValue) > ValueProgramEngine.MaxInteger)
            {
                throw new ArgumentException(
                    $"Il valore iniziale di «{variable.Name}» supera il massimo ammesso ({ValueProgramEngine.MaxInteger}).",
                    nameof(variables));
            }
        }

        if (testCases.Count == 0)
        {
            throw new ArgumentException(
                "Un esercizio che conserva valori deve dichiarare almeno un caso di prova.", nameof(testCases));
        }

        foreach (var caso in testCases)
        {
            if (caso.Inputs.Count > ValueProgramEngine.MaxParameters)
            {
                throw new ArgumentException(
                    $"Un caso di prova può avere al massimo {ValueProgramEngine.MaxParameters} valori da leggere.",
                    nameof(testCases));
            }

            if (caso.ExpectedOutput.Count == 0)
            {
                throw new ArgumentException(
                    "Un caso di prova deve dire che cosa il programma deve mostrare.", nameof(testCases));
            }

            foreach (var riga in caso.ExpectedOutput)
            {
                if (riga.Length > ValueProgramEngine.MaxTextLength)
                {
                    throw new ArgumentException(
                        $"Un'uscita attesa supera i {ValueProgramEngine.MaxTextLength} caratteri.", nameof(testCases));
                }
            }
        }

        return new ValueProgramData(variables, testCases, feedback);
    }

    /// <summary>Nomi delle variabili dichiarate, nell'ordine in cui sono dichiarate.</summary>
    public IReadOnlyList<string> VariableNames => [.. Variables.Select(variable => variable.Name)];

    /// <summary>Valori di partenza, per nome.</summary>
    public IReadOnlyDictionary<string, int> InitialValues =>
        Variables.ToDictionary(variable => variable.Name, variable => variable.InitialValue, StringComparer.Ordinal);

    /// <summary>Vero se il nome è fra quelli dichiarati.</summary>
    public bool KnowsVariable(string name) => VariableNames.Contains(name, StringComparer.Ordinal);
}
