namespace Qwerty.Codi.Infrastructure.Migrations;

/// <summary>
/// Migrazione non applicabile: catalogo incoerente, schema del database non allineato agli
/// script distribuiti, oppure script fallito. È un guasto di avvio o di prontezza, non un
/// errore di richiesta, quindi non va tradotto in un esito dell'API (§8.3, §16.1).
/// </summary>
public sealed class SchemaMigrationException : InvalidOperationException
{
    public SchemaMigrationException(string message)
        : base(message)
    {
    }

    public SchemaMigrationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
