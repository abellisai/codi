namespace Qwerty.Codi.Application.Dto;

public sealed record AttemptFinalizationGrantDto(Guid GrantId, Guid AttemptId, long ConnectionEpoch,
    long ExecutionGeneration, DateTimeOffset ExpiresAtUtc, Guid SessionId, Guid ParticipantId);
