using Microsoft.AspNetCore.SignalR;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Sessions;

namespace Qwerty.Codi.Api.Hubs;

/// <summary>
/// Avvisa i docenti che osservano la sessione quando arriva una nuova anteprima (§15.5). Il
/// contenuto non viaggia qui: l'evento porta identità, revisione, impronta e istante, e il docente
/// legge il progetto con <c>GetParticipantPreview</c> quando gli serve. Così le anteprime non
/// riempiono il canale degli aggiornamenti, che resta a bassa frequenza (§15.6).
/// </summary>
internal sealed class WorkspacePreviewNotifier(IHubContext<TeacherHub> hub)
{
    public Task NotifyAsync(WorkspacePreviewDto preview, CancellationToken cancellationToken) =>
        hub.Clients
            .Group(CodiHubGroups.Teachers(preview.SessionId))
            .SendAsync(
                TeacherRealtimeEvents.WorkspacePreviewAvailable,
                new WorkspacePreviewNoticeDto(
                    preview.ParticipantId,
                    preview.SessionId,
                    preview.ProjectRevision,
                    preview.WorkspaceHash,
                    preview.ReceivedAtUtc),
                cancellationToken);
}
