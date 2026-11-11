namespace Qwerty.Codi.Infrastructure.Configuration;

/// <summary>
/// Sezione <c>Codi</c> di §18.2. Contiene solo le impostazioni che hanno già un consumatore:
/// le altre sezioni previste dalla specifica vengono aggiunte insieme a chi le usa, per non
/// avere configurazione morta.
/// </summary>
public sealed class CodiOptions
{
    /// <summary>Directory dei dati. Relativa alla base del servizio, se non assoluta.</summary>
    public string DataDirectory { get; set; } = string.Empty;

    /// <summary>Impostazioni del file di database.</summary>
    public DatabaseOptions Database { get; set; } = new();

    /// <summary>
    /// Account amministrativo inserito automaticamente alla preparazione del database (§9.1).
    /// Vuoto significa nessun inserimento: l'ambiente decide, e chi non lo configura non crea
    /// credenziali.
    /// </summary>
    public SeedAdminOptions SeedAdmin { get; set; } = new();
}

/// <summary>Sezione <c>Codi:SeedAdmin</c>: primo account amministrativo creato all'avvio.</summary>
public sealed class SeedAdminOptions
{
    /// <summary>Nome utente dell'account iniziale. Vuoto disabilita l'inserimento automatico.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Password dell'account iniziale. Vuota disabilita l'inserimento automatico.</summary>
    public string Password { get; set; } = string.Empty;
}

/// <summary>Sezione <c>Codi:Database</c> di §18.2.</summary>
public sealed class DatabaseOptions
{
    /// <summary>Nome del file di database dentro la directory dati.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Attesa su database occupato, applicata a ogni connessione (§8.1).</summary>
    public int BusyTimeoutMs { get; set; }
}
