using Qwerty.Codi.Domain.Admin;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Admin;

/// <summary>
/// Invarianti dell'account amministrativo (§7.2, §9.1): il nome si normalizza una volta sola, la
/// password si conserva solo come impronta e un archivio alterato non deve produrre un'entità
/// coerente per sbaglio.
/// </summary>
public sealed class AdminUserTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private const string Hash = "AQAAAAIAAAA-impronta-di-prova";

    [Fact]
    [Trait("Category", "Unit")]
    public void Create_ConNomeDigitatoConSpazi_ConservaIlNomeEUsaIlNormalizzato()
    {
        var user = AdminUser.Create("  Maestra  ", Hash, Now);

        Assert.Equal("Maestra", user.Username);
        Assert.Equal("maestra", user.NormalizedUsername);
        Assert.Equal(Hash, user.PasswordHash);
        Assert.True(user.IsActive);
        Assert.Equal(Now, user.CreatedAtUtc);
        Assert.Equal(1, user.RowVersion);
        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.False(string.IsNullOrWhiteSpace(user.SecurityStamp));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Create_ConDueAccount_GeneraMarcheDiSicurezzaDiverse()
    {
        var first = AdminUser.Create("Maestra", Hash, Now);
        var second = AdminUser.Create("Maestra", Hash, Now);

        Assert.NotEqual(first.SecurityStamp, second.SecurityStamp);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Maestra\u0007")]
    public void Create_ConNomeNonUtilizzabile_Rifiuta(string username)
    {
        Assert.Throws<ArgumentException>(() => AdminUser.Create(username, Hash, Now));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Create_ConImprontaAssente_Rifiuta()
    {
        Assert.Throws<ArgumentException>(() => AdminUser.Create("Maestra", "   ", Now));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Create_ConIstanteNonUtc_Rifiuta()
    {
        var local = new DateTimeOffset(2026, 9, 30, 11, 0, 0, TimeSpan.FromHours(2));

        Assert.Throws<ArgumentException>(() => AdminUser.Create("Maestra", Hash, local));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Restore_ConDatiCoerenti_ConservaStatoEIndicatori()
    {
        var id = Guid.NewGuid();
        var createdAt = Now.AddDays(-3);

        var user = AdminUser.Restore(
            id,
            "Maestra",
            "maestra",
            Hash,
            "stamp",
            isActive: false,
            failedLoginAttempts: 3,
            lockedUntilUtc: createdAt.AddMinutes(5),
            createdAtUtc: createdAt,
            updatedAtUtc: createdAt.AddMinutes(1),
            rowVersion: 7);

        Assert.Equal(id, user.Id);
        Assert.False(user.IsActive);
        Assert.Equal(createdAt, user.CreatedAtUtc);
        Assert.Equal(3, user.FailedLoginAttempts);
        Assert.Equal(createdAt.AddMinutes(5), user.LockedUntilUtc);
        Assert.Equal(7, user.RowVersion);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Restore_ConNomeNormalizzatoIncoerente_Rifiuta()
    {
        Assert.Throws<ArgumentException>(() => AdminUser.Restore(
            Guid.NewGuid(),
            "Maestra",
            "maestro",
            Hash,
            "stamp",
            isActive: true,
            failedLoginAttempts: 0,
            lockedUntilUtc: null,
            createdAtUtc: Now,
            updatedAtUtc: Now,
            rowVersion: 1));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Restore_ConVersioneDiRigaNonPositiva_Rifiuta()
    {
        Assert.Throws<ArgumentException>(() => AdminUser.Restore(
            Guid.NewGuid(),
            "Maestra",
            "maestra",
            Hash,
            "stamp",
            isActive: true,
            failedLoginAttempts: 0,
            lockedUntilUtc: null,
            createdAtUtc: Now,
            updatedAtUtc: Now,
            rowVersion: 0));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RegisterFailedAttempt_FinoAllaSoglia_NonBloccaEAllaSogliaBlocca()
    {
        var user = AdminUser.Create("Maestra", Hash, Now);

        for (var attempt = 1; attempt < AdminUser.MaxFailedAttempts; attempt++)
        {
            Assert.False(user.RegisterFailedAttempt(Now));
        }

        Assert.False(user.IsLockedAt(Now));
        Assert.Equal(AdminUser.MaxFailedAttempts - 1, user.FailedLoginAttempts);

        Assert.True(user.RegisterFailedAttempt(Now));

        Assert.True(user.IsLockedAt(Now));
        Assert.Equal(Now + AdminUser.LockDuration, user.LockedUntilUtc);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RegisterFailedAttempt_DuranteIlBlocco_NonProlungaLaScadenza()
    {
        var user = AdminUser.Create("Maestra", Hash, Now);
        Lock(user, Now);

        var extended = user.RegisterFailedAttempt(Now.AddMinutes(5));

        Assert.False(extended);
        Assert.Equal(Now + AdminUser.LockDuration, user.LockedUntilUtc);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RegisterFailedAttempt_DopoLaScadenza_BloccaDiNuovo()
    {
        var user = AdminUser.Create("Maestra", Hash, Now);
        Lock(user, Now);
        var afterLock = Now + AdminUser.LockDuration;

        var lockedAgain = user.RegisterFailedAttempt(afterLock);

        Assert.True(lockedAgain);
        Assert.Equal(afterLock + AdminUser.LockDuration, user.LockedUntilUtc);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsLockedAt_ScadutoIlBlocco_NonBloccaPiu()
    {
        var user = AdminUser.Create("Maestra", Hash, Now);
        Lock(user, Now);

        Assert.False(user.IsLockedAt(Now + AdminUser.LockDuration));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RegisterSuccessfulAccess_AzzeraTentativiEBlocco()
    {
        var user = AdminUser.Create("Maestra", Hash, Now);
        Lock(user, Now);
        var later = Now.AddMinutes(30);

        user.RegisterSuccessfulAccess(later);

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntilUtc);
        Assert.Equal(later, user.UpdatedAtUtc);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithRowVersion_ConVersioneNonPositiva_Rifiuta()
    {
        var user = AdminUser.Create("Maestra", Hash, Now);

        Assert.Throws<ArgumentException>(() => user.WithRowVersion(0));
        Assert.Equal(9, user.WithRowVersion(9).RowVersion);
    }

    private static void Lock(AdminUser user, DateTimeOffset nowUtc)
    {
        for (var attempt = 0; attempt < AdminUser.MaxFailedAttempts; attempt++)
        {
            user.RegisterFailedAttempt(nowUtc);
        }
    }
}
