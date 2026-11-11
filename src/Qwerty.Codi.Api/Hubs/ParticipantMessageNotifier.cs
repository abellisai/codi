using Microsoft.AspNetCore.SignalR;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Sessions;

namespace Qwerty.Codi.Api.Hubs;

/// <summary>
/// Avvisa le due parti della chat quando un messaggio entra nel thread (§8, §15.1). Il messaggio è il tipo
/// dichiarato del contratto, non un oggetto costruito qui: il client legge sempre lo stesso tipo che ottiene
/// con la query (§11.3, §11.4).
///
/// La chat non passa dall'outbox di §12.2: non c'è una consegna da registrare, un ACK da attendere o un esito
/// da annotare. Un destinatario assente non è un errore — il messaggio resta nel thread e chi torna lo legge
/// con la query. Il caso d'uso decide **che cosa** entra nel thread; qui si decide solo **a chi** arriva il
/// nuovo messaggio, come per l'anteprima di §15.5.
/// </summary>
internal sealed class ParticipantMessageNotifier(
    IHubContext<ParticipantHub> participantHub,
    IHubContext<TeacherHub> teacherHub) : IParticipantMessageNotifier
{
    public Task SendToParticipantAsync(ParticipantMessageDto message, CancellationToken cancellationToken) =>
        participantHub.Clients
            .Group(CodiHubGroups.Participant(message.ParticipantId))
            .SendAsync(
                ParticipantRealtimeEvents.ParticipantMessageReceived,
                message,
                cancellationToken);

    public Task SendToTeachersAsync(ParticipantMessageDto message, CancellationToken cancellationToken) =>
        teacherHub.Clients
            .Group(CodiHubGroups.Teachers(message.SessionId))
            .SendAsync(
                TeacherRealtimeEvents.ParticipantMessagePosted,
                message,
                cancellationToken);
}
