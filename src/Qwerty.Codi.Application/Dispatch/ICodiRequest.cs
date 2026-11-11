namespace Qwerty.Codi.Application.Dispatch;

/// <summary>
/// Metadati che ogni richiesta del dispatcher dichiara nel proprio record (§4.4, §9.3). Sono
/// membri statici perché appartengono al contratto della richiesta, non all'istanza: il registro
/// li legge all'avvio, quando non esiste ancora alcun payload da deserializzare.
/// </summary>
public interface ICodiRequest
{
    /// <summary>
    /// Nome pubblico della richiesta, in PascalCase (§10.1). Non deriva dal nome del tipo: la
    /// classe si può rinominare senza rompere il client.
    /// </summary>
    static abstract string RequestName { get; }

    /// <summary>Policy dichiarata e verificata dal dispatcher prima dell'handler (§9.3).</summary>
    static abstract CodiPolicy Policy { get; }
}
