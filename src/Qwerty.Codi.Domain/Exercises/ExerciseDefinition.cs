using Qwerty.Codi.Domain.Common;

namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Definizione didattica di un esercizio (§13.2): **che cosa** il docente chiede, con quali blocchi
/// e con quali limiti. È autosufficiente e non contiene il progetto a blocchi, che è un formato
/// distinto con un ciclo di vita proprio (§13.3): la definizione dice le regole, il workspace dice
/// che cosa è stato scritto.
///
/// La definizione ha una parte generale — schema, tipo, versione del motore, titolo, consegna,
/// blocchi ammessi, limiti — e i dati propri del tipo, che vivono in <see cref="Data"/>. Quali dati
/// siano ammessi lo decide la descrizione della versione del motore
/// (<see cref="IExerciseEngineDescriptor"/>), non questa classe: è così che un tipo nuovo non
/// costringe a modificare i consumatori della definizione.
///
/// Una definizione che esiste è valida. Le regole della versione del motore sono verificate alla
/// costruzione e non nei consumatori, e la stessa funzione accoglie i dati appena scritti dal
/// docente e quelli riletti dal database: la definizione è immutabile, quindi non esiste uno stato
/// da ripristinare oltre alla sua costruzione.
/// </summary>
public sealed class ExerciseDefinition
{
    /// <summary>Lunghezza massima della consegna; scelta locale, non un requisito del dizionario.</summary>
    public const int MaxInstructionsLength = 2000;

    private ExerciseDefinition(
        int schemaVersion,
        string exerciseType,
        string engineVersion,
        string title,
        string instructions,
        IReadOnlyList<string> allowedBlocks,
        ExerciseLimits limits,
        IExerciseTypeData data)
    {
        SchemaVersion = schemaVersion;
        ExerciseType = exerciseType;
        EngineVersion = engineVersion;
        Title = title;
        Instructions = instructions;
        AllowedBlocks = allowedBlocks;
        Limits = limits;
        Data = data;
    }

    /// <summary>Versione dello schema della definizione: un formato futuro non viene interpretato come questo.</summary>
    public int SchemaVersion { get; }

    /// <summary>Tipo di attività dichiarato, ad esempio <c>GridPath</c>.</summary>
    public string ExerciseType { get; }

    /// <summary>Versione del motore che interpreta questa definizione (§13.2).</summary>
    public string EngineVersion { get; }

    /// <summary>Nome dell'esercizio, con la stessa regola di utilizzabilità dell'anagrafica.</summary>
    public string Title { get; }

    /// <summary>Consegna mostrata all'alunno.</summary>
    public string Instructions { get; }

    /// <summary>Blocchi ammessi, senza ripetizioni e tutti appartenenti al catalogo della versione.</summary>
    public IReadOnlyList<string> AllowedBlocks { get; }

    /// <summary>Limiti didattici.</summary>
    public ExerciseLimits Limits { get; }

    /// <summary>
    /// Dati propri del tipo dichiarato: per <c>GridPath</c> sono scena, condizione di completamento
    /// e messaggi di esito. Chi conosce il tipo li legge dalla propria forma concreta, e chi non lo
    /// conosce non deve interpretarli.
    /// </summary>
    public IExerciseTypeData Data { get; }

    public static ExerciseDefinition Create(
        IExerciseEngineDescriptor engine,
        string exerciseType,
        string title,
        string instructions,
        IReadOnlyList<string>? allowedBlocks,
        ExerciseLimits limits,
        IExerciseTypeData data)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(data);

        // La definizione dichiara il tipo, e la versione del motore ne dichiara uno: i due devono
        // coincidere, altrimenti lo stesso documento direbbe due cose diverse.
        if (!string.Equals(exerciseType, engine.ExerciseType, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Il tipo di esercizio '{exerciseType}' non è previsto: la versione «{engine.Version}» conosce {engine.ExerciseType}.",
                nameof(exerciseType));
        }

        engine.ValidateData(data);

        return new ExerciseDefinition(
            engine.SchemaVersion,
            exerciseType,
            engine.Version,
            EnsureTitle(title),
            EnsureInstructions(instructions),
            EnsureAllowedBlocks(engine, allowedBlocks),
            limits,
            data);
    }

    /// <summary>
    /// Il titolo segue la regola dell'anagrafica (§7.2): se le due divergessero, lo stesso nome
    /// sarebbe utilizzabile in un punto e non nell'altro.
    /// </summary>
    private static string EnsureTitle(string title)
    {
        if (!NameNormalization.IsUsable(title, Exercise.MaxTitleLength))
        {
            throw new ArgumentException(
                $"Il titolo è vuoto, troppo lungo o contiene caratteri non ammessi: ne sono ammessi {Exercise.MaxTitleLength}.",
                nameof(title));
        }

        return title.Trim();
    }

    private static string EnsureInstructions(string instructions)
    {
        var trimmed = instructions?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            throw new ArgumentException("La consegna non può essere vuota: è ciò che l'alunno deve fare.", nameof(instructions));
        }

        if (trimmed.Length > MaxInstructionsLength)
        {
            throw new ArgumentException(
                $"La consegna supera i {MaxInstructionsLength} caratteri.", nameof(instructions));
        }

        return trimmed;
    }

    /// <summary>
    /// I blocchi ammessi sono verificati contro il catalogo **della versione dichiarata** e non
    /// contro quello corrente: un blocco introdotto dopo non può comparire in una definizione di
    /// una versione precedente, e un blocco di quella versione resta ammesso per sempre.
    ///
    /// Un tipo senza catalogo — l'esercizio dichiarativo — non ammette blocchi, e per lui l'elenco
    /// vuoto è la risposta giusta invece di un diniego: non c'è nulla da comporre con i blocchi.
    /// La regola che pretende un punto di ingresso vale solo dove esiste un catalogo.
    /// </summary>
    private static IReadOnlyList<string> EnsureAllowedBlocks(
        IExerciseEngineDescriptor engine,
        IReadOnlyList<string>? allowedBlocks)
    {
        if (engine.Blocks.Count == 0)
        {
            if (allowedBlocks is { Count: > 0 })
            {
                throw new ArgumentException(
                    $"La versione «{engine.Version}» non ha blocchi: la definizione non può ammetterne.",
                    nameof(allowedBlocks));
            }

            return [];
        }

        if (allowedBlocks is null || allowedBlocks.Count == 0)
        {
            throw new ArgumentException(
                "La definizione non ammette nessun blocco: senza blocchi non c'è un esercizio da svolgere.",
                nameof(allowedBlocks));
        }

        var blocks = new List<string>(allowedBlocks.Count);

        foreach (var block in allowedBlocks)
        {
            if (string.IsNullOrWhiteSpace(block))
            {
                throw new ArgumentException("Un blocco ammesso non ha nome.", nameof(allowedBlocks));
            }

            if (!engine.KnowsBlock(block))
            {
                throw new ArgumentException(
                    $"Il blocco '{block}' non appartiene al catalogo di {engine.Version}.",
                    nameof(allowedBlocks));
            }

            if (blocks.Contains(block, StringComparer.Ordinal))
            {
                throw new ArgumentException($"Il blocco '{block}' è ripetuto.", nameof(allowedBlocks));
            }

            blocks.Add(block);
        }

        if (!blocks.Contains(engine.EntryPointBlock, StringComparer.Ordinal))
        {
            // Senza punto di ingresso non c'è nulla da eseguire, e il diniego arriverebbe all'alunno
            // invece che al docente che sta componendo l'esercizio (§13.4).
            throw new ArgumentException(
                $"Il blocco {engine.EntryPointBlock} è obbligatorio: senza punto di ingresso il programma non ha nulla da eseguire.",
                nameof(allowedBlocks));
        }

        return blocks;
    }
}
