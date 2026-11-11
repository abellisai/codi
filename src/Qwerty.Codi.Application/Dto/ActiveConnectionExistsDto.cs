namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Un'altra pagina dello stesso partecipante è già operativa (§12.5): il client resta in attesa
/// senza poter modificare nulla, e ritenta dopo l'attesa indicata.
/// </summary>
public sealed record ActiveConnectionExistsDto(string Reason, long ConnectionEpoch, long RetryAfterSeconds);
