using Microsoft.AspNetCore.SignalR;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Sessions;

namespace Qwerty.Codi.Api.Hubs;

/// <summary>
/// Consegna ai partecipanti attraverso gli hub (§11.1, §11.4, §11.7): il comando già deciso
/// dall'applicazione diventa un involucro **per destinatario** — con partecipante, epoca corrente e
/// istanza del server — e entra nel gruppo che il server ha assegnato alla connessione. Il trasporto
/// non decide nulla: che cosa inviare e a chi è già stato stabilito e persistito.
///
/// La revisione richiesta viene registrata nel registro delle connessioni mentre il messaggio parte:
/// da quel momento l'autorizzazione temporanea non si rinnova finché il client non conferma di avere
/// applicato il nuovo stato (§12.6). Un'epoca mancante — nessuna connessione nota — vale zero, che il
/// client riconosce come «nessuna connessione operativa» e non come un'epoca valida.
///
/// Un gruppo senza connessioni non è un errore: il messaggio risulta inviato al trasporto e resta
/// senza conferma fino all'ACK, mentre il client che torna riceve lo snapshot corrente e non una
/// coda di comandi vecchi (§12.2).
/// </summary>
internal sealed class HubCommandDeliveryNotifier(
    IHubContext<ParticipantHub> hubContext,
    IParticipantConnectionRegistry connections,
    IServerInstance serverInstance,
    IServiceScopeFactory scopes,
    TimeProvider time) : ICommandDeliveryNotifier
{
    public async Task SendCommandIssuedAsync(
        Guid participantId,
        CommandIssuedMessageDto message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        connections.NoteDesiredRevision(participantId, message.EffectiveRevision);

        var envelope = new CommandIssuedEnvelopeDto(
            message.ProtocolVersion,
            serverInstance.InstanceId,
            message.SessionId,
            participantId,
            message.CommandId,
            message.EffectiveRevision,
            connections.Get(participantId)?.Epoch ?? 0,
            message.IssuedAtUtc,
            message.CommandName,
            message.DesiredStates?.GetValueOrDefault(participantId) ?? message.DesiredState);

        if (envelope.DesiredState.View == "Goodbye")
        {
            await using var scope = scopes.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkContext>();
            await using var work = await factory.CreateAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var id = participantId.ToString("D");
            var credentials = await work.Repo<ICredentialSessionRepository>().QueryAsync(
                row => row.ParticipantId == id && row.RevokedAtUtcMs == null, cancellationToken).ConfigureAwait(false);
            var grants = new List<AttemptFinalizationGrantDto>();
            foreach (var credential in credentials)
                grants.AddRange(await AttemptFinalization.ReadAsync(work, credential.Id, time.GetUtcNow(), cancellationToken).ConfigureAwait(false));
            await hubContext.Clients.Group(CodiHubGroups.Participant(participantId)).SendAsync(
                ParticipantRealtimeEvents.SessionEnded,
                new SessionEndedMessageDto(
                    envelope,
                    grants,
                    message.IssuedAtUtc.Add(AttemptFinalization.Window)),
                cancellationToken).ConfigureAwait(false);
        }
        await hubContext.Clients
            .Group(CodiHubGroups.Participant(participantId))
            .SendAsync(CommandIssuedMessageDto.MessageType, envelope, cancellationToken);
    }
}
