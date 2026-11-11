using Mediator;

namespace Qwerty.Codi.Application.Dispatch;

/// <summary>
/// Chiude il tipo della risposta su una richiesta concreta e delega al mediator. Il mediator si
/// risolve dal contenitore dell'ambito corrente, così gli handler conservano la durata loro
/// configurata e il dispatcher non ne impone una.
/// </summary>
internal sealed class MediatorRequestInvoker<TRequest, TResponse> : ICodiRequestInvoker
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<object?> InvokeAsync(
        IServiceProvider services,
        object request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        var mediator = services.GetService(typeof(IMediator)) as IMediator
            ?? throw new InvalidOperationException(
                "Il mediator non è registrato nel contenitore dei servizi: gli handler non sono stati aggiunti.");

        return await mediator
            .Send((TRequest)request, cancellationToken)
            .ConfigureAwait(false);
    }
}
