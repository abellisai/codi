namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Descrizione della versione <c>quiz-1</c> (§13.9): l'alunno legge una rappresentazione, produce
/// una risposta e riceve un esito.
///
/// È il primo tipo registrato che **non** è un percorso su griglia. Non ha blocchi — il catalogo è
/// vuoto, e per un esercizio dichiarativo questo è il catalogo giusto, non un catalogo mancante — e
/// non ha un checkpoint proprio, perché non c'è un'esecuzione da sospendere e riprendere: la
/// correzione la fa il server quando l'alunno salva la risposta.
/// </summary>
public sealed class QuizExerciseData : IExerciseEngineDescriptor
{
    /// <inheritdoc />
    public string ExerciseType => QuizEngine.ExerciseType;

    /// <inheritdoc />
    public string Version => QuizEngine.Version;

    /// <inheritdoc />
    public int SchemaVersion => QuizEngine.SchemaVersion;

    /// <summary>
    /// Nessuna esecuzione da riprendere: la risposta è il lavoro, e il lavoro è già persistito per
    /// tentativo. Lo schema resta dichiarato perché il registro ne chiede uno a ogni versione, e
    /// vale <c>0</c>: «questa versione non produce checkpoint».
    /// </summary>
    public int CheckpointSchemaVersion => 0;

    /// <summary>
    /// Catalogo vuoto: un esercizio dichiarativo non si compone con blocchi. Il progetto del
    /// tentativo è il verbale della risposta, non un workspace.
    /// </summary>
    public IReadOnlyList<ExerciseBlockSpec> Blocks => [];

    /// <summary>
    /// Nessun blocco di ingresso: senza blocchi non c'è una catena da aprire. La definizione non
    /// richiede quindi alcun blocco, e la regola che ne impone uno vale solo per i tipi che hanno un
    /// catalogo.
    /// </summary>
    public string EntryPointBlock => string.Empty;

    /// <inheritdoc />
    public bool KnowsBlock(string blockType) => false;

    /// <inheritdoc />
    public ExerciseBlockSpec? Block(string blockType) => null;

    /// <summary>
    /// I dati del tipo dichiarativo: rappresentazione, domanda, modo di confronto e messaggi.
    /// </summary>
    public IExerciseTypeData CreateData(object data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data is not QuizDefinitionParts parts)
        {
            throw new ArgumentException(
                $"La versione «{Version}» descrive un esercizio {ExerciseType}: le parti ricevute non hanno la forma attesa (rappresentazione, domanda e modo di confronto).",
                nameof(data));
        }

        return QuizData.Create(parts.Stimulus, parts.Prompt, parts.Match, parts.Feedback);
    }

    /// <summary>
    /// Un esercizio dichiarativo non ha regole proprie da verificare oltre a quelle dei suoi dati,
    /// che sono già controllati alla costruzione: rappresentazione e domanda non vuote, modo di
    /// confronto previsto. Non c'è nulla da confrontare con una scena, perché una scena non esiste.
    /// </summary>
    public void ValidateData(IExerciseTypeData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data is not QuizData)
        {
            throw new ArgumentException(
                $"La versione «{Version}» descrive un esercizio {ExerciseType}: i dati da verificare sono di un altro tipo.",
                nameof(data));
        }
    }
}
