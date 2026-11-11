namespace Qwerty.Codi.Domain.Organization;

/// <summary>
/// Scuola dell'installazione (§7.2). Il nome è la chiave dell'organizzazione: due scuole con lo
/// stesso nome normalizzato sarebbero indistinguibili per il docente, quindi l'unicità è un
/// invariante, non un controllo di facciata. L'archiviazione conserva lo storico e non elimina
/// nulla (§6.1): una scuola archiviata continua a esistere per i riferimenti che la usano.
/// </summary>
public sealed class School
{
    private School(
        Guid id,
        string name,
        string normalizedName,
        bool isArchived,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        long rowVersion)
    {
        Id = id;
        Name = name;
        NormalizedName = normalizedName;
        IsArchived = isArchived;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        RowVersion = rowVersion;
    }

    /// <summary>Identificativo generato dal server: nessun ordinamento dipende da questo valore.</summary>
    public Guid Id { get; }

    /// <summary>Nome come digitato dal docente, conservato per la visualizzazione.</summary>
    public string Name { get; private set; }

    /// <summary>Nome normalizzato: è la chiave di unicità fra le scuole (§7.2).</summary>
    public string NormalizedName { get; private set; }

    /// <summary>Una scuola archiviata resta consultabile e modificabile, ma non è più operativa.</summary>
    public bool IsArchived { get; private set; }

    /// <summary>Istante di creazione, in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>Istante dell'ultima modifica registrata, in UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Versione della riga per la concorrenza ottimistica (§7.1).</summary>
    public long RowVersion { get; }

    /// <summary>
    /// Crea una scuola. Il nome si normalizza qui e non nel chiamante: due normalizzazioni diverse
    /// darebbero due scuole per lo stesso istituto.
    /// </summary>
    public static School Create(string name, DateTimeOffset nowUtc)
    {
        EnsureUsableName(name);
        EnsureUtc(nowUtc, nameof(nowUtc));

        return new School(
            Guid.NewGuid(),
            name.Trim(),
            SchoolName.Normalize(name),
            isArchived: false,
            createdAtUtc: nowUtc,
            updatedAtUtc: nowUtc,
            rowVersion: 1);
    }

    /// <summary>
    /// Ricostruisce la scuola da quanto è stato persistito. La validazione si ripete perché i dati
    /// letti sono un ingresso come gli altri (§7.1) e un archivio alterato non deve produrre
    /// un'entità incoerente.
    /// </summary>
    public static School Restore(
        Guid id,
        string name,
        string normalizedName,
        bool isArchived,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        long rowVersion)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("L'identificativo della scuola è obbligatorio.", nameof(id));
        }

        if (!SchoolName.IsValid(name))
        {
            throw new ArgumentException("Il nome della scuola letto non è utilizzabile.", nameof(name));
        }

        if (!string.Equals(normalizedName, SchoolName.Normalize(name), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Il nome normalizzato letto non corrisponde alla normalizzazione del nome.",
                nameof(normalizedName));
        }

        if (updatedAtUtc < createdAtUtc)
        {
            throw new ArgumentException(
                "L'ultima modifica non può precedere la creazione.",
                nameof(updatedAtUtc));
        }

        if (rowVersion < 1)
        {
            throw new ArgumentException("La versione di riga deve essere positiva.", nameof(rowVersion));
        }

        return new School(id, name, normalizedName, isArchived, createdAtUtc, updatedAtUtc, rowVersion);
    }

    /// <summary>
    /// Cambia il nome visualizzato, comprese maiuscole e accenti. La normalizzazione serve
    /// all'unicità: solo il nome visualizzato identico, tolti gli spazi ai bordi, è invariato.
    /// </summary>
    public bool Rename(string name, DateTimeOffset nowUtc)
    {
        EnsureUsableName(name);
        EnsureUtc(nowUtc, nameof(nowUtc));

        var displayName = name.Trim();
        if (string.Equals(displayName, Name, StringComparison.Ordinal))
        {
            return false;
        }

        Name = displayName;
        NormalizedName = SchoolName.Normalize(displayName);
        UpdatedAtUtc = nowUtc;

        return true;
    }

    /// <summary>
    /// Archivia la scuola e restituisce vero se lo stato è cambiato. Archiviare due volte è un
    /// esito previsto, non un errore: la richiesta ripetuta non deve né fallire né riscrivere la riga.
    /// </summary>
    public bool Archive(DateTimeOffset nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));

        if (IsArchived)
        {
            return false;
        }

        IsArchived = true;
        UpdatedAtUtc = nowUtc;

        return true;
    }

    /// <summary>
    /// Copia dell'entità con la versione di riga che la persistenza ha assegnato dopo aver
    /// accettato la modifica: la versione non è una decisione del dominio.
    /// </summary>
    public School WithRowVersion(long rowVersion)
    {
        if (rowVersion < 1)
        {
            throw new ArgumentException("La versione di riga deve essere positiva.", nameof(rowVersion));
        }

        return new School(Id, Name, NormalizedName, IsArchived, CreatedAtUtc, UpdatedAtUtc, rowVersion);
    }

    private static void EnsureUsableName(string name)
    {
        if (!SchoolName.IsValid(name))
        {
            throw new ArgumentException("Il nome della scuola non è utilizzabile (§7.2).", nameof(name));
        }
    }

    private static void EnsureUtc(DateTimeOffset instant, string parameterName)
    {
        if (instant.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("L'istante deve essere in UTC.", parameterName);
        }
    }
}
