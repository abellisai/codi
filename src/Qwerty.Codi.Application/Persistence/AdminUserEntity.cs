using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Domain.Admin;

namespace Qwerty.Codi.Application.Persistence;

/// <summary>
/// Entità di persistenza di admin_user. Il mapping SQL vive in Infrastructure, le invarianti
/// restano nel dominio. UUID canonici, flag interi e date epoch millisecondi seguono §7.1.
/// </summary>
public sealed class AdminUserEntity : IPersistedEntity<string>
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string NormalizedUsername { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string SecurityStamp { get; set; } = string.Empty;
    public long IsActiveFlag { get; set; }
    public long FailedLoginAttempts { get; set; }
    public long? LockedUntilUtcMs { get; set; }
    public long CreatedAtUtcMs { get; set; }
    public long UpdatedAtUtcMs { get; set; }
    public long RowVersion { get; set; }

    public static AdminUserEntity FromDomain(AdminUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new AdminUserEntity
        {
            Id = user.Id.ToString("D"),
            Username = user.Username,
            NormalizedUsername = user.NormalizedUsername,
            PasswordHash = user.PasswordHash,
            SecurityStamp = user.SecurityStamp,
            IsActiveFlag = user.IsActive ? 1 : 0,
            FailedLoginAttempts = user.FailedLoginAttempts,
            LockedUntilUtcMs = user.LockedUntilUtc?.ToUnixTimeMilliseconds(),
            CreatedAtUtcMs = user.CreatedAtUtc.ToUnixTimeMilliseconds(),
            UpdatedAtUtcMs = user.UpdatedAtUtc.ToUnixTimeMilliseconds(),
            RowVersion = user.RowVersion,
        };
    }

    /// <summary>Ricostruisce e valida anche dati scritti da strumenti esterni.</summary>
    public AdminUser ToDomain() => AdminUser.Restore(
        Guid.Parse(Id), Username, NormalizedUsername, PasswordHash, SecurityStamp,
        IsActiveFlag == 1, checked((int)FailedLoginAttempts),
        LockedUntilUtcMs is { } locked ? DateTimeOffset.FromUnixTimeMilliseconds(locked) : null,
        DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtUtcMs),
        DateTimeOffset.FromUnixTimeMilliseconds(UpdatedAtUtcMs), RowVersion);
}
