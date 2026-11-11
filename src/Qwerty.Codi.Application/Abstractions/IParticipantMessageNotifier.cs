using Qwerty.Codi.Application.Dto;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Avvisa le parti della chat quando un messaggio entra nel thread (§8, §15.1).
///
/// La chat non passa dall'outbox di §12.2: non c'è una consegna da registrare, un ACK da attendere né un
/// esito da annotare. Un destinatario assente non è un errore — il messaggio resta nel thread e chi torna
/// lo legge con la query. L'implementazione conosce il trasporto (gruppi e nomi degli eventi); il caso d'uso
/// decide solo che cosa entra nel thread e a quale parte va portato.
///
/// È la stessa separazione di <see cref="ICommandDeliveryNotifier"/>: l'applicazione chiede l'avviso, il
/// trasporto lo consegna.
/// </summary>
public interface IParticipantMessageNotifier
{
    /// <summary>Avvisa il docente che osserva la sessione del partecipante.</summary>
    Task SendToTeachersAsync(ParticipantMessageDto message, CancellationToken cancellationToken);

    /// <summary>Avvisa il partecipante proprietario del thread.</summary>
    Task SendToParticipantAsync(ParticipantMessageDto message, CancellationToken cancellationToken);
}
