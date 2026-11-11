using Mediator;

namespace Qwerty.Codi.Application.Dispatch;

/// <summary>
/// Query del catalogo di §10.5: legge e restituisce un DTO dedicato, senza mutazioni persistenti.
/// L'identità si ricava dalla credenziale, non dai parametri della richiesta.
/// </summary>
/// <typeparam name="TResponse">DTO di lettura, distinto dall'entità memorizzata.</typeparam>
public interface ICodiQuery<TResponse> : ICodiRequest, IRequest<TResponse>;
