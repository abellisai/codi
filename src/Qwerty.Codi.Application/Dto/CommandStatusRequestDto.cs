namespace Qwerty.Codi.Application.Dto;

/// <summary>Corpo della query di stato a cui un comando accettato rimanda (§10.6).</summary>
public sealed record CommandStatusRequestDto(Guid SessionId, Guid CommandId);
