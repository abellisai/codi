using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Sessions;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Persistence;

/// <summary>
/// Conversione fra dominio ed entità di persistenza per la partecipazione (§7.4). Lo stato di
/// ammissione si memorizza come testo dell'enum: una riga con un testo diverso non è uno stato, e
/// leggerla come «ammesso» darebbe operatività a chi era stato escluso.
/// </summary>
public sealed class ParticipantEntityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Unit")]
    public void Mapping_DiUnaPartecipazioneAmmessa_PreservaIdentitaPostazioneEIngresso()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var stationId = Guid.NewGuid();

        // Act
        var row = SessionParticipantEntity.FromDomain(
            SessionParticipant.Create(sessionId, "  Luca  ", stationId, Now));
        var restored = row.ToDomain();

        // Assert
        Assert.Equal(sessionId, restored.SessionId);
        Assert.Equal(stationId, restored.StationId);
        Assert.Equal("Luca", restored.DisplayName);
        Assert.Equal(nameof(AdmissionStatus.Admitted), row.AdmissionStatusText);
        Assert.True(restored.IsAdmitted);
        Assert.Null(restored.ExcludedAtUtc);
        Assert.Equal(0, restored.ConnectionEpoch);
        Assert.Equal(Now.ToUnixTimeMilliseconds(), row.JoinedAtUtcMs);
        Assert.Equal(1, restored.RowVersion);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Mapping_DiUnaPartecipazioneSenzaPostazione_ConservaIlDatoMancante()
    {
        // La postazione è facoltativa: «PC non noto» resta un valore assente e non un identificativo
        // vuoto, perché un identificativo vuoto non è una riga di `station`.
        var row = SessionParticipantEntity.FromDomain(
            SessionParticipant.Create(Guid.NewGuid(), "Anna", stationId: null, Now));

        Assert.Null(row.StationId);
        Assert.Null(row.ToDomain().StationId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Mapping_DiUnaPartecipazioneEsclusa_PreservaStatoEIstante()
    {
        // Arrange
        var participant = SessionParticipant.Create(Guid.NewGuid(), "Marco", Guid.NewGuid(), Now);
        participant.Exclude(Now.AddMinutes(5));

        // Act
        var row = SessionParticipantEntity.FromDomain(participant);
        var restored = row.ToDomain();

        // Assert
        Assert.Equal(nameof(AdmissionStatus.Excluded), row.AdmissionStatusText);
        Assert.False(restored.IsAdmitted);
        Assert.Equal(Now.AddMinutes(5), restored.ExcludedAtUtc);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToDomain_ConStatoDiAmmissioneNonRiconosciuto_RifiutaLaRiga()
    {
        // Arrange: una riga scritta da uno strumento esterno con un testo che non è uno stato.
        var row = SessionParticipantEntity.FromDomain(
            SessionParticipant.Create(Guid.NewGuid(), "Luca", stationId: null, Now));
        row.AdmissionStatusText = "ammesso";

        // Act / Assert
        Assert.Equal(
            nameof(SessionParticipantEntity.AdmissionStatusText),
            Assert.Throws<ArgumentException>(() => row.ToDomain()).ParamName);
    }
}
