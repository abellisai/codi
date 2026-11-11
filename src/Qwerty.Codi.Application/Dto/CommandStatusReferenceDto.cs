namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Riferimento alla query che dà lo stato puntuale delle consegne (§10.6): il comando accettato non
/// porta le conferme, che arrivano dopo e si interrogano quando servono.
/// </summary>
public sealed record CommandStatusReferenceDto(string Name, CommandStatusRequestDto Payload);
