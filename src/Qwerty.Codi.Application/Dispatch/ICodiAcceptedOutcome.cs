namespace Qwerty.Codi.Application.Dispatch;

/// <summary>
/// Esito di un comando che il server ha **persistito per consegnarlo** (§10.1, §10.6): il trasporto
/// lo traduce in <c>202 Accepted</c> con il riepilogo delle consegne, perché il risultato descrive il
/// comando registrato e non la sua applicazione sui client.
///
/// Un comando senza distribuzione — e una query — restituiscono un esito normale: il trasporto non
/// deduce lo stato HTTP dal tipo del caso d'uso, ma dalla natura dell'esito, che è un dato del
/// contratto.
/// </summary>
public interface ICodiAcceptedOutcome
{
}
