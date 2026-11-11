using Qwerty.Codi.Application.Dto;
using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Stato delle consegne di un comando, per destinatario (§10.6). È la query a cui rimanda un comando
/// accettato: le conferme arrivano dopo l'invio, quindi non si deducono dalla risposta e si
/// interrogano quando servono.
///
/// La proprietà della sessione si verifica nell'handler: lo stato delle consegne di una sessione
/// altrui non è leggibile. L'identità arriva dal contesto verificato dal trasporto, non dal payload.
/// </summary>
public sealed record GetCommandStatusQuery(Guid SessionId, Guid CommandId)
    : ICodiQuery<CommandStatusDto>, ICodiContextualRequest
{
    public static string RequestName => "GetCommandStatus";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}
