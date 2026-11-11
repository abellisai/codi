namespace Qwerty.Codi.Application.Dispatch;

/// <summary>
/// Catalogo incoerente: due richieste con lo stesso nome, una richiesta anonima fuori
/// dall'elenco chiuso di §10.4, oppure una richiesta locale esposta via HTTP. È un guasto di
/// avvio, non un errore di richiesta: il server non deve partire con un catalogo ambiguo.
/// </summary>
public sealed class CodiDispatchConfigurationException : InvalidOperationException
{
    public CodiDispatchConfigurationException(string message)
        : base(message)
    {
    }
}
