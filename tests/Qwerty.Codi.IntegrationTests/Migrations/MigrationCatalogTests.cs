using Qwerty.Codi.Infrastructure.Migrations;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Migrations;

/// <summary>
/// Catalogo delle migrazioni (§8.3): numerazione, unicità e impronta del contenuto. Sono test
/// unitari: non aprono database e non toccano il disco, quindi verificano soltanto le regole di
/// costruzione del catalogo.
/// </summary>
public sealed class MigrationCatalogTests
{
    private const string ProbeSql = "CREATE TABLE probe (id TEXT NOT NULL PRIMARY KEY);";

    [Fact]
    [Trait("Category", "Unit")]
    public void FromEmbeddedScripts_ConLeMigrazioniDistribuite_NumeraDaUnoSenzaBuchi()
    {
        var catalog = MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly);

        Assert.NotEmpty(catalog.Scripts);
        Assert.Equal(catalog.Scripts.Count, catalog.LatestVersion);

        for (var index = 0; index < catalog.Scripts.Count; index++)
        {
            var script = catalog.Scripts[index];
            Assert.Equal(index + 1, script.Version);
            Assert.Matches("^[a-z0-9_]+$", script.Name);
            Assert.Matches("^[0-9a-f]{64}$", script.Checksum);
            Assert.Null(catalog.Find(script.Version + catalog.Scripts.Count));
        }

        Assert.NotNull(catalog.Find(1));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ComputeChecksum_ConFineRigaDiversi_ProduceLaStessaImpronta()
    {
        var unixLineEndings = MigrationScript.ComputeChecksum("SELECT 1;\nSELECT 2;\n");
        var windowsLineEndings = MigrationScript.ComputeChecksum("SELECT 1;\r\nSELECT 2;\r\n");

        Assert.Equal(unixLineEndings, windowsLineEndings);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ComputeChecksum_ConContenutoDiverso_ProduceImprontaDiversa()
    {
        var original = MigrationScript.Create(1, "probe", ProbeSql);

        Assert.NotEqual(original.Checksum, MigrationScript.ComputeChecksum($"{ProbeSql}\nSELECT 1;"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Catalog_ConVersioneRipetuta_RifiutaIlCatalogo()
    {
        var exception = Assert.Throws<SchemaMigrationException>(() =>
            new MigrationCatalog(
            [
                MigrationScript.Create(1, "probe", ProbeSql),
                MigrationScript.Create(1, "probe_bis", ProbeSql),
            ]));

        Assert.Contains("Numerazione", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Catalog_ConNumerazioneConBuco_RifiutaIlCatalogo()
    {
        var exception = Assert.Throws<SchemaMigrationException>(() =>
            new MigrationCatalog(
            [
                MigrationScript.Create(1, "probe", ProbeSql),
                MigrationScript.Create(3, "probe_tre", ProbeSql),
            ]));

        Assert.Contains("versione 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Catalog_Vuoto_RifiutaIlCatalogo()
    {
        Assert.Throws<SchemaMigrationException>(() => new MigrationCatalog([]));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FromEmbeddedFileName_ConNomeSenzaNumero_RifiutaLoScript()
    {
        var exception = Assert.Throws<SchemaMigrationException>(() =>
            MigrationScript.FromEmbeddedFileName("admin_identity.sql", ProbeSql));

        Assert.Contains("NNNN_nome.sql", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FromEmbeddedFileName_ConNomeNumerato_DerivaVersioneENome()
    {
        var script = MigrationScript.FromEmbeddedFileName("0007_media_assets.sql", ProbeSql);

        Assert.Equal(7, script.Version);
        Assert.Equal("media_assets", script.Name);
    }
}
