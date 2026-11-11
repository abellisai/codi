using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Admin;
using Qwerty.Codi.Domain.Credentials;

namespace Qwerty.Codi.IntegrationTests;

/// <summary>Conversioni per gli assert dei test preesistenti; i filtri usano l'API generica reale.</summary>
internal static class PersistenceTestReads
{
    public static async Task<AdminUser?> ReadAdminAsync(
        this IAdminUserRepository users, string name, CancellationToken token) =>
        (await users.QueryAsync(entity => entity.NormalizedUsername == name, token)).SingleOrDefault()?.ToDomain();

    public static async Task<CredentialSession?> ReadCredentialAsync(
        this ICredentialSessionRepository sessions, Guid id, CancellationToken token) =>
        (await sessions.GetAsync(id.ToString("D"), token))?.ToDomain();

    public static Task<string> StoreAsync(
        this ICredentialSessionRepository sessions, CredentialSession session, CancellationToken token) =>
        sessions.InsertAsync(CredentialSessionEntity.FromDomain(session), token);
}
