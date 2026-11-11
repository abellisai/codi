using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Qwerty.Codi.Api.Authentication;
using Qwerty.Codi.Api.Contracts;
using Qwerty.Codi.Api.Dispatch;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Sessions;

namespace Qwerty.Codi.Api.Endpoints.Public;

/// <summary>
/// Gli endpoint dedicati del partecipante (§10.3): l'ingresso idempotente e il profilo del
/// contesto corrente. Sono dedicati perché il trasporto fa parte del contratto — l'ingresso **emette
/// il cookie** di autenticazione, cosa che il dispatcher non può fare — e delegano ai casi d'uso
/// senza contenere regole applicative.
///
/// L'ingresso resta raggiungibile anche dal dispatcher generico dell'area pubblica, perché §10.1 lo
/// ammette: le due porte eseguono lo stesso comando, con lo stesso contesto di trasporto.
/// </summary>
internal static class PublicParticipantEndpoints
{
    private const string JoinRoute = "/join";
    private const string MeRoute = "/auth/me";
    private const string LeaveRoute = "/participant/leave";

    public static IEndpointRouteBuilder MapCodiPublicParticipants(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(JoinRoute, JoinAsync);
        endpoints.MapGet(MeRoute, CurrentContextAsync);
        endpoints.MapPost(LeaveRoute, LeaveAsync);

        return endpoints;
    }

    /// <summary>
    /// Ingresso idempotente (§9.4). Il corpo è il comando; il trasporto aggiunge ciò che il corpo
    /// non può dichiarare — lo scope dal ticket di ammissione e la chiave di idempotenza
    /// dall'intestazione — e traduce l'esito in un cookie firmato. La marca di sicurezza entra nel
    /// cookie e **non** nella risposta: la risposta resta un profilo minimo, adatto a essere
    /// registrato dal client.
    /// </summary>
    private static async Task<IResult> JoinAsync(
        HttpContext httpContext,
        CodiRequestExecutor executor,
        ICodiRequestContextProvider contextProvider,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        var context = await contextProvider
            .ResolveRequestContextAsync(httpContext, cancellationToken)
            .ConfigureAwait(false);

        using var payload = await DispatcherEndpoints
            .ReadPayloadAsync(httpContext.Request, JoinSessionCommand.RequestName, cancellationToken)
            .ConfigureAwait(false);

        var result = await executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                JoinSessionCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                context,
                cancellationToken)
            .ConfigureAwait(false);

        if (result is not JoinSessionOutcome outcome)
        {
            // Un esito di tipo diverso è un difetto di programmazione, non un errore del client.
            throw new InvalidOperationException(
                "L'ingresso non ha restituito la partecipazione e la credenziale emessa.");
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, outcome.Joined.DisplayName),
                new Claim(CodiClaimTypes.CredentialSessionId, outcome.Joined.CredentialSessionId.ToString("D")),
                new Claim(CodiClaimTypes.CredentialStamp, outcome.CredentialStamp),
            ],
            ParticipantCookieAuthentication.SchemeName);

        var principal = new ClaimsPrincipal(identity);
        await httpContext
            .SignInAsync(ParticipantCookieAuthentication.SchemeName, principal)
            .ConfigureAwait(false);

        // Il ticket di ammissione ha finito il suo compito: la credenziale lo sostituisce.
        AdmissionCookie.Clear(httpContext);

        // SignInAsync scrive il ticket nella risposta ma non cambia User su questa richiesta: il
        // request token va emesso per l'identità appena autenticata, come per l'accesso docente.
        httpContext.User = principal;
        CodiAntiforgery.IssueRequestToken(httpContext, antiforgery);

        return Results.Ok(new ParticipantJoinResponse(
            outcome.Joined.ParticipantId,
            outcome.Joined.SessionId,
            outcome.Joined.DisplayName,
            outcome.Joined.View));
    }

    /// <summary>
    /// Profilo del partecipante (§10.3): chi è e che cosa deve mostrare. Non si limita ai claim del
    /// cookie, perché la credenziale viene riletta a ogni richiesta dal contesto: una credenziale
    /// revocata non è più valida solo perché il cookie è ancora nel browser (§9.2).
    /// </summary>
    private static async Task<IResult> CurrentContextAsync(
        HttpContext httpContext,
        CodiRequestExecutor executor,
        ICodiRequestContextProvider contextProvider,
        CancellationToken cancellationToken)
    {
        var context = await contextProvider
            .ResolveRequestContextAsync(httpContext, cancellationToken)
            .ConfigureAwait(false);

        using var payload = JsonDocument.Parse("{}");

        var result = await executor
            .ExecuteAsync(
                CodiRequestKind.Query,
                GetParticipantContextQuery.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                context,
                cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(result);
    }

    /// <summary>
    /// Uscita volontaria dell'alunno (§9.2, §10.3): esegue <c>LeaveSession</c>, che revoca la
    /// credenziale, e cancella il cookie del partecipante. È un endpoint dedicato perché il
    /// trasporto fa parte del contratto — come l'ingresso emette il cookie, l'uscita lo rimuove —
    /// mentre il caso d'uso non conosce il cookie.
    ///
    /// La revoca vale anche per una connessione già aperta, perché la credenziale viene riletta a
    /// ogni richiesta (§9.2). Il token antiforgery legato all'identità uscente viene cancellato
    /// insieme al cookie: un ingresso successivo ne emette uno nuovo.
    /// </summary>
    private static async Task<IResult> LeaveAsync(
        HttpContext httpContext,
        CodiRequestExecutor executor,
        ICodiRequestContextProvider contextProvider,
        CancellationToken cancellationToken)
    {
        var context = await contextProvider
            .ResolveRequestContextAsync(httpContext, cancellationToken)
            .ConfigureAwait(false);

        using var payload = JsonDocument.Parse("{}");

        // Senza credenziale valida la richiesta riceve 401, come prescrive §10.3: l'uscita è
        // un'azione di chi è dentro, non un modo per cancellare un cookie qualsiasi.
        await executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                LeaveSessionCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                context,
                cancellationToken)
            .ConfigureAwait(false);

        await httpContext.SignOutAsync(ParticipantCookieAuthentication.SchemeName).ConfigureAwait(false);
        httpContext.Response.Cookies.Delete(CodiAntiforgery.PublicCookieName, new CookieOptions
        {
            Secure = true,
            Path = "/",
            SameSite = SameSiteMode.Strict,
        });

        return Results.NoContent();
    }
}
