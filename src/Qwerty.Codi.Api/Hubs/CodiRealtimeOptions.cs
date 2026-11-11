namespace Qwerty.Codi.Api.Hubs;

/// <summary>
/// Impostazioni del protocollo realtime (§11.2 passo 2). Gli origin ammessi sono **configurazione** e
/// non una costante: in sviluppo il browser parla con l'app Angular su un'altra porta, e in
/// produzione con lo stesso origin del server, quindi l'elenco cambia con l'ambiente senza che
/// cambi il controllo.
/// </summary>
internal sealed class CodiRealtimeOptions
{
    /// <summary>Sezione di configurazione: <c>Codi:Realtime</c>.</summary>
    public const string SectionName = "Codi:Realtime";

    /// <summary>
    /// Origin ammessi per le connessioni hub, nella forma <c>schema://host:porta</c>. Un origin
    /// assente dall'elenco non apre la connessione; un origin uguale a quello della richiesta è
    /// ammesso anche senza elenco, perché è la stessa origine del server.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];
}
