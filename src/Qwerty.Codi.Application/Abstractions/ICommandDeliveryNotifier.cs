using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Sessions;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Consegna di un messaggio al trasporto di un destinatario (§12.2). L'applicazione decide **che
/// cosa** inviare e a chi; come raggiungerlo — un gruppo dell'hub del partecipante — è una scelta
/// del trasporto, che vive nell'API.
///
/// Il dispatcher outbox non conosce SignalR: chiede una consegna e registra l'esito. Un errore del
/// trasporto è un esito da annotare, non un'eccezione che deve fermare la coda.
/// </summary>
public interface ICommandDeliveryNotifier
{
    /// <summary>
    /// Invia il messaggio <c>CommandIssued</c> al destinatario. Il destinatario è un partecipante:
    /// il gruppo di §11.1 lo individua, quindi il messaggio non deve portare l'identità per essere
    /// instradato.
    /// </summary>
    Task SendCommandIssuedAsync(
        Guid participantId,
        CommandIssuedMessageDto message,
        CancellationToken cancellationToken);
}
