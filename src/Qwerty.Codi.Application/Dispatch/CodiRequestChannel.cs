namespace Qwerty.Codi.Application.Dispatch;

/// <summary>
/// Canale su cui una richiesta viene eseguita. Le richieste locali non sono raggiungibili dal
/// dispatcher HTTP: <c>BootstrapAdminAccount</c> esiste solo da riga di comando (§10.4).
/// </summary>
public enum CodiRequestChannel
{
    /// <summary>Endpoint dedicati e dispatcher HTTP.</summary>
    Http,

    /// <summary>
    /// Metodo di un hub di §11. Il trasporto è il collegamento SignalR, ma la richiesta attraversa
    /// lo stesso registro e la stessa verifica della policy dell'HTTP: un hub autenticato non è di
    /// per sé autorizzato a ogni operazione (§11.7).
    /// </summary>
    Hub,

    /// <summary>Comando locale dello stesso processo, fuori dall'API pubblica.</summary>
    Local,
}
