namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Esito registrato dell'ingresso: i riferimenti alle entità create e ciò che serve alla pagina per
/// mostrare il primo stato. È anche il documento conservato dal registro di idempotenza (§7.5),
/// quindi non contiene segreti: la marca di sicurezza della credenziale resta nel cookie protetto.
/// </summary>
public sealed record ParticipantJoinedDto(
    Guid ParticipantId,
    Guid SessionId,
    string DisplayName,
    string View,
    Guid CredentialSessionId);
