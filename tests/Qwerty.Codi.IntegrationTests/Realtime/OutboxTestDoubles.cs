using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Sessions;

namespace Qwerty.Codi.IntegrationTests.Realtime;

/// <summary>
/// Trasporto finto: registra le consegne ricevute e può fallire su richiesta. Il dispatcher non
/// conosce SignalR, quindi i suoi test non hanno bisogno di un hub: verificano che cosa l'applicazione
/// ha deciso di inviare e a chi.
/// </summary>
internal sealed class RecordingCommandDeliveryNotifier : ICommandDeliveryNotifier
{
    public List<(Guid ParticipantId, CommandIssuedMessageDto Message)> Sent { get; } = [];

    /// <summary>Se impostata, ogni invio fallisce con questa eccezione.</summary>
    public Exception? Failure { get; set; }

    public Task SendCommandIssuedAsync(
        Guid participantId,
        CommandIssuedMessageDto message,
        CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Sent.Add((participantId, message));

        return Task.CompletedTask;
    }
}
