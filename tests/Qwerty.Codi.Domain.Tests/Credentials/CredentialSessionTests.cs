using Qwerty.Codi.Domain.Credentials;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Credentials;

/// <summary>
/// Invarianti della credenziale persistita (§7.2, §9.1): coerenza fra tipo e soggetto, scadenza
/// assoluta, revoca registrata una volta sola e marca che lega il ticket alla riga. Sono test di
/// sola logica: nessun database, nessun file.
/// </summary>
public sealed class CredentialSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    [Fact]
    [Trait("Category", "Unit")]
    public void IssueForAdmin_ConAmministratoreValido_EmetteCredenzialeUtilizzabile()
    {
        var adminUserId = Guid.NewGuid();

        var session = CredentialSession.IssueForAdmin(adminUserId, Now, Lifetime);

        Assert.Equal(CredentialPrincipalKind.Admin, session.PrincipalKind);
        Assert.Equal(adminUserId, session.AdminUserId);
        Assert.Null(session.ParticipantId);
        Assert.Equal(Now, session.IssuedAtUtc);
        Assert.Equal(Now + Lifetime, session.ExpiresAtUtc);
        Assert.Null(session.RevokedAtUtc);
        Assert.Equal(1, session.RowVersion);
        Assert.False(string.IsNullOrWhiteSpace(session.SecurityStamp));
        Assert.True(session.IsUsableAt(Now, session.SecurityStamp));
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(true, 8, 0)]
    [InlineData(false, 0, 0)]
    [InlineData(false, -1, 0)]
    [InlineData(false, 8, 2)]
    public void IssueForAdmin_ConSoggettoDurataOIstanteNonValidi_Rifiuta(
        bool emptyAdminId,
        int lifetimeHours,
        int offsetHours)
    {
        var adminUserId = emptyAdminId ? Guid.Empty : Guid.NewGuid();
        var lifetime = TimeSpan.FromHours(lifetimeHours);
        var now = new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.FromHours(offsetHours));

        Assert.Throws<ArgumentException>(() => CredentialSession.IssueForAdmin(adminUserId, now, lifetime));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsUsableAt_ConMarcaDiversa_NonEUtilizzabile()
    {
        var session = CredentialSession.IssueForAdmin(Guid.NewGuid(), Now, Lifetime);

        Assert.False(session.IsUsableAt(Now, "marca-sostituita"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsUsableAt_AllaScadenza_NonEUtilizzabile()
    {
        var session = CredentialSession.IssueForAdmin(Guid.NewGuid(), Now, Lifetime);

        Assert.True(session.IsUsableAt(Now + Lifetime - TimeSpan.FromMinutes(1), session.SecurityStamp));
        Assert.False(session.IsUsableAt(Now + Lifetime, session.SecurityStamp));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsUsableAt_DopoLaRevoca_NonEUtilizzabile()
    {
        var session = CredentialSession.IssueForAdmin(Guid.NewGuid(), Now, Lifetime);

        Assert.True(session.Revoke(Now.AddMinutes(5)));

        Assert.Equal(Now.AddMinutes(5), session.RevokedAtUtc);
        Assert.False(session.IsUsableAt(Now.AddMinutes(6), session.SecurityStamp));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Revoke_Ripetuta_RegistraUnaVoltaSola()
    {
        var session = CredentialSession.IssueForAdmin(Guid.NewGuid(), Now, Lifetime);
        session.Revoke(Now.AddMinutes(5));

        var secondRevocation = session.Revoke(Now.AddMinutes(10));

        Assert.False(secondRevocation);
        Assert.Equal(Now.AddMinutes(5), session.RevokedAtUtc);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(CredentialPrincipalKind.Admin, true)]
    [InlineData(CredentialPrincipalKind.Participant, false)]
    public void Restore_ConTipoEIdentitaIncoerenti_Rifiuta(CredentialPrincipalKind kind, bool withBothIdentities)
    {
        Assert.Throws<ArgumentException>(() => CredentialSession.Restore(
            Guid.NewGuid(),
            kind,
            adminUserId: withBothIdentities ? Guid.NewGuid() : null,
            participantId: withBothIdentities ? Guid.NewGuid() : null,
            "stamp",
            Now,
            Now + Lifetime,
            revokedAtUtc: null,
            rowVersion: 1));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Restore_ConScadenzaNonSuccessivaAllEmissione_Rifiuta()
    {
        Assert.Throws<ArgumentException>(() => CredentialSession.Restore(
            Guid.NewGuid(),
            CredentialPrincipalKind.Admin,
            Guid.NewGuid(),
            participantId: null,
            "stamp",
            Now,
            Now,
            revokedAtUtc: null,
            rowVersion: 1));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithRowVersion_ConVersioneNonPositiva_Rifiuta()
    {
        var session = CredentialSession.IssueForAdmin(Guid.NewGuid(), Now, Lifetime);

        Assert.Throws<ArgumentException>(() => session.WithRowVersion(0));
        Assert.Equal(4, session.WithRowVersion(4).RowVersion);
        Assert.Equal(session.SecurityStamp, session.WithRowVersion(4).SecurityStamp);
    }
}
