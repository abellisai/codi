using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Qwerty.Codi.Api.Dispatch;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Domain.Credentials;
using Qwerty.Codi.Domain.Sessions;
using Qwerty.Codi.Application.Sessions;

namespace Qwerty.Codi.Api.Authentication;

/// <summary>
/// Risolve il contesto autorizzativo dalla credenziale presentata. Il cookie da solo non basta: la
/// credenziale viene riletta a ogni richiesta e deve risultare ancora utilizzabile, così la revoca
/// vale anche per una connessione già aperta (§9.2). Un cookie valido ma con la credenziale
/// revocata non concede niente, e non concede niente nemmeno un ticket di cui non si riconosce la
/// marca.
///
/// Oltre all'identità il contesto porta i dati del trasporto che il corpo non può dichiarare
/// (§4.4): lo **scope di ammissione** del ticket pubblico, che serve all'idempotenza dell'ingresso;
/// la **chiave di idempotenza** dell'intestazione, che i comandi ripetibili dichiarano (§10.2); e
/// l'**indirizzo di rete**, che non concede nulla e serve al conteggio dei tentativi di codice
/// (§17.3).
///
/// Per un partecipante la sola credenziale non basta a concedere <c>AdmittedParticipant</c>: la
/// policy vale per una partecipazione **ammessa** a una sessione **non terminata** (§9.3), quindi la
/// verifica si ripete a ogni richiesta e non al momento dell'ingresso.
/// </summary>
internal sealed class CookieRequestContextProvider(
    IUnitOfWorkContext unitOfWorkContext,
    IParticipantConnectionRegistry connections,
    TimeProvider timeProvider,
    IDataProtectionProvider dataProtectionProvider) : ICodiRequestContextProvider
{
    /// <summary>Intestazione della chiave di idempotenza (§10.2).</summary>
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    public async Task<CodiRequestContext> ResolveRequestContextAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var admissionScope = AdmissionCookie.ReadScope(httpContext, dataProtectionProvider);
        var idempotencyKey = ReadIdempotencyKey(httpContext);
        var remoteAddress = ReadRemoteAddress(httpContext);

        if (httpContext.User.Identity?.IsAuthenticated != true)
        {
            return Anonymous(admissionScope, idempotencyKey, remoteAddress);
        }

        var sessionId = ReadCredentialSessionId(httpContext.User);
        var stamp = httpContext.User.FindFirst(CodiClaimTypes.CredentialStamp)?.Value;

        if (sessionId is null || string.IsNullOrEmpty(stamp))
        {
            return Anonymous(admissionScope, idempotencyKey, remoteAddress);
        }

        await using var work = await unitOfWorkContext.CreateAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var row = await work.Repo<ICredentialSessionRepository>().GetAsync(
            sessionId.Value.ToString("D"), cancellationToken).ConfigureAwait(false);
        var session = row?.ToDomain();
        if (session is null || !session.IsUsableAt(timeProvider.GetUtcNow(), stamp))
        {
            return Anonymous(admissionScope, idempotencyKey, remoteAddress);
        }

        return session.PrincipalKind switch
        {
            // La credenziale amministrativa concede anche la regia delle sessioni (decisione D3:
            // installazione a docente singolo). La proprietà della singola sessione la verifica il
            // caso d'uso, non la concessione della policy.
            CredentialPrincipalKind.Admin => new CodiRequestContext(
                isAuthenticated: true,
                [CodiPolicy.Admin, CodiPolicy.SessionController],
                session.Id,
                session.AdminUserId,
                participantId: null,
                admissionScope,
                idempotencyKey,
                remoteAddress),

            CredentialPrincipalKind.Participant when session.ParticipantId is { } participantId =>
                await ResolveParticipantAsync(
                        work, session.Id, participantId, admissionScope, idempotencyKey, remoteAddress, cancellationToken)
                    .ConfigureAwait(false) ?? Anonymous(admissionScope, idempotencyKey, remoteAddress),
            _ => Anonymous(admissionScope, idempotencyKey, remoteAddress),
        };
    }

    /// <summary>
    /// Un contesto senza identità ma con i dati del trasporto: è il contesto dell'ingresso, che
    /// avviene prima che esista una credenziale e ha comunque bisogno del proprio scope.
    /// </summary>
    private static CodiRequestContext Anonymous(
        string? admissionScope,
        string? idempotencyKey,
        string? remoteAddress) =>
        new(
            isAuthenticated: false,
            [CodiPolicy.AnonymousBootstrap],
            credentialSessionId: null,
            adminUserId: null,
            participantId: null,
            admissionScope,
            idempotencyKey,
            remoteAddress);

    /// <summary>
    /// La partecipazione deve essere ammessa e la sessione non terminata: sono le due condizioni
    /// della policy <c>AdmittedParticipant</c> (§9.3). Un partecipante escluso o una sessione chiusa
    /// non ricevono il contesto, e la richiesta si presenta come senza identità.    ///
    /// La policy <c>ActiveParticipantConnection</c> si aggiunge solo se il registro delle connessioni
    /// conferma che il partecipante ha una connessione operativa e sincronizzata: è ciò che distingue
    /// «è entrato» da «sta lavorando adesso». L'**epoca** dichiarata dal corpo non si controlla qui,
    /// perché il contesto non la vede: la verifica dell'epoca è del caso d'uso che la riceve (§12.5).    /// </summary>
    private async Task<CodiRequestContext?> ResolveParticipantAsync(
         IUnitOfWork work,
         Guid credentialSessionId,
         Guid participantId,
         string? admissionScope,
         string? idempotencyKey,
         string? remoteAddress,
         CancellationToken cancellationToken)
    {
        var participantRow = await work.Repo<ISessionParticipantRepository>()
            .GetAsync(participantId.ToString("D"), cancellationToken)
            .ConfigureAwait(false);
        var participant = participantRow?.ToDomain();

        if (participant is null || !participant.IsAdmitted)
        {
            return null;
        }

        var sessionRow = await work.Repo<ILessonSessionRepository>()
            .GetAsync(participant.SessionId.ToString("D"), cancellationToken)
            .ConfigureAwait(false);
        var session = sessionRow?.ToDomain();

        if (session is null)
        {
            return null;
        }

        // La connessione operativa è un'informazione del registro, non della riga: la credenziale
        // dice chi è entrato, il registro dice se sta lavorando adesso. L'epoca che il corpo
        // dichiara la verifica il caso d'uso (§12.5), perché il contesto non la vede.
        var connection = connections.Get(participantId);
        var isOperative = connection is { SnapshotApplied: true, Presence: not ParticipantPresence.NotOperational };
        var finalizations = await AttemptFinalization.ReadAsync(work, credentialSessionId.ToString("D"),
            timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var grants = new List<CodiPolicy>();
        if (finalizations.Count > 0) grants.Add(CodiPolicy.FinalizingParticipant);

        if (session.Status == SessionStatus.Ended)
        {
            if (session.EndedAtUtc is { } ended && ended.Add(AttemptFinalization.Window) <= timeProvider.GetUtcNow())
                return null;
            // Lezione terminata: nessun permesso operativo, ma la connessione corrente può ancora
            // **confermare** i comandi già consegnati prima che la sessione si chiudesse (A-04,
            // §12.7). Senza questo, l'ultimo stato applicato resterebbe senza conferma per sempre e
            // il riepilogo del docente mostrerebbe un lavoro che l'alunno ha invece concluso.
            if (isOperative) grants.Add(CodiPolicy.ActiveParticipantConnection);
            return grants.Count > 0
                ? new CodiRequestContext(
                    isAuthenticated: true,
                    grants,
                    credentialSessionId,
                    adminUserId: null,
                    participantId,
                    admissionScope,
                    idempotencyKey,
                    remoteAddress)
                : null;
        }

        grants.Add(CodiPolicy.AdmittedParticipant);
        if (isOperative) grants.Add(CodiPolicy.ActiveParticipantConnection);
        return new CodiRequestContext(
            isAuthenticated: true,
            grants,
            credentialSessionId,
            adminUserId: null,
            participantId,
            admissionScope,
            idempotencyKey,
            remoteAddress);
    }

    /// <summary>
    /// Chiave di idempotenza dell'intestazione. Un'intestazione vuota o assente vale come assente: a
    /// decidere se la richiesta ne abbia bisogno è il caso d'uso, non il trasporto.
    /// </summary>
    private static string? ReadIdempotencyKey(HttpContext httpContext)
    {
        var value = httpContext.Request.Headers[IdempotencyKeyHeader].ToString().Trim();

        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// Indirizzo di rete del chiamante, quando la connessione ne dichiara uno. È un dato del
    /// trasporto che non concede permessi: serve solo al conteggio dei tentativi di codice di
    /// ingresso (§17.3). Dietro un proxy non configurato è l'indirizzo del proxy: il conteggio
    /// resta corretto, semplicemente più severo.
    /// </summary>
    private static string? ReadRemoteAddress(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString();

    private static Guid? ReadCredentialSessionId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirst(CodiClaimTypes.CredentialSessionId)?.Value;

        return Guid.TryParse(value, out var sessionId) ? sessionId : null;
    }
}
