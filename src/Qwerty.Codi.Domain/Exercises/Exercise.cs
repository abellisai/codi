using Qwerty.Codi.Domain.Common;

namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Esercizio (§7.2): titolo, descrizione facoltativa e **tipo di attività**. Il titolo non è una
/// chiave — due esercizi possono chiamarsi allo stesso modo — ma deve essere utilizzabile come
/// testo: non vuoto, senza caratteri di controllo e entro il limite della colonna.
///
/// Il tipo sta qui e non soltanto nella definizione perché l'anagrafica lo precede: quando il
/// docente crea un esercizio la bozza non esiste ancora, e senza il tipo l'editor non saprebbe quali
/// campi mostrare. È l'unica informazione della definizione che vive anche sull'identità, e le due
/// devono concordare quando la bozza arriva.
/// </summary>
public sealed class Exercise
{
    /// <summary>Lunghezza massima del titolo, allineata al vincolo della tabella.</summary>
    public const int MaxTitleLength = 160;

    /// <summary>Lunghezza massima della descrizione; scelta locale, non un requisito del dizionario.</summary>
    public const int MaxDescriptionLength = 2000;

    private Exercise(
        Guid id,
        string exerciseType,
        string title,
        string description,
        bool isArchived,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        long rowVersion)
    {
        Id = id;
        ExerciseType = exerciseType;
        Title = title;
        Description = description;
        IsArchived = isArchived;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        RowVersion = rowVersion;
    }

    public Guid Id { get; }

    /// <summary>
    /// Tipo di attività dichiarato all'anagrafica, fra quelli del registro dei motori
    /// (<see cref="ExerciseEngineRegistry"/>). Non cambia dopo la creazione: cambiare tipo
    /// significherebbe riscrivere l'esercizio, e un esercizio già assegnato non si riscrive.
    /// </summary>
    public string ExerciseType { get; }

    /// <summary>Titolo come digitato dal docente.</summary>
    public string Title { get; private set; }

    /// <summary>Descrizione: stringa vuota quando il docente non ne ha scritta una.</summary>
    public string Description { get; private set; }

    /// <summary>Un esercizio archiviato conserva lo storico e non fa parte della configurazione attiva.</summary>
    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Versione della riga per la concorrenza ottimistica (§7.1).</summary>
    public long RowVersion { get; }

    /// <summary>
    /// Crea un esercizio del tipo indicato. Il tipo è obbligatorio: un esercizio senza tipo non
    /// avrebbe un motore che lo interpreta, e l'editor non saprebbe che cosa comporre.
    /// </summary>
    public static Exercise Create(string exerciseType, string title, string? description, DateTimeOffset nowUtc)
    {
        var usableType = EnsureUsableType(exerciseType);
        var usableTitle = EnsureUsableTitle(title);
        var usableDescription = EnsureUsableDescription(description);

        if (nowUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("L'istante di creazione deve essere in UTC.", nameof(nowUtc));
        }

        return new Exercise(
            Guid.NewGuid(), usableType, usableTitle, usableDescription,
            isArchived: false, createdAtUtc: nowUtc, updatedAtUtc: nowUtc, rowVersion: 1);
    }

    /// <summary>Ricostruisce l'esercizio da quanto è stato persistito, validando i dati letti.</summary>
    public static Exercise Restore(
        Guid id,
        string exerciseType,
        string title,
        string description,
        bool isArchived,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        long rowVersion)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("L'identificativo dell'esercizio è obbligatorio.", nameof(id));
        }

        var usableType = EnsureUsableType(exerciseType);
        var usableTitle = EnsureUsableTitle(title);
        var usableDescription = EnsureUsableDescription(description);

        if (updatedAtUtc < createdAtUtc)
        {
            throw new ArgumentException("L'ultima modifica non può precedere la creazione.", nameof(updatedAtUtc));
        }

        if (rowVersion < 1)
        {
            throw new ArgumentException("La versione di riga deve essere positiva.", nameof(rowVersion));
        }

        return new Exercise(
            id, usableType, usableTitle, usableDescription, isArchived,
            createdAtUtc, updatedAtUtc, rowVersion);
    }

    /// <summary>
    /// Cambia titolo e descrizione. Restituisce falso quando il risultato è identico a prima: una
    /// richiesta che non cambia nulla non deve alzare la versione di riga.
    /// </summary>
    public bool Update(string title, string? description, DateTimeOffset nowUtc)
    {
        var usableTitle = EnsureUsableTitle(title);
        var usableDescription = EnsureUsableDescription(description);

        if (string.Equals(usableTitle, Title, StringComparison.Ordinal) &&
            string.Equals(usableDescription, Description, StringComparison.Ordinal))
        {
            return false;
        }

        Title = usableTitle;
        Description = usableDescription;
        UpdatedAtUtc = nowUtc;

        return true;
    }

    /// <summary>Archivia l'esercizio. Ripetere l'archiviazione non è un errore e non cambia nulla.</summary>
    public bool Archive(DateTimeOffset nowUtc)
    {
        if (IsArchived)
        {
            return false;
        }

        IsArchived = true;
        UpdatedAtUtc = nowUtc;

        return true;
    }

    /// <summary>Copia con la versione di riga assegnata dalla persistenza.</summary>
    public Exercise WithRowVersion(long rowVersion)
    {
        if (rowVersion < 1)
        {
            throw new ArgumentException("La versione di riga deve essere positiva.", nameof(rowVersion));
        }

        return new Exercise(
            Id, ExerciseType, Title, Description, IsArchived, CreatedAtUtc, UpdatedAtUtc, rowVersion);
    }

    /// <summary>
    /// Un tipo utilizzabile è un tipo **registrato**: il registro dei motori è l'unica sorgente di
    /// che cosa il prodotto sa interpretare, e un tipo scritto a mano nell'anagrafica sarebbe un
    /// esercizio che nessun motore può svolgere.
    /// </summary>
    private static string EnsureUsableType(string exerciseType)
    {
        if (string.IsNullOrWhiteSpace(exerciseType))
        {
            throw new ArgumentException("Il tipo dell'esercizio è obbligatorio.", nameof(exerciseType));
        }

        var engine = ExerciseEngineRegistry.FindType(exerciseType)
            ?? throw new ArgumentException(
                $"Il tipo di esercizio «{exerciseType}» non è previsto: questa build conosce {string.Join(", ", ExerciseEngineRegistry.KnownExerciseTypes)}.",
                nameof(exerciseType));

        return engine.ExerciseType;
    }

    private static string EnsureUsableTitle(string title)
    {
        if (!NameNormalization.IsUsable(title, MaxTitleLength))
        {
            throw new ArgumentException(
                "Il titolo dell'esercizio è vuoto, troppo lungo o contiene caratteri non ammessi.",
                nameof(title));
        }

        return title.Trim();
    }

    private static string EnsureUsableDescription(string? description)
    {
        var trimmed = description?.Trim() ?? string.Empty;

        if (trimmed.Length > MaxDescriptionLength)
        {
            throw new ArgumentException(
                $"La descrizione supera i {MaxDescriptionLength} caratteri.", nameof(description));
        }

        return trimmed;
    }
}
