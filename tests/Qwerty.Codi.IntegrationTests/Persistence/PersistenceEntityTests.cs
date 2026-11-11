using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Admin;
using Qwerty.Codi.Domain.Credentials;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Persistence;

public sealed class PersistenceEntityTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void ToDomain_ConTipoCredenzialeSconosciuto_RifiutaLaRiga()
    {
        // Arrange: un dato di persistenza è un ingresso da validare, anche senza il CHECK SQL.
        var row = CredentialSessionEntity.FromDomain(CredentialSession.IssueForAdmin(
            Guid.NewGuid(), TestTimeProvider.Instant, TimeSpan.FromHours(8)));
        row.PrincipalKind = "Sconosciuto";

        // Act
        var error = Assert.Throws<ArgumentException>(() => row.ToDomain());

        // Assert
        Assert.Equal(nameof(CredentialSessionEntity.PrincipalKind), error.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Mapping_ConCredenzialePartecipanteRevocata_PreservaIdentitaDateENull()
    {
        // Arrange: la conversione è disponibile già nel dominio; non implementa il join T3.
        var participant = Guid.NewGuid();
        var issued = TestTimeProvider.Instant;
        var revoked = issued.AddMinutes(10);
        var original = CredentialSession.Restore(Guid.NewGuid(), CredentialPrincipalKind.Participant,
            adminUserId: null, participant, "stamp", issued, issued.AddHours(8), revoked, rowVersion: 3);

        // Act
        var row = CredentialSessionEntity.FromDomain(original);
        var restored = row.ToDomain();

        // Assert
        Assert.Equal(original.Id, restored.Id);
        Assert.Null(restored.AdminUserId);
        Assert.Equal(participant, restored.ParticipantId);
        Assert.Equal(CredentialPrincipalKind.Participant, restored.PrincipalKind);
        Assert.Equal(issued, restored.IssuedAtUtc);
        Assert.Equal(issued.AddHours(8), restored.ExpiresAtUtc);
        Assert.Equal(revoked, restored.RevokedAtUtc);
        Assert.Equal(issued.ToUnixTimeMilliseconds(), row.CreatedAtUtcMs);
        Assert.Equal(revoked.ToUnixTimeMilliseconds(), row.UpdatedAtUtcMs);
        Assert.Equal(3, restored.RowVersion);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToDomain_ConConteggioFuoriIntervalloInt_NonTroncaSilenziosamenteIlDato()
    {
        // Arrange
        var row = AdminUserEntity.FromDomain(AdminUser.Create("Maestra", "hash", TestTimeProvider.Instant));
        row.FailedLoginAttempts = long.MaxValue;

        // Act / Assert: un conteggio corrotto non può trasformarsi in un account sbloccato.
        Assert.Throws<OverflowException>(() => row.ToDomain());
    }
}
