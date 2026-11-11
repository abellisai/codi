using Qwerty.Codi.Infrastructure.Migrations;

namespace Qwerty.Codi.Infrastructure.Persistence;

/// <summary>
/// Stato di prontezza del database (§18.4): modo di journal letto dal file e allineamento dello
/// schema alle migrazioni distribuite. Riassume solo esiti booleani e numeri di versione, senza
/// contenuto dei dati.
/// </summary>
public sealed record DatabaseReadiness(string JournalMode, MigrationStatus Schema)
{
    /// <summary>Il journal è effettivamente WAL, non soltanto richiesto (§8.1).</summary>
    public bool UsesWriteAheadLogging =>
        string.Equals(JournalMode, "wal", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Il database è utilizzabile: journal WAL attivo e schema senza divergenze né migrazioni
    /// in attesa. Una versione sconosciuta o un'impronta errata impediscono la prontezza (§8.3).
    /// </summary>
    public bool IsReady => UsesWriteAheadLogging && Schema.IsUpToDate;
}
