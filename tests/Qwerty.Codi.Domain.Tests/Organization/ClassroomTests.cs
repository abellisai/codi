using Qwerty.Codi.Domain.Organization;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Organization;

/// <summary>
/// Invarianti della classe (§7.2, §5.2): scuola obbligatoria e immutabile, nome normalizzato
/// insieme all'anno, archiviazione registrata una volta sola e ricostruzione che valida i dati.
/// </summary>
public sealed class ClassroomTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid SchoolId = Guid.Parse("3b3f1d7c-6c8f-4a1e-92b5-2e6f5a4d3c2b");

    [Fact]
    public void Create_ConDatiUtilizzabili_NormalizzaAnnoENome()
    {
        var classroom = Classroom.Create(SchoolId, "  2ª A  ", "2026-2027", Now);

        Assert.NotEqual(Guid.Empty, classroom.Id);
        Assert.Equal(SchoolId, classroom.SchoolId);
        Assert.Equal("2ª A", classroom.Name);
        Assert.Equal("2ª a", classroom.NormalizedName);
        Assert.Equal("2026/2027", classroom.SchoolYear);
        Assert.False(classroom.IsArchived);
        Assert.Equal(Now, classroom.CreatedAtUtc);
        Assert.Equal(Now, classroom.UpdatedAtUtc);
        Assert.Equal(1, classroom.RowVersion);
    }

    [Fact]
    public void Create_SenzaScuola_Rifiuta()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            Classroom.Create(Guid.Empty, "2ª A", "2026/2027", Now));

        Assert.Equal("schoolId", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ConNomeInutilizzabile_Rifiuta(string name)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            Classroom.Create(SchoolId, name, "2026/2027", Now));

        Assert.Equal("name", exception.ParamName);
    }

    [Theory]
    [InlineData("2026")]
    [InlineData("2026/2028")]
    public void Create_ConAnnoInutilizzabile_Rifiuta(string schoolYear)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            Classroom.Create(SchoolId, "2ª A", schoolYear, Now));

        Assert.Equal("schoolYear", exception.ParamName);
    }

    [Fact]
    public void Create_ConIstanteNonUtc_Rifiuta()
    {
        var local = new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.FromHours(2));

        var exception = Assert.Throws<ArgumentException>(() =>
            Classroom.Create(SchoolId, "2ª A", "2026/2027", local));

        Assert.Equal("nowUtc", exception.ParamName);
    }

    [Fact]
    public void Update_ConNomeEAnnoDiversi_CambiaEntrambiEAggiornaLaModifica()
    {
        var classroom = Classroom.Create(SchoolId, "2ª A", "2026/2027", Now);
        var later = Now.AddHours(1);

        var changed = classroom.Update("  2ª  B ", "2027-2028", later);

        Assert.True(changed);
        Assert.Equal("2ª  B", classroom.Name);
        Assert.Equal("2ª b", classroom.NormalizedName);
        Assert.Equal("2027/2028", classroom.SchoolYear);
        Assert.Equal(later, classroom.UpdatedAtUtc);
        Assert.Equal(Now, classroom.CreatedAtUtc);
    }

    [Theory]
    [InlineData("2ª A", "2026/2027")]
    [InlineData("  2ª A  ", "2026/2027")]
    [InlineData("2ª A", "2026-2027")]
    public void Update_ConGliStessiValori_NonEUnaModifica(string name, string schoolYear)
    {
        // Nome e anno si confrontano nella forma normalizzata: queste scritture non alzano la
        // versione di riga, perché il risultato è identico a prima.
        var classroom = Classroom.Create(SchoolId, "2ª A", "2026/2027", Now);

        var changed = classroom.Update(name, schoolYear, Now.AddMinutes(5));

        Assert.False(changed);
        Assert.Equal(Now, classroom.UpdatedAtUtc);
    }

    [Fact]
    public void Update_ConAnnoInutilizzabile_RifiutaSenzaModificare()
    {
        var classroom = Classroom.Create(SchoolId, "2ª A", "2026/2027", Now);

        Assert.Throws<ArgumentException>(() => classroom.Update("2ª A", "2026", Now.AddMinutes(1)));
        Assert.Equal("2026/2027", classroom.SchoolYear);
        Assert.Equal(Now, classroom.UpdatedAtUtc);
    }

    [Fact]
    public void Archive_PrimaVolta_CambiaStatoESecondaVolta_NonEUnErrore()
    {
        var classroom = Classroom.Create(SchoolId, "2ª A", "2026/2027", Now);
        var archived = Now.AddMinutes(2);

        Assert.True(classroom.Archive(archived));
        Assert.True(classroom.IsArchived);

        Assert.False(classroom.Archive(Now.AddMinutes(3)));
        Assert.Equal(archived, classroom.UpdatedAtUtc);
    }

    [Fact]
    public void Restore_ConAnnoNonCanonico_RifiutaLaRicostruzione()
    {
        // Il trattino è accettato in ingresso e ricondotto alla barra: una riga che lo conserva
        // non è stata scritta da questa applicazione e non deve entrare nel dominio.
        var exception = Assert.Throws<ArgumentException>(() => Classroom.Restore(
            Guid.NewGuid(), SchoolId, "2ª A", "2ª a", "2026-2027", isArchived: false, Now, Now, rowVersion: 1));

        Assert.Equal("schoolYear", exception.ParamName);
    }

    [Fact]
    public void Restore_ConNomeNormalizzatoIncoerente_RifiutaLaRicostruzione()
    {
        var exception = Assert.Throws<ArgumentException>(() => Classroom.Restore(
            Guid.NewGuid(), SchoolId, "2ª A", "seconda a", "2026/2027", isArchived: false, Now, Now, rowVersion: 1));

        Assert.Equal("normalizedName", exception.ParamName);
    }

    [Fact]
    public void Restore_SenzaScuola_RifiutaLaRicostruzione()
    {
        var exception = Assert.Throws<ArgumentException>(() => Classroom.Restore(
            Guid.NewGuid(), Guid.Empty, "2ª A", "2ª a", "2026/2027", isArchived: false, Now, Now, rowVersion: 1));

        Assert.Equal("schoolId", exception.ParamName);
    }

    [Fact]
    public void WithRowVersion_ConVersioneNonPositiva_Rifiuta()
    {
        var classroom = Classroom.Create(SchoolId, "2ª A", "2026/2027", Now);

        var exception = Assert.Throws<ArgumentException>(() => classroom.WithRowVersion(0));

        Assert.Equal("rowVersion", exception.ParamName);
    }
}
