using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Qwerty.Codi.Infrastructure.Migrations;

/// <summary>
/// Migrazione SQL numerata (§7.5, §8.3): versione, nome descrittivo, script e impronta del
/// contenuto. L'impronta si calcola dallo script invece di dichiararla a mano, così la modifica
/// di una migrazione già applicata viene rilevata e non resta silenziosa.
/// </summary>
public sealed record MigrationScript
{
    private static readonly Regex EmbeddedFileNamePattern = new(
        @"^(?<version>\d{4})_(?<name>[a-z0-9_]+)\.sql$",
        RegexOptions.CultureInvariant);

    private static readonly Regex NamePattern = new(
        @"^[a-z0-9_]+$",
        RegexOptions.CultureInvariant);

    private MigrationScript(int version, string name, string sql, string checksum)
    {
        Version = version;
        Name = name;
        Sql = sql;
        Checksum = checksum;
    }

    /// <summary>Numero progressivo della migrazione, a partire da 1.</summary>
    public int Version { get; }

    /// <summary>Nome descrittivo senza numero, come viene registrato in <c>schema_migration</c>.</summary>
    public string Name { get; }

    /// <summary>Istruzioni SQL eseguite in una sola transazione.</summary>
    public string Sql { get; }

    /// <summary>Impronta SHA-256 esadecimale minuscola dello script normalizzato.</summary>
    public string Checksum { get; }

    /// <summary>
    /// Costruisce la migrazione da versione, nome e script. È il punto di ingresso dei test e
    /// dei cataloghi costruiti a mano, oltre che della lettura delle risorse incorporate.
    /// </summary>
    public static MigrationScript Create(int version, string name, string sql)
    {
        if (version < 1)
        {
            throw new SchemaMigrationException(
                $"La versione di una migrazione deve essere positiva, trovata {version}.");
        }

        if (string.IsNullOrWhiteSpace(name) || !NamePattern.IsMatch(name))
        {
            throw new SchemaMigrationException(
                $"Il nome della migrazione '{name}' deve contenere solo lettere minuscole, cifre e trattini bassi.");
        }

        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new SchemaMigrationException(
                $"La migrazione {version} ({name}) non contiene istruzioni SQL.");
        }

        return new MigrationScript(version, name, sql, ComputeChecksum(sql));
    }

    /// <summary>
    /// Costruisce la migrazione dal nome del file incorporato, che è anche il criterio di
    /// numerazione: <c>NNNN_nome.sql</c>. Il numero nel file è l'unica fonte della versione.
    /// </summary>
    public static MigrationScript FromEmbeddedFileName(string fileName, string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var match = EmbeddedFileNamePattern.Match(fileName);
        if (!match.Success)
        {
            throw new SchemaMigrationException(
                $"Il nome dello script di migrazione '{fileName}' non rispetta il formato NNNN_nome.sql.");
        }

        var version = int.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture);
        return Create(version, match.Groups["name"].Value, sql);
    }

    /// <summary>
    /// Impronta dello script, stabile rispetto ai fine riga: il testo viene incorporato nei byte
    /// della macchina che compila, quindi i CRLF si normalizzano prima di calcolare l'hash.
    /// Senza questa normalizzazione lo stesso script versionato produrrebbe impronte diverse su
    /// Windows e su Linux, e il confronto con una migrazione applicata fallirebbe a torto.
    /// </summary>
    public static string ComputeChecksum(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var normalized = sql
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant();
    }
}
