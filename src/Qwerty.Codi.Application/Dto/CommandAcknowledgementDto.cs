namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Esito registrato della conferma: lo stato della consegna dopo la transizione e se l'applicazione
/// richiede un recupero. Il docente lo vede nel riepilogo di §10.6; il client lo usa per sapere se
/// il server ha accettato la propria dichiarazione.
/// </summary>
public sealed record CommandAcknowledgementDto(
    Guid CommandId,
    string DeliveryStatus,
    bool RequiresRecovery);
