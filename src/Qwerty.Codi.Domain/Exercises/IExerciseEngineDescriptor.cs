namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Descrizione di una versione del motore degli esercizi: che tipo di attività interpreta, con
/// quale schema di definizione, con quali blocchi e con quali dati propri del tipo (§13.1–§13.4).
///
/// È l'unico punto che conosce le regole di una versione. Chi legge una definizione, chi verifica un
/// progetto a blocchi e chi accetta un checkpoint chiedono a questa descrizione che cosa è ammesso,
/// invece di contenere un elenco di tipi o di versioni: aggiungere una versione significa registrarla,
/// e nient'altro nel prodotto deve cambiare.
///
/// Il catalogo è **per versione del motore** e non «il catalogo attuale»: una versione pubblicata
/// porta con sé la propria descrizione e continua a essere interpretata con le sue regole anche
/// quando ne esiste una più grande (§13.2, §13.4).
/// </summary>
public interface IExerciseEngineDescriptor
{
    /// <summary>Tipo di esercizio dichiarato nella definizione, ad esempio <c>GridPath</c>.</summary>
    string ExerciseType { get; }

    /// <summary>Versione del motore, ad esempio <c>gridpath-1</c>.</summary>
    string Version { get; }

    /// <summary>Versione dello schema di definizione compresa da questa versione del motore.</summary>
    int SchemaVersion { get; }

    /// <summary>
    /// Versione dello schema del checkpoint prodotto da questa versione (§13.7): è il formato che il
    /// server accetta e che il player scrive. Due motori possono averne di diversi, e il server lo
    /// impara da qui invece di fissare un numero valido per tutti.
    /// </summary>
    int CheckpointSchemaVersion { get; }

    /// <summary>Blocchi del catalogo di questa versione, nell'ordine in cui si offrono.</summary>
    IReadOnlyList<ExerciseBlockSpec> Blocks { get; }

    /// <summary>
    /// Blocco che apre la catena eseguibile. Senza di lui il programma non ha nulla da eseguire: è
    /// la condizione che la definizione richiede e che il progetto deve soddisfare per partire
    /// (§13.4).
    /// </summary>
    string EntryPointBlock { get; }

    /// <summary>Vero se il blocco appartiene al catalogo di questa versione.</summary>
    bool KnowsBlock(string blockType);

    /// <summary>Descrizione di un blocco del catalogo, oppure <c>null</c> se la versione non lo conosce.</summary>
    ExerciseBlockSpec? Block(string blockType);

    /// <summary>
    /// Costruisce i dati propri del tipo dalle parti già dichiarate nella definizione. La versione
    /// del motore è l'unica che sa **quali** dati le servono; chi ha in mano il documento si limita
    /// a consegnarli. Un dato mancante o della forma sbagliata produce un
    /// <see cref="ArgumentException"/> che nomina il campo.
    /// </summary>
    IExerciseTypeData CreateData(object data);

    /// <summary>
    /// Verifica le regole del tipo che non riguardano i campi dei blocchi: per <c>GridPath</c>, che
    /// il completamento coincida con il traguardo della scena. Riceve i dati già costruiti da
    /// <see cref="CreateData"/>.
    /// </summary>
    void ValidateData(IExerciseTypeData data);
}
