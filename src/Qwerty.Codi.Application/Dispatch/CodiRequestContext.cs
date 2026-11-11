namespace Qwerty.Codi.Application.Dispatch;

/// <summary>
/// Contesto autorizzativo di una richiesta, ricavato dal trasporto e non dal payload (§4.4,
/// §9.3). Porta identità e permessi già verificati: gli handler non accettano identificativi
/// autorevoli dichiarati nel payload.
/// </summary>
public sealed class CodiRequestContext
{
    private static readonly CodiPolicy[] AnonymousGrants = [CodiPolicy.AnonymousBootstrap];

    private readonly IReadOnlySet<CodiPolicy> _grants;

    /// <summary>
    /// Contesto senza identità: ammette soltanto <see cref="CodiPolicy.AnonymousBootstrap"/>.
    /// È il contesto del bootstrap e del login, prima che esista una credenziale (§9.4).
    /// </summary>
    public static CodiRequestContext Anonymous { get; } = new(isAuthenticated: false, AnonymousGrants);

    /// <summary>
    /// Costruisce il contesto dalle concessioni risolte dal trasporto. Il costruttore è pubblico
    /// perché i trasporti vivono in altri progetti, ma un contesto si deriva dalla credenziale:
    /// nessun payload può dichiarare i propri permessi.
    /// </summary>
    public CodiRequestContext(
        bool isAuthenticated,
        IEnumerable<CodiPolicy> grants,
        Guid? credentialSessionId = null,
        Guid? adminUserId = null,
        Guid? participantId = null,
        string? admissionScope = null,
        string? idempotencyKey = null,
        string? remoteAddress = null)
    {
        ArgumentNullException.ThrowIfNull(grants);

        IsAuthenticated = isAuthenticated;
        _grants = grants.ToHashSet();
        CredentialSessionId = credentialSessionId;
        AdminUserId = adminUserId;
        ParticipantId = participantId;
        AdmissionScope = admissionScope;
        IdempotencyKey = idempotencyKey;
        RemoteAddress = remoteAddress;
    }

    /// <summary>Vero se esiste un'identità autenticata, indipendentemente dalla specifica policy.</summary>
    public bool IsAuthenticated { get; }

    /// <summary>Credenziale corrente verificata dal trasporto, mai dal payload.</summary>
    public Guid? CredentialSessionId { get; }

    /// <summary>Amministratore della credenziale verificata.</summary>
    public Guid? AdminUserId { get; }

    /// <summary>Partecipante della credenziale verificata (§9.2).</summary>
    public Guid? ParticipantId { get; }

    /// <summary>
    /// Scope dell'ingresso anonimo, letto dal cookie di ammissione (§9.4). Non è un'identità e non
    /// concede permessi: serve all'idempotenza del join, che senza una chiave di scope non saprebbe
    /// a chi attribuire una richiesta ripetuta prima che esista una credenziale.
    /// </summary>
    public string? AdmissionScope { get; }

    /// <summary>
    /// Chiave di idempotenza dichiarata nell'intestazione della richiesta (§10.2). Il trasporto la
    /// legge dall'intestazione e non dal corpo, così un client non può attribuire a un altro una
    /// richiesta ripetuta.
    /// </summary>
    public string? IdempotencyKey { get; }

    /// <summary>
    /// Indirizzo di rete da cui arriva la richiesta, quando il trasporto lo conosce. **Non concede
    /// nulla** e non partecipa a nessuna decisione di autorizzazione: serve al conteggio dei
    /// tentativi di codice di ingresso (§17.3), dove un browser che rinnova il ticket a ogni
    /// richiesta non deve poter eludere il limite cambiando lo scope.
    /// </summary>
    public string? RemoteAddress { get; }

    /// <summary>Vero se la policy richiesta è concessa.</summary>
    public bool Allows(CodiPolicy policy) => _grants.Contains(policy);
}
