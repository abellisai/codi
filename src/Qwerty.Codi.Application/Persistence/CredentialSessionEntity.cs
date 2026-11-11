using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Domain.Credentials;

namespace Qwerty.Codi.Application.Persistence;

/// <summary>Riga di credential_session, distinta dal ticket utilizzabile dal client.</summary>
public sealed class CredentialSessionEntity : IPersistedEntity<string>
{
    public string Id { get; set; } = string.Empty;
    public string PrincipalKind { get; set; } = string.Empty;
    public string? AdminUserId { get; set; }
    public string? ParticipantId { get; set; }
    public string SecurityStamp { get; set; } = string.Empty;
    public long IssuedAtUtcMs { get; set; }
    public long ExpiresAtUtcMs { get; set; }
    public long? RevokedAtUtcMs { get; set; }
    public long CreatedAtUtcMs { get; set; }
    public long UpdatedAtUtcMs { get; set; }
    public long RowVersion { get; set; }

    public static CredentialSessionEntity FromDomain(CredentialSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var issued = session.IssuedAtUtc.ToUnixTimeMilliseconds();
        return new CredentialSessionEntity
        {
            Id = session.Id.ToString("D"),
            PrincipalKind = session.PrincipalKind.ToString(),
            AdminUserId = session.AdminUserId?.ToString("D"),
            ParticipantId = session.ParticipantId?.ToString("D"),
            SecurityStamp = session.SecurityStamp,
            IssuedAtUtcMs = issued,
            ExpiresAtUtcMs = session.ExpiresAtUtc.ToUnixTimeMilliseconds(),
            RevokedAtUtcMs = session.RevokedAtUtc?.ToUnixTimeMilliseconds(),
            CreatedAtUtcMs = issued,
            UpdatedAtUtcMs = session.RevokedAtUtc?.ToUnixTimeMilliseconds() ?? issued,
            RowVersion = session.RowVersion,
        };
    }

    public CredentialSession ToDomain()
    {
        if (!Enum.TryParse<CredentialPrincipalKind>(PrincipalKind, ignoreCase: false, out var kind))
            throw new ArgumentException($"Tipo di credenziale non riconosciuto: '{PrincipalKind}'.", nameof(PrincipalKind));

        return CredentialSession.Restore(
            Guid.Parse(Id), kind,
            AdminUserId is null ? null : Guid.Parse(AdminUserId),
            ParticipantId is null ? null : Guid.Parse(ParticipantId), SecurityStamp,
            DateTimeOffset.FromUnixTimeMilliseconds(IssuedAtUtcMs),
            DateTimeOffset.FromUnixTimeMilliseconds(ExpiresAtUtcMs),
            RevokedAtUtcMs is { } revoked ? DateTimeOffset.FromUnixTimeMilliseconds(revoked) : null,
            RowVersion);
    }
}
