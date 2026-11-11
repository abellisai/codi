namespace Qwerty.Codi.Application.Dispatch;

/// <summary>
/// Richiesta che usa l'identità verificata: l'esecutore la associa dopo la deserializzazione e
/// prima dell'handler. Il contesto non è un campo del contratto JSON (§4.4, §9.3).
/// </summary>
public interface ICodiContextualRequest
{
    ICodiRequest WithContext(CodiRequestContext context);
}
