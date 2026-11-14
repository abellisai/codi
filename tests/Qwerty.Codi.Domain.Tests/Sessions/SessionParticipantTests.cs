using Qwerty.Codi.Domain.Sessions;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Sessions;

/// <summary>
/// Partecipazione di un alunno alla sessione (§7.4): chi entra è ammesso e senza epoca acquisita,
/// il nome è ripulito dagli spazi ai bordi, l'esclusione registra il proprio istante e ripeterla non
/// cambia nulla. La coerenza fra ammissione e istante di esclusione è un invariante del dominio
/// oltre che un CHECK del database (§7.4).
/// </summary>
public sealed class SessionParticipantTests
{
    private static readonly Guid SessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StationId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DateTimeOffset Now = new(2026, 10, 2, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Unit")]
    public void Leave_ConPartecipanteAmmesso_RegistraUscitaRipetibileSenzaEsclusione()
    {
        var participant = SessionParticipant.Create(SessionId, "Luca", null, Now);
        participant.Leave(Now.AddMinutes(1));
        participant.Leave(Now.AddMinutes(2));
        Assert.False(participant.IsAdmitted);
        Assert.Equal(Now.AddMinutes(1), participant.LeftAtUtc);
        Assert.Null(participant.ExcludedAtUtc);
        Assert.Equal(participant.LeftAtUtc, participant.WithRowVersion(2).LeftAtUtc);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Leave_PrimaDellIngresso_RifiutaSenzaCambiareStato()
    {
        var participant = SessionParticipant.Create(SessionId, "Luca", null, Now);
        Assert.Throws<ArgumentException>(() => participant.Leave(Now.AddSeconds(-1)));
        Assert.True(participant.IsAdmitted);
        Assert.Null(participant.LeftAtUtc);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Create_ConDatiValidi_CreaAmmessoSenzaEpocaEConIlNomeRipulito()
    {
        // Act
        var participant = SessionParticipant.Create(SessionId, "  Luca  ", StationId, Now);

        // Assert
        Assert.NotEqual(Guid.Empty, participant.Id);
        Assert.Equal(SessionId, participant.SessionId);
        Assert.Equal(StationId, participant.StationId);
        Assert.Equal("Luca", participant.DisplayName);
        Assert.Equal(AdmissionStatus.Admitted, participant.AdmissionStatus);
        Assert.True(participant.IsAdmitted);
        Assert.Equal(0, participant.ConnectionEpoch);
        Assert.Equal(Now, participant.JoinedAtUtc);
        Assert.Null(participant.ExcludedAtUtc);
        Assert.Equal(Now, participant.CreatedAtUtc);
        Assert.Equal(Now, participant.UpdatedAtUtc);
        Assert.Equal(1, participant.RowVersion);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Create_SenzaPostazioneNota_AccettaLaPartecipazione()
    {
        // Arrange: la postazione si può associare dopo (§9.5), quindi non è obbligatoria all'ingresso.
        var participant = SessionParticipant.Create(SessionId, "Luca", stationId: null, Now);

        // Act / Assert
        Assert.Null(participant.StationId);
        Assert.Equal("Luca", participant.DisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Lu\tca")]
    [Trait("Category", "Unit")]
    public void Create_ConNomeNonUtilizzabile_Rifiuta(string displayName)
    {
        Assert.Throws<ArgumentException>(() => SessionParticipant.Create(SessionId, displayName, null, Now));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Create_ConNomeOltreIlLimite_Rifiuta()
    {
        var tooLong = new string('a', ParticipantName.MaxGraphemes + 1);

        var error = Assert.Throws<ArgumentException>(
            () => SessionParticipant.Create(SessionId, tooLong, null, Now));

        Assert.Equal("displayName", error.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Create_SenzaSessione_Rifiuta()
    {
        var error = Assert.Throws<ArgumentException>(
            () => SessionParticipant.Create(Guid.Empty, "Luca", null, Now));

        Assert.Equal("sessionId", error.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Create_ConIstanteNonUtc_Rifiuta()
    {
        var local = new DateTimeOffset(2026, 10, 2, 10, 30, 0, TimeSpan.FromHours(2));

        var error = Assert.Throws<ArgumentException>(
            () => SessionParticipant.Create(SessionId, "Luca", null, local));

        Assert.Equal("nowUtc", error.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Exclude_ConPartecipazioneAmmessa_RegistraStatoIstanteEVersione()
    {
        // Arrange
        var participant = SessionParticipant.Create(SessionId, "Luca", null, Now);
        var later = Now.AddMinutes(5);

        // Act
        participant.Exclude(later);

        // Assert
        Assert.Equal(AdmissionStatus.Excluded, participant.AdmissionStatus);
        Assert.False(participant.IsAdmitted);
        Assert.Equal(later, participant.ExcludedAtUtc);
        Assert.Equal(later, participant.UpdatedAtUtc);
        // L'epoca resta quella acquisita: l'esclusione non riavvia la connessione, la toglie.
        Assert.Equal(0, participant.ConnectionEpoch);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Exclude_Ripetuta_ConservaIlPrimoIstante()
    {
        // Arrange: come l'archiviazione ripetuta, l'esclusione ripetuta non è un secondo evento.
        var participant = SessionParticipant.Create(SessionId, "Luca", null, Now);
        participant.Exclude(Now.AddMinutes(5));

        // Act
        participant.Exclude(Now.AddMinutes(10));

        // Assert
        Assert.Equal(Now.AddMinutes(5), participant.ExcludedAtUtc);
        Assert.Equal(Now.AddMinutes(5), participant.UpdatedAtUtc);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Restore_ConDatiPersistiti_ConservaStatoEdEpoca()
    {
        // Act
        var participant = SessionParticipant.Restore(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            SessionId,
            StationId,
            "Anna Maria",
            AdmissionStatus.Admitted,
            connectionEpoch: 2,
            Now,
            excludedAtUtc: null,
            Now,
            Now.AddSeconds(30),
            rowVersion: 3);

        // Assert
        Assert.Equal("Anna Maria", participant.DisplayName);
        Assert.Equal(2, participant.ConnectionEpoch);
        Assert.Equal(3, participant.RowVersion);
        Assert.Equal(Now.AddSeconds(30), participant.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(AdmissionStatus.Excluded, false)]
    [InlineData(AdmissionStatus.Admitted, true)]
    [Trait("Category", "Unit")]
    public void Restore_ConAmmissioneEIstanteIncoerenti_Rifiuta(AdmissionStatus status, bool hasExcludedAt)
    {
        // Arrange: solo una partecipazione esclusa porta l'istante, e una esclusa ce l'ha sempre.
        DateTimeOffset? excludedAt = hasExcludedAt ? Now.AddMinutes(1) : null;

        // Act
        var error = Assert.Throws<ArgumentException>(() => SessionParticipant.Restore(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            SessionId,
            null,
            "Luca",
            status,
            connectionEpoch: 0,
            Now,
            excludedAt,
            Now,
            Now,
            rowVersion: 1));

        // Assert
        Assert.Equal("excludedAtUtc", error.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Restore_ConAmmissioneNonRiconosciuta_Rifiuta()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SessionParticipant.Restore(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            SessionId,
            null,
            "Luca",
            (AdmissionStatus)7,
            connectionEpoch: 0,
            Now,
            excludedAtUtc: null,
            Now,
            Now,
            rowVersion: 1));
    }

    [Theory]
    [InlineData(-1, 1, "connectionEpoch")]
    [InlineData(0, 0, "rowVersion")]
    [Trait("Category", "Unit")]
    public void Restore_ConContatoriFuoriIntervallo_Rifiuta(
        long connectionEpoch,
        long rowVersion,
        string expectedParameter)
    {
        var error = Assert.Throws<ArgumentException>(() => SessionParticipant.Restore(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            SessionId,
            null,
            "Luca",
            AdmissionStatus.Admitted,
            connectionEpoch,
            Now,
            excludedAtUtc: null,
            Now,
            Now,
            rowVersion));

        Assert.Equal(expectedParameter, error.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Restore_ConIngressoPrimaDellaCreazione_Rifiuta()
    {
        var error = Assert.Throws<ArgumentException>(() => SessionParticipant.Restore(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            SessionId,
            null,
            "Luca",
            AdmissionStatus.Admitted,
            connectionEpoch: 0,
            Now.AddSeconds(-1),
            excludedAtUtc: null,
            Now,
            Now,
            rowVersion: 1));

        Assert.Equal("joinedAtUtc", error.ParamName);
    }
}
