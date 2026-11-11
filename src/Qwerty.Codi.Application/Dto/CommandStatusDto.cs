namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Stato delle consegne di un comando (§10.6): il comando registrato, il riepilogo e l'elenco per
/// destinatario. Il riepilogo ha le tre caselle del contratto; una consegna **superata** non rientra
/// in nessuna delle tre e resta visibile nell'elenco, dove conserva la sua ricevuta storica.
/// </summary>
public sealed record CommandStatusDto(
    Guid CommandId,
    Guid SessionId,
    string CommandName,
    long Revision,
    DeliverySummaryDto Summary,
    IReadOnlyList<CommandDeliveryDto> Deliveries);
