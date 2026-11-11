namespace Qwerty.Codi.Domain.Admin;

/// <summary>
/// Account amministrativo dell'installazione (§9.1). Il nome è un'etichetta, non una credenziale:
/// l'autenticazione passa dalla password, di cui qui si conserva soltanto l'impronta. Il nome
/// normalizzato accompagna sempre quello digitato, perché è su quello che si decide l'unicità.
/// </summary>
public sealed class AdminUser
{
    /// <summary>Tentativi falliti consecutivi che fanno scattare il blocco temporaneo.</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>Durata del blocco temporaneo dopo i tentativi esauriti.</summary>
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    private AdminUser(
        Guid id,
        string username,
        string normalizedUsername,
        string passwordHash,
        string securityStamp,
        bool isActive,
        int failedLoginAttempts,
        DateTimeOffset? lockedUntilUtc,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        long rowVersion)
    {
        Id = id;
        Username = username;
        NormalizedUsername = normalizedUsername;
        PasswordHash = passwordHash;
        SecurityStamp = securityStamp;
        IsActive = isActive;
        FailedLoginAttempts = failedLoginAttempts;
        LockedUntilUtc = lockedUntilUtc;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        RowVersion = rowVersion;
    }

    /// <summary>Identificativo generato dal server: nessun ordinamento dipende da questo valore.</summary>
    public Guid Id { get; }

    /// <summary>Nome come digitato dal docente, conservato per la visualizzazione.</summary>
    public string Username { get; }

    /// <summary>Nome normalizzato: è la chiave di unicità dell'accesso (§7.2).</summary>
    public string NormalizedUsername { get; }

    /// <summary>Impronta della password, mai la password.</summary>
    public string PasswordHash { get; }

    /// <summary>
    /// Marca di sicurezza dell'account: cambia quando cambiano i presupposti delle credenziali
    /// emesse, così una sessione già aperta può essere invalidata (§9.2).
    /// </summary>
    public string SecurityStamp { get; }

    /// <summary>Un account disattivato non accede, ma resta nello storico.</summary>
    public bool IsActive { get; }

    /// <summary>Tentativi falliti consecutivi dall'ultimo accesso riuscito.</summary>
    public int FailedLoginAttempts { get; private set; }

    /// <summary>Fine del blocco temporaneo, se è attivo.</summary>
    public DateTimeOffset? LockedUntilUtc { get; private set; }

    /// <summary>Istante di creazione, in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>Istante dell'ultima modifica registrata, in UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Versione della riga per la concorrenza ottimistica (§7.1).</summary>
    public long RowVersion { get; }

    /// <summary>
    /// Crea il primo account amministrativo. Il nome si normalizza qui e non nel chiamante: due
    /// normalizzazioni diverse darebbero due account per lo stesso docente.
    /// </summary>
    public static AdminUser Create(string username, string passwordHash, DateTimeOffset nowUtc)
    {
        if (!AdminUsername.IsValid(username))
        {
            throw new ArgumentException(
                "Il nome utente amministrativo non è utilizzabile (§7.2).",
                nameof(username));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        if (nowUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("L'istante di creazione deve essere in UTC.", nameof(nowUtc));
        }

        return new AdminUser(
            Guid.NewGuid(),
            username.Trim(),
            AdminUsername.Normalize(username),
            passwordHash,
            CreateSecurityStamp(),
            isActive: true,
            failedLoginAttempts: 0,
            lockedUntilUtc: null,
            createdAtUtc: nowUtc,
            updatedAtUtc: nowUtc,
            rowVersion: 1);
    }

    /// <summary>
    /// Ricostruisce l'account da quanto è stato persistito. La validazione si ripete perché i
    /// dati letti sono un ingresso come gli altri (§7.1) e un archivio alterato non deve produrre
    /// un'entità incoerente.
    /// </summary>
    public static AdminUser Restore(
        Guid id,
        string username,
        string normalizedUsername,
        string passwordHash,
        string securityStamp,
        bool isActive,
        int failedLoginAttempts,
        DateTimeOffset? lockedUntilUtc,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        long rowVersion)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("L'identificativo dell'account è obbligatorio.", nameof(id));
        }

        if (!AdminUsername.IsValid(username))
        {
            throw new ArgumentException("Il nome utente letto non è utilizzabile.", nameof(username));
        }

        if (!string.Equals(normalizedUsername, AdminUsername.Normalize(username), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Il nome normalizzato letto non corrisponde alla normalizzazione del nome.",
                nameof(normalizedUsername));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(securityStamp);

        if (failedLoginAttempts < 0)
        {
            throw new ArgumentException(
                "Il numero di tentativi falliti non può essere negativo.",
                nameof(failedLoginAttempts));
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

        return new AdminUser(
            id,
            username,
            normalizedUsername,
            passwordHash,
            securityStamp,
            isActive,
            failedLoginAttempts,
            lockedUntilUtc,
            createdAtUtc,
            updatedAtUtc,
            rowVersion);
    }

    /// <summary>Vero se l'accesso è temporaneamente bloccato per troppi tentativi falliti (§9.1).</summary>
    public bool IsLockedAt(DateTimeOffset nowUtc) =>
        LockedUntilUtc is { } until && until > nowUtc;

    /// <summary>
    /// Registra un tentativo fallito e, raggiunta la soglia, fa scattare il blocco temporaneo.
    /// Restituisce vero se è stato questo tentativo a far scattare il blocco.
    /// </summary>
    public bool RegisterFailedAttempt(DateTimeOffset nowUtc)
    {
        FailedLoginAttempts++;
        UpdatedAtUtc = nowUtc;

        if (FailedLoginAttempts < MaxFailedAttempts)
        {
            return false;
        }

        if (LockedUntilUtc is { } until && until > nowUtc)
        {
            // Il blocco è ancora attivo e non si prolunga: altrimenti basterebbe continuare a
            // provare per tenere fuori anche chi conosce la password giusta.
            return false;
        }

        LockedUntilUtc = nowUtc + LockDuration;

        return true;
    }

    /// <summary>
    /// Registra un accesso riuscito: azzera tentativi e blocco. Il conteggio riparte solo dopo
    /// una password corretta, perché un blocco non deve sopravvivere alla password giusta.
    /// </summary>
    public void RegisterSuccessfulAccess(DateTimeOffset nowUtc)
    {
        FailedLoginAttempts = 0;
        LockedUntilUtc = null;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Copia dell'entità con la versione di riga che la persistenza ha assegnato dopo aver
    /// accettato la modifica: la versione non è una decisione del dominio.
    /// </summary>
    public AdminUser WithRowVersion(long rowVersion)
    {
        if (rowVersion < 1)
        {
            throw new ArgumentException("La versione di riga deve essere positiva.", nameof(rowVersion));
        }

        return new AdminUser(
            Id,
            Username,
            NormalizedUsername,
            PasswordHash,
            SecurityStamp,
            IsActive,
            FailedLoginAttempts,
            LockedUntilUtc,
            CreatedAtUtc,
            UpdatedAtUtc,
            rowVersion);
    }

    /// <summary>
    /// Marca di sicurezza nuova per ogni account e per ogni cambio dei presupposti delle
    /// credenziali. Non serve un identificativo ordinabile: basta che sia distinta.
    /// </summary>
    private static string CreateSecurityStamp() => Guid.NewGuid().ToString("N");
}
