using Microsoft.AspNetCore.SignalR;
using Qwerty.Codi.Api.Hubs;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.Domain.Sessions;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Realtime;

/// <summary>
/// Instradamento degli avvisi della chat (§8, §15.1). Il caso d'uso decide che cosa entra nel thread;
/// qui si verifica solo a chi arriva il messaggio nuovo: l'alunno interessato e i docenti della sua
/// sessione. La chat non passa dall'outbox, quindi non c'è una consegna da registrare né un ACK da
/// attendere — un destinatario assente non è un errore, il messaggio resta nel thread.
/// </summary>
public sealed class ParticipantMessageNotifierTests
{
    private readonly FakeHubClients _participantClients = new();
    private readonly FakeHubClients _teacherClients = new();
    private readonly ParticipantMessageNotifier _notifier;

    public ParticipantMessageNotifierTests() =>
        _notifier = new ParticipantMessageNotifier(
            new FakeHubContext(_participantClients),
            new FakeHubContext(_teacherClients));

    [Fact]
    public async Task MessaggioDelDocente_VaSoloAlGruppoDiQuelPartecipante()
    {
        var message = Message(MessageAuthor.Teacher, "Hai bisogno di aiuto?");

        await _notifier.SendToParticipantAsync(message, CancellationToken.None);

        var sent = Assert.Single(_participantClients.Sent);
        Assert.Equal($"group:{CodiHubGroups.Participant(message.ParticipantId)}", sent.Target);
        Assert.Equal(ParticipantRealtimeEvents.ParticipantMessageReceived, sent.Method);
        // Il payload è il tipo dichiarato del contratto: il client legge lo stesso tipo della query.
        Assert.IsType<ParticipantMessageDto>(sent.Arguments[0]);
        Assert.Empty(_teacherClients.Sent);
    }

    [Fact]
    public async Task MessaggioDellAlunno_VaSoloAiDocentiDellaSessione()
    {
        var message = Message(MessageAuthor.Participant, "Non capisco il blocco.");

        await _notifier.SendToTeachersAsync(message, CancellationToken.None);

        var sent = Assert.Single(_teacherClients.Sent);
        Assert.Equal($"group:{CodiHubGroups.Teachers(message.SessionId)}", sent.Target);
        Assert.Equal(TeacherRealtimeEvents.ParticipantMessagePosted, sent.Method);
        Assert.IsType<ParticipantMessageDto>(sent.Arguments[0]);
        // Nessun alunno riceve la chat di un altro: la chat non esiste fra bambini (§8).
        Assert.Empty(_participantClients.Sent);
    }

    private ParticipantMessageDto Message(MessageAuthor author, string text) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), author.ToString(), text, CodiUseCaseHost.Now);

    private sealed class FakeHubContext(FakeHubClients clients) : IHubContext<TeacherHub>, IHubContext<ParticipantHub>
    {
        public IHubClients Clients => clients;
        public IGroupManager Groups { get; } = new FakeGroupManager();
    }
}
