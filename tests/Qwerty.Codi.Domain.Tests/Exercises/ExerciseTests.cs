using Qwerty.Codi.Domain.Exercises;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Exercises;

/// <summary>
/// Invarianti dell'esercizio (§7.2): titolo obbligatorio e utilizzabile, descrizione facoltativa
/// normalizzata a stringa vuota, archiviazione registrata una volta sola.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ExerciseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ConTitoloESenzaDescrizione_RipulisceEAccettaLaDescrizioneAssente()
    {
        var exercise = Exercise.Create("GridPath", "  Ciclo con i blocchi  ", null, Now);

        Assert.NotEqual(Guid.Empty, exercise.Id);
        Assert.Equal("Ciclo con i blocchi", exercise.Title);
        Assert.Equal(string.Empty, exercise.Description);
        Assert.False(exercise.IsArchived);
        Assert.Equal(Now, exercise.CreatedAtUtc);
        Assert.Equal(Now, exercise.UpdatedAtUtc);
        Assert.Equal(1, exercise.RowVersion);
    }

    [Fact]
    public void Create_ConDescrizioneDiSoliSpazi_LaTrattaComeAssente()
    {
        var exercise = Exercise.Create("GridPath", "Ciclo con i blocchi", "   ", Now);

        Assert.Equal(string.Empty, exercise.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ConTitoloInutilizzabile_Rifiuta(string title)
    {
        var exception = Assert.Throws<ArgumentException>(() => Exercise.Create("GridPath", title, null, Now));

        Assert.Equal("title", exception.ParamName);
    }

    [Fact]
    public void Create_ConTitoloTroppoLungo_Rifiuta()
    {
        var tooLong = new string('a', Exercise.MaxTitleLength + 1);

        var exception = Assert.Throws<ArgumentException>(() => Exercise.Create("GridPath", tooLong, null, Now));

        Assert.Equal("title", exception.ParamName);
    }

    [Fact]
    public void Create_ConDescrizioneTroppoLunga_Rifiuta()
    {
        var tooLong = new string('a', Exercise.MaxDescriptionLength + 1);

        var exception = Assert.Throws<ArgumentException>(
            () => Exercise.Create("GridPath", "Ciclo con i blocchi", tooLong, Now));

        Assert.Equal("description", exception.ParamName);
    }

    [Fact]
    public void Create_ConIstanteNonUtc_Rifiuta()
    {
        var local = new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.FromHours(2));

        var exception = Assert.Throws<ArgumentException>(
            () => Exercise.Create("GridPath", "Ciclo con i blocchi", null, local));

        Assert.Equal("nowUtc", exception.ParamName);
    }

    [Fact]
    public void Update_ConTitoloDiverso_CambiaIlTitoloEAggiornaLaModifica()
    {
        var exercise = Exercise.Create("GridPath", "Ciclo con i blocchi", "Prima", Now);
        var later = Now.AddMinutes(10);

        Assert.True(exercise.Update("Ripetizione", "Seconda", later));

        Assert.Equal("Ripetizione", exercise.Title);
        Assert.Equal("Seconda", exercise.Description);
        Assert.Equal(later, exercise.UpdatedAtUtc);
        Assert.Equal(Now, exercise.CreatedAtUtc);
    }

    [Theory]
    [InlineData("Ciclo con i blocchi", null)]
    [InlineData("  Ciclo con i blocchi  ", "")]
    [InlineData("Ciclo con i blocchi", "   ")]
    public void Update_ConGliStessiValori_NonEUnaModifica(string title, string? description)
    {
        var exercise = Exercise.Create("GridPath", "Ciclo con i blocchi", null, Now);

        Assert.False(exercise.Update(title, description, Now.AddMinutes(5)));
        Assert.Equal(Now, exercise.UpdatedAtUtc);
    }

    [Fact]
    public void Archive_PrimaVolta_CambiaStatoESecondaVolta_NonEUnErrore()
    {
        var exercise = Exercise.Create("GridPath", "Ciclo con i blocchi", null, Now);
        var archived = Now.AddMinutes(1);

        Assert.True(exercise.Archive(archived));
        Assert.True(exercise.IsArchived);
        Assert.False(exercise.Archive(Now.AddMinutes(2)));
        Assert.Equal(archived, exercise.UpdatedAtUtc);
    }

    [Fact]
    public void Restore_ConTitoloNonUtilizzabile_RifiutaLaRicostruzione()
    {
        var exception = Assert.Throws<ArgumentException>(() => Exercise.Restore(
            Guid.NewGuid(), "GridPath", "   ", string.Empty, isArchived: false, Now, Now, rowVersion: 1));

        Assert.Equal("title", exception.ParamName);
    }

    [Fact]
    public void Restore_ConVersioneNonPositiva_RifiutaLaRicostruzione()
    {
        var exception = Assert.Throws<ArgumentException>(() => Exercise.Restore(
            Guid.NewGuid(), "GridPath", "Ciclo con i blocchi", string.Empty, isArchived: false, Now, Now, rowVersion: 0));

        Assert.Equal("rowVersion", exception.ParamName);
    }

    [Fact]
    public void WithRowVersion_ConVersionePositiva_CopiaSenzaToccareIlResto()
    {
        var exercise = Exercise.Create("GridPath", "Ciclo con i blocchi", "Descrizione", Now);

        var copy = exercise.WithRowVersion(3);

        Assert.Equal(3, copy.RowVersion);
        Assert.Equal(1, exercise.RowVersion);
        Assert.Equal(exercise.Id, copy.Id);
        Assert.Equal(exercise.Description, copy.Description);
    }
}
