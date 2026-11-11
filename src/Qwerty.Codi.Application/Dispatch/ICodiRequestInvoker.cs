namespace Qwerty.Codi.Application.Dispatch;

/// <summary>
/// Invoca l'handler di una richiesta già deserializzata. Esiste perché il dispatcher conosce il
/// tipo della richiesta solo a runtime, mentre il mediator è generico: la chiusura generica
/// avviene una volta sola, quando si costruisce il registro, e non a ogni richiesta.
/// </summary>
public interface ICodiRequestInvoker
{
    /// <summary>Esegue la richiesta e restituisce l'esito tipizzato come oggetto.</summary>
    ValueTask<object?> InvokeAsync(
        IServiceProvider services,
        object request,
        CancellationToken cancellationToken);
}
