using System.Text.Json;
namespace Qwerty.Codi.Application.Dto;

public sealed record ParticipantObservationDto(Guid SessionId, Guid ParticipantId,
    long ConnectionEpoch, long EffectiveRevision, Guid? AttemptId, long ExecutionGeneration,
    long Sequence, string RuntimeStatus, string? Reason, Guid? RunId, string? WorkspaceHash,
    JsonElement? Scene, DateTimeOffset ReceivedAtUtc);
