using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Qwerty.Codi.Api.Dispatch;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Sessions;

namespace Qwerty.Codi.Api.Hubs;

/// <summary>
/// Hub del docente (§11.1). Serve l'osservazione della sessione: il docente dichiara quale sessione
/// vuole seguire e il server, dopo averne verificato la proprietà, lo associa al gruppo dei docenti
/// di quella sessione. Non esiste un metodo alternativo per impartire comandi didattici: quelli
/// restano sul dispatcher admin (§11.1).
///
/// <see cref="ObserveSession"/> non contiene regole applicative: esegue la query <c>GetSession</c>
/// attraverso il dispatcher, con lo stesso registro, la stessa area e la stessa policy dell'HTTP
/// (§11.7). Un identificativo di sessione che non appartiene al docente è rifiutato dal caso d'uso,
/// non da un controllo scritto nell'hub.
///
/// I metodi che riportano la fotografia completa dei partecipanti e le ricevute dei comandi
/// appartengono alla regia realtime (T4), quando esistono telemetria e outbox.
/// </summary>
[Authorize]
internal sealed class TeacherHub(
    CodiRequestExecutor executor,
    ICodiRequestContextProvider contextProvider, TeacherObservationRegistry? observations = null) : Hub
{
    /// <summary>
    /// Segue una sessione: verifica la proprietà leggendola dal dispatcher e associa la connessione
    /// al gruppo dei docenti. Ripetere l'osservazione sulla stessa sessione è un'aggiunta al gruppo
    /// già presente, senza effetti.
    /// </summary>
    public async Task ObserveSession(Guid sessionId)
    {
        var httpContext = Context.GetHttpContext();
        if (httpContext is null)
        {
            Context.Abort();

            return;
        }

        var context = await contextProvider
            .ResolveRequestContextAsync(httpContext, Context.ConnectionAborted)
            .ConfigureAwait(false);

        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(new { sessionId }, CodiJson.Payload));

        // La lettura dal dispatcher è ciò che autorizza l'osservazione: policy e proprietà della
        // risorsa sono le stesse dell'HTTP, quindi un hub autenticato non diventa onnipotente.
        var snapshot = await executor
            .ExecuteAsync(
                CodiRequestKind.Query,
                GetSessionOverviewQuery.RequestName,
                CodiRequestChannel.Hub,
                payload.RootElement,
                context,
                Context.ConnectionAborted)
            .ConfigureAwait(false);

        if (Context.Items.TryGetValue("observed-session", out var prior) && prior is Guid previous && previous != sessionId)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, CodiHubGroups.Teachers(previous), Context.ConnectionAborted).ConfigureAwait(false);
        Context.Items["observed-session"] = sessionId;
        observations?.Observe(new TeacherObservation(Context.ConnectionId, sessionId, httpContext.User));
        if (Clients is not null)
            await Clients.Caller.SendAsync("TeacherSnapshot", snapshot, Context.ConnectionAborted).ConfigureAwait(false);

        await Groups
            .AddToGroupAsync(Context.ConnectionId, CodiHubGroups.Teachers(sessionId), Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Messaggio del docente verso un singolo alunno (§11.3, §8): anche qui il trasporto non contiene regole
    /// applicative. Il comando <c>PostTeacherMessage</c> passa dal dispatcher, con la stessa area e la stessa
    /// policy dell'HTTP, e la proprietà della sessione viene verificata dal caso d'uso e non dall'hub.
    /// </summary>
    public async Task PostMessage(PostTeacherMessageArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var httpContext = Context.GetHttpContext();
        if (httpContext is null)
        {
            Context.Abort();

            return;
        }

        var context = await contextProvider
            .ResolveRequestContextAsync(httpContext, Context.ConnectionAborted)
            .ConfigureAwait(false);

        await executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                PostTeacherMessageCommand.RequestName,
                CodiRequestChannel.Hub,
                JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    arguments.SessionId,
                    arguments.ParticipantId,
                    arguments.Text,
                    arguments.ClientMessageKey,
                }, CodiJson.Payload)).RootElement,
                context,
                Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        observations?.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
