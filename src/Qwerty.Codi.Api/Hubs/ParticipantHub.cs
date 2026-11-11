using Qwerty.Codi.Application.Dto;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Qwerty.Codi.Api.Dispatch;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Sessions;

namespace Qwerty.Codi.Api.Hubs;

/// <summary>
/// Hub del partecipante (§11.1, §11.2). Il collegamento SignalR è il trasporto: da solo non rende
/// operativo l'alunno. All'apertura il server risolve il contesto dalla credenziale e, solo se la
/// partecipazione è ammessa a una sessione non terminata, associa la connessione al gruppo del
/// partecipante. Altrimenti chiude la connessione: un trasporto senza identità non ha nulla da
/// osservare.
///
/// I metodi del protocollo non contengono regole applicative (§11.7): la mutazione persistente —
/// l'epoca — passa dal comando <c>InitializeParticipantConnection</c> attraverso il dispatcher, con
/// la stessa area e la stessa policy dell'HTTP; presenza, autorizzazione temporanea e sostituzione
/// delle connessioni restano nel registro in memoria.
///
/// L'identità si riverifica a **ogni** metodo, non solo alla connessione: una credenziale revocata
/// deve smettere di valere anche su un trasporto già aperto (§9.2).
/// </summary>
[Authorize]
internal sealed class ParticipantHub(
    ICodiRequestContextProvider contextProvider,
    CodiRequestExecutor executor,
    IParticipantConnectionRegistry connections,
    IServerInstance serverInstance,
    TimeProvider timeProvider, ParticipantObservationService? observations = null, ISessionCoordinator? coordinator = null,
    WorkspacePreviewNotifier? previews = null) : Hub
{
    /// <summary>Anteprima dei blocchi ammessa su SignalR (§15.5): oltre si usa l'endpoint dedicato.</summary>
    private const int SignalRPreviewLimitBytes = 24 * 1024;

    public override async Task OnConnectedAsync()
    {
        var context = await ResolveContextAsync().ConfigureAwait(false);
        if (context?.ParticipantId is not { } participantId)
        {
            return;
        }

        await Groups
            .AddToGroupAsync(Context.ConnectionId, CodiHubGroups.Participant(participantId), Context.ConnectionAborted)
            .ConfigureAwait(false);

        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        // La rimozione avviene solo se connessione ed epoca coincidono ancora: la disconnessione
        // della vecchia pagina, che arriva dopo che la nuova ha acquisito l'operatività, non deve
        // scollegare chi ha preso il posto (§12.5).
        if (Context.Items.TryGetValue(ConnectionItemKey, out var value) &&
            value is ParticipantConnectionRequest acquired)
        {
            connections.Release(acquired.ParticipantId, acquired.ConnectionId, acquired.Epoch);
        }

        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Handshake applicativo (§11.2 passi 3–4): il client dichiara versioni e istanza di pagina, il
    /// server acquisisce l'operatività incrementando l'epoca e gli invia lo stato da applicare. Una
    /// seconda pagina dello stesso partecipante, mentre la prima è operativa, resta in attesa senza
    /// poter modificare nulla (§12.5).
    /// </summary>
    public async Task InitializeClient(InitializeClientArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var context = await ResolveContextAsync().ConfigureAwait(false);
        if (context?.ParticipantId is not { } participantId)
        {
            return;
        }

        var identity = await ExecuteQueryAsync<ParticipantSnapshotDto>(GetParticipantSnapshotQuery.RequestName,
            new { }, context).ConfigureAwait(false);
        await using var coordination = coordinator is null ? null :
            await coordinator.AcquireAsync(identity.SessionId, Context.ConnectionAborted).ConfigureAwait(false);
        var nowUtc = timeProvider.GetUtcNow();
        if (connections.CanAcquire(participantId, arguments.ClientInstanceId, nowUtc) ==
            ConnectionTakeover.OtherInstanceActive)
        {
            await Clients.Caller
                .SendAsync(
                    ParticipantRealtimeEvents.ActiveConnectionExists,
                    new ActiveConnectionExistsDto(
                        "ActiveConnectionExists",
                        connections.Get(participantId)?.Epoch ?? 0,
                        (long)ParticipantProtocol.HeartbeatInterval.TotalSeconds),
                    Context.ConnectionAborted)
                .ConfigureAwait(false);

            return;
        }

        var snapshot = await ExecuteCommandAsync<ParticipantSnapshotDto>(
            InitializeParticipantConnectionCommand.RequestName,
            new
            {
                arguments.ProtocolVersion,
                arguments.ClientInstanceId,
                arguments.AppVersion,
                arguments.EngineVersion,
            },
            context).ConfigureAwait(false);

        var acquired = new ParticipantConnectionRequest(
            participantId,
            snapshot.SessionId,
            arguments.ClientInstanceId,
            Context.ConnectionId,
            snapshot.ConnectionEpoch,
            snapshot.EffectiveRevision);

        var supersededConnectionId = connections.Acquire(acquired, nowUtc);
        Context.Items[ConnectionItemKey] = acquired;

        if (supersededConnectionId is not null)
        {
            // Il vecchio client deve interrompere ogni attività: continuare significherebbe scrivere
            // con un'epoca superata, che il server rifiuta (§12.5).
            await Clients.Client(supersededConnectionId)
                .SendAsync(
                    ParticipantRealtimeEvents.ConnectionSuperseded,
                    new ConnectionSupersededDto("SupersededByNewConnection", snapshot.ConnectionEpoch - 1, snapshot.ConnectionEpoch),
                    Context.ConnectionAborted)
                .ConfigureAwait(false);
        }

        await Clients.Caller
            .SendAsync(ParticipantRealtimeEvents.SessionSnapshot, snapshot, Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Fine del caricamento iniziale (§11.2 passi 6–7): da qui l'autorizzazione temporanea diventa
    /// rinnovabile. Se la revisione dichiarata è ormai superata, il client ha caricato uno stato
    /// vecchio e riceve subito un nuovo snapshot invece di restare operativo su dati sbagliati.
    /// </summary>
    public async Task SnapshotApplied(SnapshotAppliedArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var context = await ResolveContextAsync().ConfigureAwait(false);
        if (context?.ParticipantId is not { } participantId)
        {
            return;
        }

        var current = await ExecuteQueryAsync<ParticipantSnapshotDto>(GetParticipantSnapshotQuery.RequestName,
            new { }, context).ConfigureAwait(false);
        connections.NoteDesiredRevision(participantId, current.EffectiveRevision);
        if (current.EffectiveRevision != arguments.SessionRevision)
        {
            await Clients.Caller.SendAsync(ParticipantRealtimeEvents.SessionSnapshot, current, Context.ConnectionAborted).ConfigureAwait(false);
            return;
        }

        var accepted = connections.MarkSnapshotApplied(
            new SnapshotAppliedRequest(
                participantId,
                Context.ConnectionId,
                arguments.ConnectionEpoch,
                arguments.SessionRevision),
            timeProvider.GetUtcNow());

        if (!accepted)
        {
            await SendSupersededAsync(participantId).ConfigureAwait(false);

            return;
        }

        // La revisione autorevole è quella persistita: se è avanti rispetto a quella che il client
        // dichiara di avere applicato, il client ha caricato uno stato vecchio e riceve subito il
        // nuovo, invece di restare operativo su dati sbagliati (§11.2 passo 7).

    }

    /// <summary>
    /// Battito del client (§11.6): aggiorna la presenza e, se identità, epoca e stato applicato sono
    /// correnti, rinnova l'autorizzazione temporanea di esecuzione (§12.6). Senza rinnovo il player
    /// si sospende: la scadenza è un permesso che finisce, non un errore.
    /// </summary>
    public async Task Heartbeat(HeartbeatArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var context = await ResolveContextAsync().ConfigureAwait(false);
        if (context?.ParticipantId is not { } participantId)
        {
            return;
        }

        if (!context.Allows(CodiPolicy.AdmittedParticipant) ||
            arguments.ServerInstanceId is { } instance && instance != serverInstance.InstanceId)
        {
            await SendSupersededAsync(participantId).ConfigureAwait(false);
            return;
        }

        var snapshot = await ExecuteQueryAsync<ParticipantSnapshotDto>(GetParticipantSnapshotQuery.RequestName,
            new { }, context).ConfigureAwait(false);
        connections.NoteDesiredRevision(participantId, snapshot.EffectiveRevision);
        var outcome = connections.Heartbeat(
            new HeartbeatRequest(
                participantId,
                Context.ConnectionId,
                arguments.ConnectionEpoch,
                arguments.Nonce,
                arguments.SessionRevision,
                arguments.PageVisible),
            timeProvider.GetUtcNow());

        if (outcome.Presence == ParticipantPresence.Disconnected)
        {
            await SendSupersededAsync(participantId).ConfigureAwait(false);

            return;
        }

        if (outcome.Lease is { } lease)
        {
            await Clients.Caller
                .SendAsync(
                    ParticipantRealtimeEvents.LeaseGranted,
                    new ExecutionLeaseDto(
                        lease.Nonce,
                        lease.EffectiveRevision,
                        lease.ConnectionEpoch,
                        (long)lease.MaxDuration.TotalSeconds,
                        lease.ValidUntilUtc,
                        serverInstance.InstanceId),
                    Context.ConnectionAborted)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Conferma applicativa (§12.2): il client dichiara che cosa ha applicato di un comando ricevuto.
    /// Il caso d'uso registra la consegna; qui si aggiorna la revisione applicata nel registro, così
    /// l'autorizzazione temporanea può tornare a rinnovarsi (§12.6) e il battito successivo non
    /// resta senza risposta.
    /// </summary>
    public async Task CommandAcknowledged(CommandAcknowledgementArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var context = await ResolveContextAsync().ConfigureAwait(false);
        if (context?.ParticipantId is not { } participantId)
        {
            return;
        }

        var operational = connections.Get(participantId);
        if (operational is null || operational.ConnectionId != Context.ConnectionId || operational.Epoch != arguments.ConnectionEpoch)
        {
            await SendSupersededAsync(participantId).ConfigureAwait(false);
            return;
        }

        await ExecuteCommandAsync<CommandAcknowledgementDto>(
            AcknowledgeCommandCommand.RequestName,
            new
            {
                arguments.CommandId,
                arguments.ProtocolVersion,
                arguments.EffectiveRevision,
                arguments.ConnectionEpoch,
                arguments.Outcome,
                arguments.RuntimeStatus,
                arguments.LastPersistedProjectRevision,
                arguments.ErrorCode,
            },
            context).ConfigureAwait(false);

        if (arguments.Outcome is "Applied" or "AppliedWithRecoveryRequired") connections.NoteAppliedRevision(
            participantId,
            Context.ConnectionId,
            arguments.ConnectionEpoch,
            arguments.EffectiveRevision);
    }

    public async Task ActivityStateUpdated(ActivityStateArguments arguments)
    {
        var context = await ResolveContextAsync().ConfigureAwait(false);
        if (context is null || observations is null) return;
        try
        {
            await observations.UpdateAsync(context, Context.ConnectionId, arguments, Context.ConnectionAborted).ConfigureAwait(false);
        }
        catch (CodiRequestException exception) when (exception.Code == CodiErrorCodes.SessionStateConflict)
        {
            // Una telemetria in viaggio può precedere lo stato appena comandato.
            // Il servizio rifiuta l'osservazione senza mutazioni; il trasporto ignora il dato obsoleto.
        }
    }

    /// <summary>
    /// Anteprima dei blocchi via SignalR (§11.3, §15.5): il client manda la forma compatta entro il
    /// limite; oltre quel limite usa l'endpoint dedicato di §10.3. L'anteprima non è un salvataggio:
    /// passa dal comando <c>PublishWorkspacePreview</c> attraverso il dispatcher, con la stessa area
    /// e la stessa policy dell'HTTP (§11.7), e finisce in una cache in memoria.
    /// </summary>
    public async Task WorkspacePreviewUpdated(WorkspacePreviewArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var context = await ResolveContextAsync().ConfigureAwait(false);
        if (context?.ParticipantId is not { } participantId)
        {
            return;
        }

        var operational = connections.Get(participantId);
        if (operational is null || operational.ConnectionId != Context.ConnectionId ||
            operational.Epoch != arguments.ConnectionEpoch)
        {
            await SendSupersededAsync(participantId).ConfigureAwait(false);

            return;
        }

        if (arguments.ProtocolVersion != ParticipantProtocol.CurrentVersion ||
            Encoding.UTF8.GetByteCount(arguments.WorkspaceJson) > SignalRPreviewLimitBytes)
        {
            throw CodiRequestException.InvalidPayload(PublishWorkspacePreviewCommand.RequestName);
        }

        var preview = await ExecuteCommandAsync<WorkspacePreviewDto>(
            PublishWorkspacePreviewCommand.RequestName,
            new
            {
                arguments.AttemptId,
                arguments.ConnectionEpoch,
                arguments.ExecutionGeneration,
                arguments.ProjectRevision,
                arguments.WorkspaceHash,
                arguments.WorkspaceJson,
            },
            context).ConfigureAwait(false);

        if (previews is not null)
        {
            await previews.NotifyAsync(preview, Context.ConnectionAborted).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Messaggio dell'alunno nella propria chat (§11.3, §8): il trasporto non contiene regole applicative,
    /// quindi il messaggio passa dal comando <c>PostParticipantMessage</c> attraverso il dispatcher, con la
    /// stessa area e la stessa policy dell'HTTP (§11.7). Il thread vive in memoria e non si replaya: la
    /// pagina legge il thread con la query all'apertura e riceve solo i messaggi nuovi.
    ///
    /// L'avviso al docente è del caso d'uso attraverso l'astrazione di §12.2, così il canale hub e l'HTTP
    /// comportano allo stesso modo: il trasporto qui si limita a tradurre il metodo del protocollo.
    /// </summary>
    public async Task PostMessage(PostMessageArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var context = await ResolveContextAsync().ConfigureAwait(false);
        if (context is null) return;

        await ExecuteCommandAsync<ParticipantMessageDto>(
            PostParticipantMessageCommand.RequestName,
            new { arguments.Text, arguments.ClientMessageKey },
            context).ConfigureAwait(false);
    }

    /// <summary>Chiave con cui la connessione acquisita è conservata negli elementi del contesto.</summary>
    private const string ConnectionItemKey = "participant-connection";

    /// <summary>
    /// Risolve il contesto del partecipante e, se la credenziale non vale più o la partecipazione
    /// non è più ammessa, avvisa il client e chiude: un trasporto aperto con una credenziale revocata
    /// non deve poter continuare a ricevere né a inviare (§9.2).
    /// </summary>
    private async Task<CodiRequestContext?> ResolveContextAsync()
    {
        var httpContext = Context.GetHttpContext();
        if (httpContext is null)
        {
            Context.Abort();

            return null;
        }

        var context = await contextProvider
            .ResolveRequestContextAsync(httpContext, Context.ConnectionAborted)
            .ConfigureAwait(false);

        // Basta una delle due concessioni: la partecipazione ammessa (il caso ordinario) oppure la
        // connessione operativa, che resta valida anche su una lezione appena terminata per
        // confermare i comandi già consegnati (A-04). La policy della singola richiesta la verifica
        // comunque il dispatcher (§11.7): qui non si decide che cosa è permesso, solo chi è.
        if (context.ParticipantId is not null &&
            (context.Allows(CodiPolicy.AdmittedParticipant) || context.Allows(CodiPolicy.ActiveParticipantConnection)))
        {
            return context;
        }

        await Clients.Caller
            .SendAsync(
                ParticipantRealtimeEvents.ParticipationRevoked,
                new ParticipationRevokedDto("CredentialNotUsable"),
                Context.ConnectionAborted)
            .ConfigureAwait(false);

        Context.Abort();

        return null;
    }

    /// <summary>
    /// La connessione non è più quella corrente: il client deve smettere di agire e riacquisire
    /// l'operatività con un nuovo handshake.
    /// </summary>
    private async Task SendSupersededAsync(Guid participantId)
    {
        await Clients.Caller
            .SendAsync(
                ParticipantRealtimeEvents.ConnectionSuperseded,
                new ConnectionSupersededDto("SupersededByNewConnection", 0, connections.Get(participantId)?.Epoch ?? 0),
                Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Esegue un comando del catalogo sull'hub (§11.7): stesso registro, stessa area e stessa policy
    /// dell'HTTP. Il corpo viaggia nella forma del contratto, così i contatori a 64 bit restano
    /// stringhe decimali anche su questo canale.
    /// </summary>
    private async Task<TResponse> ExecuteCommandAsync<TResponse>(
        string requestName,
        object body,
        CodiRequestContext context) =>
        (TResponse)(await ExecuteAsync(CodiRequestKind.Command, requestName, body, context).ConfigureAwait(false))!;

    /// <inheritdoc cref="ExecuteCommandAsync{TResponse}"/>
    private async Task<TResponse> ExecuteQueryAsync<TResponse>(
        string requestName,
        object body,
        CodiRequestContext context) =>
        (TResponse)(await ExecuteAsync(CodiRequestKind.Query, requestName, body, context).ConfigureAwait(false))!;

    private async Task<object?> ExecuteAsync(
        CodiRequestKind kind,
        string requestName,
        object body,
        CodiRequestContext context)
    {
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(body, CodiJson.Payload));

        return await executor
            .ExecuteAsync(
                kind,
                requestName,
                CodiRequestChannel.Hub,
                payload.RootElement,
                context,
                Context.ConnectionAborted)
            .ConfigureAwait(false);
    }
}
