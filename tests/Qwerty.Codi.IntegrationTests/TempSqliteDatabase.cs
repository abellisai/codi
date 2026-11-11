using Qwerty.Codi.Infrastructure.Migrations;
using Qwerty.Codi.Infrastructure.Persistence;
using Qwerty.Codi.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Qwerty.Codi.IntegrationTests;

/// <summary>
/// Database SQLite temporaneo dedicato al singolo test (§20.3): percorso univoco sotto la
/// cartella temporanea, factory di connessione pronta e rimozione della cartella alla fine,
/// compresi i file <c>-wal</c> e <c>-shm</c> prodotti dal journal WAL.
/// </summary>
public sealed class TempSqliteDatabase : IDisposable
{
    private const int BusyTimeoutMs = 5000;

    private readonly string _basePath;
    private readonly ServiceProvider _persistenceServices;

    private TempSqliteDatabase(string basePath)
    {
        _basePath = basePath;

        // SQLite non crea la cartella del file: la si prepara qui, come fa il bootstrap con
        // la directory dati configurata.
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);

        Database = new SqliteDatabase(DatabasePath, BusyTimeoutMs);
        Factory = new SqliteConnectionFactory(Database);
        var services = new ServiceCollection();
        services.AddSingleton(Factory);
        services.AddCodiPersistence();
        _persistenceServices = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    public SqliteDatabase Database { get; }

    public SqliteConnectionFactory Factory { get; }
    public IUnitOfWorkContext Units => _persistenceServices.GetRequiredService<IUnitOfWorkContext>();

    private string DatabasePath => Path.Combine(_basePath, "data", "codi.db");

    public static TempSqliteDatabase Create() =>
        new(Path.Combine(Path.GetTempPath(), $"codi-{Guid.NewGuid():N}"));

    public void Dispose()
    {
        _persistenceServices.Dispose();
        try
        {
            if (Directory.Exists(_basePath))
            {
                Directory.Delete(_basePath, recursive: true);
            }
        }
        catch (IOException)
        {
            // Il percorso è univoco e non riutilizzato: un file temporaneo non eliminato non
            // deve trasformare un test superato in un test fallito.
        }
    }
}
