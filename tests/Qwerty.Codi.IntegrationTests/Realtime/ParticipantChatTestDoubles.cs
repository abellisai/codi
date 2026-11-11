using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dto;

namespace Qwerty.Codi.IntegrationTests.Realtime;

/// <summary>
/// Trasporto finto della chat (§8, §15.1). Il caso d'uso non conosce SignalR: qui si registra quale
/// parte avrebbe ricevuto l'avviso, così i test verificano che una scrittura nuova arrivi una sola
/// volta e che la ripetizione con la stessa chiave non riavvisi.
/// </summary>
internal sealed class RecordingParticipantMessageNotifier : IParticipantMessageNotifier
{
    /// <summary>Avvisi ricevuti dal docente, nell'ordine.</summary>
    public List<ParticipantMessageDto> ToTeachers { get; } = [];

    /// <summary>Avvisi ricevuti dal partecipante, nell'ordine.</summary>
    public List<ParticipantMessageDto> ToParticipant { get; } = [];

    public Task SendToTeachersAsync(ParticipantMessageDto message, CancellationToken cancellationToken)
    {
        ToTeachers.Add(message);

        return Task.CompletedTask;
    }

    public Task SendToParticipantAsync(ParticipantMessageDto message, CancellationToken cancellationToken)
    {
        ToParticipant.Add(message);

        return Task.CompletedTask;
    }
}
