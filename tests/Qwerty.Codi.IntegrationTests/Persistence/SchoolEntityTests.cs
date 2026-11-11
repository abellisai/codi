using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Organization;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Persistence;

/// <summary>
/// Conversione fra scuola di dominio ed entità di persistenza (§7.1): il formato persistito è
/// esplicito — UUID canonico, flag intero, date in millisecondi — e la ricostruzione valida.
/// </summary>
public sealed class SchoolEntityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "Unit")]
    public void Mapping_ConScuolaArchiviata_PreservaIdentitaFormatoEStato()
    {
        // Arrange
        var school = School.Create("Scuola Manzoni", Now);
        school.Archive(Now.AddMinutes(30));

        // Act
        var row = SchoolEntity.FromDomain(school);
        var restored = row.ToDomain();

        // Assert
        Assert.Equal(school.Id.ToString("D"), row.Id);
        Assert.Equal(1, row.IsArchivedFlag);
        Assert.Equal(Now.ToUnixTimeMilliseconds(), row.CreatedAtUtcMs);
        Assert.Equal(Now.AddMinutes(30).ToUnixTimeMilliseconds(), row.UpdatedAtUtcMs);

        Assert.Equal(school.Id, restored.Id);
        Assert.Equal(school.Name, restored.Name);
        Assert.Equal(school.NormalizedName, restored.NormalizedName);
        Assert.True(restored.IsArchived);
        Assert.Equal(Now, restored.CreatedAtUtc);
        Assert.Equal(Now.AddMinutes(30), restored.UpdatedAtUtc);
        Assert.Equal(1, restored.RowVersion);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToDomain_ConNomeNormalizzatoIncoerente_RifiutaLaRiga()
    {
        // Arrange: una riga scritta da uno strumento esterno non deve entrare nel dominio se
        // contraddice la regola di normalizzazione, anche quando il CHECK SQL la accetta.
        var row = SchoolEntity.FromDomain(School.Create("Scuola Manzoni", Now));
        row.NormalizedName = "manzoni";

        // Act
        var error = Assert.Throws<ArgumentException>(() => row.ToDomain());

        // Assert
        Assert.Equal("normalizedName", error.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToDomain_ConStatoArchiviatoFuoriIntervallo_RifiutaLaRiga()
    {
        // Arrange
        var row = SchoolEntity.FromDomain(School.Create("Scuola Manzoni", Now));
        row.IsArchivedFlag = 2;

        // Act: un flag diverso da 0 e 1 non è uno stato, e leggerlo come «non archiviata»
        // riaprirebbe in silenzio una scuola archiviata.
        var error = Assert.Throws<ArgumentException>(() => row.ToDomain());

        // Assert
        Assert.Equal(nameof(SchoolEntity.IsArchivedFlag), error.ParamName);
    }
}
