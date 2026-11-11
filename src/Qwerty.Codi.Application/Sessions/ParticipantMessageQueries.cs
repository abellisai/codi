using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Thread di un alunno letto dal docente (§8, §15.1). La lettura ha un effetto osservabile: azzera il
/// conteggio dei non letti sulla card, perché «non letti» significa appunto «non ancora letti dal
/// docente». La proprietà della sessione si controlla come nelle altre letture di regia (§6.4).
/// </summary>
public sealed record GetParticipantMessagesQuery(Guid SessionId, Guid ParticipantId)
    : ICodiQuery<ParticipantThreadDto>, ICodiContextualRequest
{
    public static string RequestName => "GetParticipantMessages";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Thread letto dall'alunno (§8, §15.1): solo il proprio, perché l'identità arriva dalla credenziale
/// verificata e non dai parametri (§9.3). Il conteggio dei non letti resta quello del docente — il
/// server non misura se un bambino ha letto, e non finge di farlo.
/// </summary>
public sealed record GetOwnMessagesQuery : ICodiQuery<ParticipantThreadDto>, ICodiContextualRequest
{
    public static string RequestName => "GetOwnMessages";

    public static CodiPolicy Policy => CodiPolicy.AdmittedParticipant;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}
