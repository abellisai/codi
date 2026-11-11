using Qwerty.Codi.Domain.Common;

namespace Qwerty.Codi.Domain.Organization;

/// <summary>
/// Classe di una scuola (§7.2): nome e anno scolastico insieme, all'interno della scuola.
/// La scuola di appartenenza si fissa alla creazione e non cambia: una classe appartiene a una
/// sola scuola (§5.2), quindi spostarla sarebbe un'altra operazione, non una modifica.
/// </summary>
public sealed class Classroom
{
    /// <summary>Lunghezza massima del nome, allineata al vincolo della tabella.</summary>
    public const int MaxNameLength = 120;

    private Classroom(
        Guid id,
        Guid schoolId,
        string name,
        string normalizedName,
        string schoolYear,
        bool isArchived,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        long rowVersion)
    {
        Id = id;
        SchoolId = schoolId;
        Name = name;
        NormalizedName = normalizedName;
        SchoolYear = schoolYear;
        IsArchived = isArchived;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        RowVersion = rowVersion;
    }

    public Guid Id { get; }

    /// <summary>Scuola di appartenenza, decisa alla creazione.</summary>
    public Guid SchoolId { get; }

    /// <summary>Nome come digitato dal docente, conservato per la visualizzazione.</summary>
    public string Name { get; private set; }

    /// <summary>Nome normalizzato: con l'anno e la scuola decide l'unicità della classe.</summary>
    public string NormalizedName { get; private set; }

    /// <summary>Anno scolastico nella forma canonica `AAAA/AAAA`.</summary>
    public string SchoolYear { get; private set; }

    /// <summary>Una classe archiviata conserva lo storico e non fa parte della configurazione attiva.</summary>
    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Versione della riga per la concorrenza ottimistica (§7.1).</summary>
    public long RowVersion { get; }

    /// <summary>Crea una classe di una scuola. Il nome si normalizza qui, non nel chiamante.</summary>
    public static Classroom Create(Guid schoolId, string name, string schoolYear, DateTimeOffset nowUtc)
    {
        if (schoolId == Guid.Empty)
        {
            throw new ArgumentException("La scuola della classe è obbligatoria.", nameof(schoolId));
        }

        var normalizedName = EnsureUsableName(name);
        var normalizedYear = EnsureUsableYear(schoolYear);
        EnsureUtc(nowUtc, nameof(nowUtc));

        return new Classroom(
            Guid.NewGuid(), schoolId, name.Trim(), normalizedName, normalizedYear,
            isArchived: false, createdAtUtc: nowUtc, updatedAtUtc: nowUtc, rowVersion: 1);
    }

    /// <summary>Ricostruisce la classe da quanto è stato persistito, validando i dati letti.</summary>
    public static Classroom Restore(
        Guid id,
        Guid schoolId,
        string name,
        string normalizedName,
        string schoolYear,
        bool isArchived,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        long rowVersion)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("L'identificativo della classe è obbligatorio.", nameof(id));
        }

        if (schoolId == Guid.Empty)
        {
            throw new ArgumentException("La scuola della classe è obbligatoria.", nameof(schoolId));
        }

        var expectedName = EnsureUsableName(name);
        if (!string.Equals(normalizedName, expectedName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Il nome normalizzato letto non corrisponde alla normalizzazione del nome.",
                nameof(normalizedName));
        }

        var expectedYear = EnsureUsableYear(schoolYear);
        if (!string.Equals(schoolYear, expectedYear, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "L'anno scolastico letto non è nella forma canonica.", nameof(schoolYear));
        }

        if (updatedAtUtc < createdAtUtc)
        {
            throw new ArgumentException("L'ultima modifica non può precedere la creazione.", nameof(updatedAtUtc));
        }

        if (rowVersion < 1)
        {
            throw new ArgumentException("La versione di riga deve essere positiva.", nameof(rowVersion));
        }

        return new Classroom(
            id, schoolId, name.Trim(), normalizedName, schoolYear, isArchived,
            createdAtUtc, updatedAtUtc, rowVersion);
    }

    /// <summary>
    /// Cambia nome e anno scolastico. Restituisce falso quando il risultato è identico a prima:
    /// una richiesta che non cambia nulla non deve alzare la versione di riga.
    /// </summary>
    public bool Update(string name, string schoolYear, DateTimeOffset nowUtc)
    {
        var normalizedName = EnsureUsableName(name);
        var normalizedYear = EnsureUsableYear(schoolYear);

        if (string.Equals(normalizedName, NormalizedName, StringComparison.Ordinal) &&
            string.Equals(normalizedYear, SchoolYear, StringComparison.Ordinal))
        {
            return false;
        }

        Name = name.Trim();
        NormalizedName = normalizedName;
        SchoolYear = normalizedYear;
        UpdatedAtUtc = nowUtc;

        return true;
    }

    /// <summary>Archivia la classe. Ripetere l'archiviazione non è un errore e non cambia nulla.</summary>
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
    public Classroom WithRowVersion(long rowVersion)
    {
        if (rowVersion < 1)
        {
            throw new ArgumentException("La versione di riga deve essere positiva.", nameof(rowVersion));
        }

        return new Classroom(
            Id, SchoolId, Name, NormalizedName, SchoolYear, IsArchived,
            CreatedAtUtc, UpdatedAtUtc, rowVersion);
    }

    private static string EnsureUsableName(string name)
    {
        if (!NameNormalization.IsUsable(name, MaxNameLength))
        {
            throw new ArgumentException(
                "Il nome della classe è vuoto, troppo lungo o contiene caratteri non ammessi.",
                nameof(name));
        }

        return NameNormalization.Normalize(name);
    }

    private static string EnsureUsableYear(string schoolYear)
    {
        if (!SchoolYearNormalization.TryNormalize(schoolYear, out var normalized))
        {
            throw new ArgumentException(
                "L'anno scolastico deve indicare due anni consecutivi, per esempio 2026/2027.",
                nameof(schoolYear));
        }

        return normalized;
    }

    private static void EnsureUtc(DateTimeOffset instant, string parameterName)
    {
        if (instant.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("L'istante deve essere in UTC.", parameterName);
        }
    }
}
