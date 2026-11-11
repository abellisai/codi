namespace Qwerty.Codi.Domain.Credentials;

/// <summary>
/// Natura di una sessione credenziale. Amministratore e partecipante usano schemi di cookie
/// distinti (§9.1, §9.2), quindi il tipo è un dato della riga e non una deduzione dalla presenza
/// di un identificativo.
/// </summary>
public enum CredentialPrincipalKind
{
    Admin,
    Participant,
}

/// <summary>
/// Credenziale emessa e persistita (§7.2, §9.1): registra chi l'ha ricevuta, quando scade e
/// quando è stata revocata. Il ticket che il client conserva non viene memorizzato: si conserva
/// la marca di sicurezza, che il ticket porta con sé, così revocare la riga rende obsoleto il
/// ticket senza doverlo cercare.
/// </summary>
public sealed class CredentialSession
{
    private CredentialSession(
        Guid id,
        CredentialPrincipalKind principalKind,
        Guid? adminUserId,
        Guid? participantId,
        string securityStamp,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset? revokedAtUtc,
        long rowVersion)
    {
        Id = id;
        PrincipalKind = principalKind;
        AdminUserId = adminUserId;
        ParticipantId = participantId;
        SecurityStamp = securityStamp;
        IssuedAtUtc = issuedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        RevokedAtUtc = revokedAtUtc;
        RowVersion = rowVersion;
    }

    public Guid Id { get; }

    public CredentialPrincipalKind PrincipalKind { get; }

    /// <summary>Amministratore a cui appartiene la credenziale, se il tipo è amministratore.</summary>
    public Guid? AdminUserId { get; }

    /// <summary>Partecipante a cui appartiene la credenziale, se il tipo è partecipante.</summary>
    public Guid? ParticipantId { get; }

    /// <summary>Marca portata dal ticket: la revoca la rende non più corrispondente.</summary>
    public string SecurityStamp { get; }

    public DateTimeOffset IssuedAtUtc { get; }

    /// <summary>Scadenza assoluta: non si rinnova oltre questo istante (§9.1).</summary>
    public DateTimeOffset ExpiresAtUtc { get; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public long RowVersion { get; private set; }

    /// <summary>
    /// Emette la credenziale di un amministratore. La durata arriva dal caso d'uso, perché è una
    /// politica di prodotto e non un'invariante dell'oggetto (§9.1).
    /// </summary>
    public static CredentialSession IssueForAdmin(Guid adminUserId, DateTimeOffset nowUtc, TimeSpan lifetime)
    {
        if (adminUserId == Guid.Empty)
        {
            throw new ArgumentException("L'amministratore della credenziale è obbligatorio.", nameof(adminUserId));
        }

        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentException("La durata della credenziale deve essere positiva.", nameof(lifetime));
        }

        if (nowUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("L'istante di emissione deve essere in UTC.", nameof(nowUtc));
        }

        return new CredentialSession(
            Guid.NewGuid(),
            CredentialPrincipalKind.Admin,
            adminUserId,
            participantId: null,
            Guid.NewGuid().ToString("N"),
            nowUtc,
            nowUtc + lifetime,
            revokedAtUtc: null,
            rowVersion: 1);
    }

    /// <summary>
    /// Emette la credenziale di un partecipante all'ingresso nella sessione (§9.2, §9.4). La marca
    /// di sicurezza nasce qui e viaggia solo dentro il cookie protetto: il database conserva la
    /// marca, non il ticket.
    /// </summary>
    public static CredentialSession IssueForParticipant(Guid participantId, DateTimeOffset nowUtc, TimeSpan lifetime)
    {
        if (participantId == Guid.Empty)
        {
            throw new ArgumentException("Il partecipante della credenziale è obbligatorio.", nameof(participantId));
        }

        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentException("La durata della credenziale deve essere positiva.", nameof(lifetime));
        }

        if (nowUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("L'istante di emissione deve essere in UTC.", nameof(nowUtc));
        }

        return new CredentialSession(
            Guid.NewGuid(),
            CredentialPrincipalKind.Participant,
            adminUserId: null,
            participantId,
            Guid.NewGuid().ToString("N"),
            nowUtc,
            nowUtc + lifetime,
            revokedAtUtc: null,
            rowVersion: 1);
    }

    /// <summary>
    /// Ricostruisce la credenziale da quanto è stato persistito, ripetendo la validazione: i dati
    /// letti sono un ingresso come gli altri e una riga alterata non deve produrre un oggetto
    /// che si dichiara valido (§7.2).
    /// </summary>
    public static CredentialSession Restore(
        Guid id,
        CredentialPrincipalKind principalKind,
        Guid? adminUserId,
        Guid? participantId,
        string securityStamp,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset? revokedAtUtc,
        long rowVersion)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("L'identificativo della credenziale è obbligatorio.", nameof(id));
        }

        var coherenceIsRespected = principalKind switch
        {
            CredentialPrincipalKind.Admin => adminUserId is not null && participantId is null,
            CredentialPrincipalKind.Participant => participantId is not null && adminUserId is null,
            _ => false,
        };

        if (!coherenceIsRespected)
        {
            throw new ArgumentException(
                "La credenziale deve riferirsi a un solo soggetto, coerente con il suo tipo.",
                nameof(principalKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(securityStamp);

        if (expiresAtUtc <= issuedAtUtc)
        {
            throw new ArgumentException(
                "La scadenza deve seguire l'emissione.",
                nameof(expiresAtUtc));
        }

        if (rowVersion < 1)
        {
            throw new ArgumentException("La versione di riga deve essere positiva.", nameof(rowVersion));
        }

        return new CredentialSession(
            id,
            principalKind,
            adminUserId,
            participantId,
            securityStamp,
            issuedAtUtc,
            expiresAtUtc,
            revokedAtUtc,
            rowVersion);
    }

    /// <summary>
    /// Vero se la credenziale è ancora utilizzabile in questo istante e se il ticket presentato è
    /// quello emesso: una marca diversa significa che la riga è stata revocata o sostituita, e il
    /// controllo vale anche per una connessione già aperta (§9.2).
    /// </summary>
    public bool IsUsableAt(DateTimeOffset nowUtc, string securityStamp) =>
        RevokedAtUtc is null &&
        nowUtc < ExpiresAtUtc &&
        string.Equals(SecurityStamp, securityStamp, StringComparison.Ordinal);

    /// <summary>
    /// Revoca la credenziale. Restituisce falso se era già revocata: ripetere la revoca non è un
    /// errore, ma nemmeno una nuova modifica da registrare.
    /// </summary>
    public bool Revoke(DateTimeOffset nowUtc)
    {
        if (RevokedAtUtc is not null)
        {
            return false;
        }

        RevokedAtUtc = nowUtc;

        return true;
    }

    /// <summary>Copia con la versione di riga assegnata dalla persistenza dopo una modifica.</summary>
    public CredentialSession WithRowVersion(long rowVersion)
    {
        if (rowVersion < 1)
        {
            throw new ArgumentException("La versione di riga deve essere positiva.", nameof(rowVersion));
        }

        return new CredentialSession(
            Id,
            PrincipalKind,
            AdminUserId,
            ParticipantId,
            SecurityStamp,
            IssuedAtUtc,
            ExpiresAtUtc,
            RevokedAtUtc,
            rowVersion);
    }
}
