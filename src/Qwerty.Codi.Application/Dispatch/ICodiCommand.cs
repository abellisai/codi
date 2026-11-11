using Mediator;

namespace Qwerty.Codi.Application.Dispatch;

/// <summary>
/// Comando del catalogo di §10.4: muta lo stato e restituisce un esito tipizzato. Attraversa il
/// behavior di validazione e quello di transazione quando esistono.
/// </summary>
/// <typeparam name="TResponse">Esito del caso d'uso, tipizzato e distinto dalle entità memorizzate.</typeparam>
public interface ICodiCommand<TResponse> : ICodiRequest, IRequest<TResponse>;
