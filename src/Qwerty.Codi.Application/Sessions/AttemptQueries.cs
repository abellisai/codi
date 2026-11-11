using Qwerty.Codi.Application.Dto;
using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Application.Sessions;

public sealed record GetAttemptFinalizationsQuery : ICodiQuery<IReadOnlyList<AttemptFinalizationGrantDto>>, ICodiContextualRequest
{
    public static string RequestName => "GetAttemptFinalizations";
    public static CodiPolicy Policy => CodiPolicy.FinalizingParticipant;
    [JsonIgnore] public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;
    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

public sealed record ListParticipantAttemptsQuery(Guid SessionId, Guid ParticipantId)
    : ICodiQuery<IReadOnlyList<AttemptDto>>, ICodiContextualRequest
{
    public static string RequestName => "ListParticipantAttempts";
    public static CodiPolicy Policy => CodiPolicy.SessionController;
    [JsonIgnore] public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;
    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}
