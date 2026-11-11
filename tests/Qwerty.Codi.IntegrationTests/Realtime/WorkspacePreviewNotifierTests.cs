using Microsoft.AspNetCore.SignalR;
using Qwerty.Codi.Api.Hubs;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Sessions;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Realtime;

/// <summary>
/// Avviso al docente che osserva (§15.5): l'evento dice che un'anteprima è arrivata e per chi, ma
/// non porta il progetto. Il contenuto si legge a parte, con la query dedicata, così il canale
/// degli aggiornamenti resta a bassa frequenza (§15.6) e il docente apre un solo lavoro alla volta
/// (§14.3).
/// </summary>
public sealed class WorkspacePreviewNotifierTests
{
    [Fact]
    public async Task NotifyAsync_AvvisaSoloIDocentiDellaSessione_SenzaIlProgetto()
    {
        var clients = new FakeHubClients();
        var notifier = new WorkspacePreviewNotifier(new NotifierHubContext(clients));
        var participantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var preview = new WorkspacePreviewDto(participantId, sessionId, Guid.NewGuid(), 4, "impronta",
            CodiUseCaseHost.Now);

        await notifier.NotifyAsync(preview, CancellationToken.None);

        var sent = Assert.Single(clients.Sent);
        Assert.Equal($"group:{CodiHubGroups.Teachers(sessionId)}", sent.Target);
        Assert.Equal(TeacherRealtimeEvents.WorkspacePreviewAvailable, sent.Method);
        var notice = Assert.IsType<WorkspacePreviewNoticeDto>(sent.Arguments[0]);
        Assert.Equal(participantId, notice.ParticipantId);
        Assert.Equal(sessionId, notice.SessionId);
        Assert.Equal(4, notice.ProjectRevision);
        Assert.Equal("impronta", notice.WorkspaceHash);
        Assert.Equal(CodiUseCaseHost.Now, notice.ReceivedAtUtc);
        // Cinque campi, nessuno in più: il tentativo e il progetto dell'alunno non viaggiano qui.
        Assert.Equal(5, typeof(WorkspacePreviewNoticeDto).GetProperties().Length);
    }

    private sealed class NotifierHubContext(FakeHubClients clients) : IHubContext<TeacherHub>
    {
        public IHubClients Clients => clients;
        public IGroupManager Groups { get; } = new FakeGroupManager();
    }
}
