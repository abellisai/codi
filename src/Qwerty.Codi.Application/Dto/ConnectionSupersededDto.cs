namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// La connessione ha perso l'operatività perché un'altra l'ha acquisita (§11.3): il vecchio client
/// deve interrompere ogni attività e non riprendere da solo.
/// </summary>
public sealed record ConnectionSupersededDto(string Reason, long ConnectionEpoch, long CurrentEpoch);
