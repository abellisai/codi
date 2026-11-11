using Qwerty.Codi.Application.Dto;
using System.Text.Json;
using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Application.Sessions;

public sealed record SaveAttemptWorkspaceCommand(Guid AttemptId, Guid SaveId, long ConnectionEpoch,
    long? BaseRowVersion, long ClientProjectRevision, string WorkspaceJson, string WorkspaceHash)
    : ICodiCommand<AttemptSaveDto>, ICodiContextualRequest
{
    public static string RequestName => "SaveAttemptWorkspace";
    public static CodiPolicy Policy => CodiPolicy.ActiveParticipantConnection;
    [JsonIgnore] public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;
    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

public sealed record SaveAttemptCheckpointCommand(Guid AttemptId, Guid SaveId, long ConnectionEpoch,
    long? BaseRowVersion, AttemptCheckpoint Checkpoint) : ICodiCommand<AttemptSaveDto>, ICodiContextualRequest
{
    public static string RequestName => "SaveAttemptCheckpoint";
    public static CodiPolicy Policy => CodiPolicy.ActiveParticipantConnection;
    [JsonIgnore] public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;
    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

public sealed record FinalizeAttemptCommand(Guid AttemptId, Guid GrantId, Guid SaveId, long ConnectionEpoch,
    long? BaseRowVersion, long ClientProjectRevision, string WorkspaceJson, string WorkspaceHash,
    AttemptCheckpoint? Checkpoint = null) : ICodiCommand<AttemptSaveDto>, ICodiContextualRequest
{
    public static string RequestName => "FinalizeAttempt";
    public static CodiPolicy Policy => CodiPolicy.FinalizingParticipant;
    [JsonIgnore] public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;
    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>Formato persistito; RuntimeState appartiene al motore dichiarato.</summary>
public sealed record AttemptCheckpoint(int SchemaVersion, string EngineVersion, Guid AttemptId,
    Guid ExerciseVersionId, long ExecutionGeneration, Guid RunId, string WorkspaceHash,
    long ProjectRevision, string RuntimeStatus, JsonElement RuntimeState);
