namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Esito dell'accesso amministrativo. La marca di sicurezza è il valore che il ticket del cookie
/// deve portare con sé, così la revoca lo rende obsoleto (§9.1, §9.2): è un dato interno al
/// processo, e il trasporto non deve restituirlo al client così com'è.
/// </summary>
public sealed record AdminSignedInDto(
    Guid CredentialSessionId,
    Guid AdminUserId,
    string Username,
    DateTimeOffset ExpiresAtUtc,
    string SecurityStamp);
