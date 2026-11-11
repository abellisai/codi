using Qwerty.Codi.Application.Dto;
using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Ultima anteprima pubblicata di un alunno (§10.5, §15.5). Il docente la legge quando vuole il
/// contenuto; nella griglia basta la freschezza, che arriva con l'istantanea della sessione.
/// </summary>
public sealed record GetParticipantPreviewQuery(Guid SessionId, Guid ParticipantId)
    : ICodiQuery<ParticipantPreviewViewDto?>, ICodiContextualRequest
{
    public static string RequestName => "GetParticipantPreview";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}
