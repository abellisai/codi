using Qwerty.Codi.Infrastructure.Configuration;

namespace Qwerty.Codi.Infrastructure.Persistence;

/// <summary>
/// Posizione del file di database e attesa su lock, già risolte in percorso assoluto (§18.2).
/// Le impostazioni sono condivise da tutte le connessioni della stessa unità di lavoro.
/// </summary>
public sealed record SqliteDatabase(string FilePath, int BusyTimeoutMs)
{
    /// <summary>
    /// Risolve la configurazione di §18.2 contro una base esplicita del servizio: un percorso
    /// dati relativo viene composto con la base, non con la directory di lavoro del processo.
    /// </summary>
    public static SqliteDatabase FromOptions(string basePath, CodiOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.DataDirectory))
        {
            throw new ArgumentException("La directory dei dati non è configurata.", nameof(options));
        }

        var fileName = options.Database.FileName;
        if (string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName))
        {
            throw new ArgumentException(
                "Il nome del file di database deve essere un nome di file, non un percorso.",
                nameof(options));
        }

        if (options.Database.BusyTimeoutMs <= 0)
        {
            throw new ArgumentException(
                "L'attesa su database occupato deve essere un numero di millisecondi positivo.",
                nameof(options));
        }

        var directory = Path.GetFullPath(options.DataDirectory, basePath);
        var filePath = Path.GetFullPath(fileName, directory);

        return new SqliteDatabase(filePath, options.Database.BusyTimeoutMs);
    }
}
