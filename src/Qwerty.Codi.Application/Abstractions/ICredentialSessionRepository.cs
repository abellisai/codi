using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Credentials;

namespace Qwerty.Codi.Application.Abstractions;

public interface ICredentialSessionRepository : IRepository<CredentialSessionEntity, string>
{
    /// <summary>Revoca con controllo di versione e revoca precedente assente; null se non modificata.</summary>
    Task<CredentialSession?> TryRevokeAsync(CredentialSession session, CancellationToken cancellationToken);
}
