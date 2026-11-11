using Qwerty.Codi.Domain.Common;

namespace Qwerty.Codi.Domain.Lessons;

/// <summary>
/// Lezione di §7.3: appartiene a un corso e porta titolo e descrizione facoltativa. Il corso di
/// una lezione non cambia dopo la creazione: spostarla significherebbe cambiare il programma di
/// due corsi con una sola richiesta, e non è un'operazione che il docente compie per errore.
/// Le attività non stanno qui: sono righe ordinate con vita propria (§7.3) e si sostituiscono
/// con <c>ReplaceLessonActivities</c>.
/// </summary>
public sealed class Lesson
{
    /// <summary>Lunghezza massima del titolo, allineata al vincolo della tabella.</summary>
    public const int MaxTitleLength = 160;

    /// <summary>Lunghezza massima della descrizione; scelta locale, non un requisito del dizionario.</summary>
    public const int MaxDescriptionLength = 2000;

    private Lesson(
        Guid id,
        Guid courseId,
        string title,
        string description,
        bool isArchived,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        long rowVersion)
    {
        Id = id;
        CourseId = courseId;
        Title = title;
        Description = description;
        IsArchived = isArchived;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        RowVersion = rowVersion;
    }

    public Guid Id { get; }

    /// <summary>Corso di appartenenza: immutabile dopo la creazione.</summary>
    public Guid CourseId { get; }

    /// <summary>Titolo come digitato dal docente.</summary>
    public string Title { get; private set; }

    /// <summary>Descrizione: stringa vuota quando il docente non ne ha scritta una.</summary>
    public string Description { get; private set; }

    /// <summary>Una lezione archiviata conserva lo storico e non fa parte del programma attivo.</summary>
    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Versione della riga per la concorrenza ottimistica (§7.1).</summary>
    public long RowVersion { get; }

    public static Lesson Create(Guid courseId, string title, string? description, DateTimeOffset nowUtc)
    {
        EnsureCourse(courseId);
        var usableTitle = EnsureUsableTitle(title);
        var usableDescription = EnsureUsableDescription(description);
        EnsureUtc(nowUtc, nameof(nowUtc));

        return new Lesson(
            Guid.NewGuid(), courseId, usableTitle, usableDescription,
            isArchived: false, createdAtUtc: nowUtc, updatedAtUtc: nowUtc, rowVersion: 1);
    }

    /// <summary>Ricostruisce la lezione da quanto è stato persistito, validando i dati letti.</summary>
    public static Lesson Restore(
        Guid id,
        Guid courseId,
        string title,
        string description,
        bool isArchived,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        long rowVersion)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("L'identificativo della lezione è obbligatorio.", nameof(id));
        }

        EnsureCourse(courseId);
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

        return new Lesson(
            id, courseId, usableTitle, usableDescription, isArchived,
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

    /// <summary>Archivia la lezione. Ripetere l'archiviazione non è un errore e non cambia nulla.</summary>
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
    public Lesson WithRowVersion(long rowVersion)
    {
        if (rowVersion < 1)
        {
            throw new ArgumentException("La versione di riga deve essere positiva.", nameof(rowVersion));
        }

        return new Lesson(
            Id, CourseId, Title, Description, IsArchived, CreatedAtUtc, UpdatedAtUtc, rowVersion);
    }

    private static void EnsureCourse(Guid courseId)
    {
        if (courseId == Guid.Empty)
        {
            throw new ArgumentException("Una lezione appartiene a un corso.", nameof(courseId));
        }
    }

    private static string EnsureUsableTitle(string title)
    {
        if (!NameNormalization.IsUsable(title, MaxTitleLength))
        {
            throw new ArgumentException(
                "Il titolo della lezione è vuoto, troppo lungo o contiene caratteri non ammessi.",
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

    private static void EnsureUtc(DateTimeOffset instant, string parameterName)
    {
        if (instant.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("L'istante di creazione deve essere in UTC.", parameterName);
        }
    }
}
