using Qwerty.Codi.Domain.Common;

namespace Qwerty.Codi.Domain.Organization;

/// <summary>
/// Corso (§7.2): titolo e descrizione facoltativa. Il titolo non è una chiave — due corsi possono
/// chiamarsi allo stesso modo — ma deve essere utilizzabile come testo: non vuoto, senza caratteri
/// di controllo e entro il limite della colonna.
/// </summary>
public sealed class Course
{
    /// <summary>Lunghezza massima del titolo, allineata al vincolo della tabella.</summary>
    public const int MaxTitleLength = 160;

    /// <summary>Lunghezza massima della descrizione; scelta locale, non un requisito del dizionario.</summary>
    public const int MaxDescriptionLength = 2000;

    private Course(
        Guid id,
        string title,
        string description,
        bool isArchived,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        long rowVersion)
    {
        Id = id;
        Title = title;
        Description = description;
        IsArchived = isArchived;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        RowVersion = rowVersion;
    }

    public Guid Id { get; }

    /// <summary>Titolo come digitato dal docente.</summary>
    public string Title { get; private set; }

    /// <summary>Descrizione: stringa vuota quando il docente non ne ha scritta una.</summary>
    public string Description { get; private set; }

    /// <summary>Un corso archiviato conserva lo storico e non fa parte della configurazione attiva.</summary>
    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Versione della riga per la concorrenza ottimistica (§7.1).</summary>
    public long RowVersion { get; }

    public static Course Create(string title, string? description, DateTimeOffset nowUtc)
    {
        var usableTitle = EnsureUsableTitle(title);
        var usableDescription = EnsureUsableDescription(description);

        if (nowUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("L'istante di creazione deve essere in UTC.", nameof(nowUtc));
        }

        return new Course(
            Guid.NewGuid(), usableTitle, usableDescription,
            isArchived: false, createdAtUtc: nowUtc, updatedAtUtc: nowUtc, rowVersion: 1);
    }

    /// <summary>Ricostruisce il corso da quanto è stato persistito, validando i dati letti.</summary>
    public static Course Restore(
        Guid id,
        string title,
        string description,
        bool isArchived,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        long rowVersion)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("L'identificativo del corso è obbligatorio.", nameof(id));
        }

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

        return new Course(
            id, usableTitle, usableDescription, isArchived,
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

    /// <summary>Archivia il corso. Ripetere l'archiviazione non è un errore e non cambia nulla.</summary>
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
    public Course WithRowVersion(long rowVersion)
    {
        if (rowVersion < 1)
        {
            throw new ArgumentException("La versione di riga deve essere positiva.", nameof(rowVersion));
        }

        return new Course(
            Id, Title, Description, IsArchived, CreatedAtUtc, UpdatedAtUtc, rowVersion);
    }

    private static string EnsureUsableTitle(string title)
    {
        if (!NameNormalization.IsUsable(title, MaxTitleLength))
        {
            throw new ArgumentException(
                "Il titolo del corso è vuoto, troppo lungo o contiene caratteri non ammessi.",
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
