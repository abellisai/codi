using System.Reflection;
using System.Text;

namespace Qwerty.Codi.Infrastructure.Migrations;

/// <summary>
/// Insieme ordinato delle migrazioni disponibili in questa versione dell'applicazione (§8.3).
/// La numerazione è la sequenza di applicazione: deve partire da 1 e non avere buchi, perché un
/// numero mancante significa uno script non distribuito, non una versione da saltare.
/// </summary>
public sealed class MigrationCatalog
{
    private readonly IReadOnlyDictionary<int, MigrationScript> _byVersion;

    /// <summary>
    /// Costruisce il catalogo da script già caricati e ne verifica numerazione e unicità. I test
    /// usano questo costruttore per esercitare buchi, duplicati e impronte divergenti.
    /// </summary>
    public MigrationCatalog(IEnumerable<MigrationScript> scripts)
    {
        ArgumentNullException.ThrowIfNull(scripts);

        var ordered = scripts.OrderBy(script => script.Version).ToList();
        if (ordered.Count == 0)
        {
            throw new SchemaMigrationException("Il catalogo delle migrazioni è vuoto.");
        }

        for (var index = 0; index < ordered.Count; index++)
        {
            var expected = index + 1;
            if (ordered[index].Version != expected)
            {
                throw new SchemaMigrationException(
                    $"Numerazione delle migrazioni non continua: attesa la versione {expected}, trovata {ordered[index].Version}.");
            }
        }

        Scripts = ordered;
        _byVersion = ordered.ToDictionary(script => script.Version);
    }

    /// <summary>Script in ordine di versione, senza buchi e con nome univoco per costruzione.</summary>
    public IReadOnlyList<MigrationScript> Scripts { get; }

    /// <summary>Versione più alta conosciuta da questa versione dell'applicazione.</summary>
    public int LatestVersion => Scripts[^1].Version;

    /// <summary>Script della versione richiesta, oppure <see langword="null"/> se sconosciuta.</summary>
    public MigrationScript? Find(int version) => _byVersion.GetValueOrDefault(version);

    /// <summary>
    /// Legge gli script incorporati nell'assembly. Il prefisso delle risorse segue il namespace
    /// e la cartella, non il nome dell'assembly: così lo spazio dei nomi resta l'unico criterio.
    /// </summary>
    public static MigrationCatalog FromEmbeddedScripts(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var prefix = $"{typeof(MigrationCatalog).Namespace}.Scripts.";
        var scripts = new List<MigrationScript>();

        foreach (var resourceName in assembly
            .GetManifestResourceNames()
            .Where(name =>
                name.StartsWith(prefix, StringComparison.Ordinal) &&
                name.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal))
        {
            var fileName = resourceName[prefix.Length..];

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new SchemaMigrationException($"Risorsa incorporata non leggibile: {resourceName}.");
            using var reader = new StreamReader(stream, Encoding.UTF8);

            scripts.Add(MigrationScript.FromEmbeddedFileName(fileName, reader.ReadToEnd()));
        }

        if (scripts.Count == 0)
        {
            throw new SchemaMigrationException(
                $"Nessuno script di migrazione trovato fra le risorse incorporate con prefisso '{prefix}'.");
        }

        return new MigrationCatalog(scripts);
    }
}
