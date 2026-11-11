namespace Qwerty.Codi.Application.Dispatch;

/// <summary>
/// Richiesta che non ha un canale HTTP. Il registro la rifiuta se qualcuno prova a esporla dal
/// dispatcher: la policy ammette l'invocazione, il canale decide dove (§10.4).
/// </summary>
public interface ILocalOnlyRequest : ICodiRequest;
