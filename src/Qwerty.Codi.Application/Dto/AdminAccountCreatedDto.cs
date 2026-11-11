namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Esito del bootstrap amministrativo: i riferimenti all'account creato. Non contiene la password
/// né la sua impronta, perché un esito non è il posto dove far transitare un segreto (§9.1).
/// </summary>
public sealed record AdminAccountCreatedDto(Guid AdminUserId, string Username);
