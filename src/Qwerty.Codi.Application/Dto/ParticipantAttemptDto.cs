namespace Qwerty.Codi.Application.Dto;

/// <summary>Lavoro corrente dell'alunno, con il progetto e la presenza del checkpoint.</summary>
public sealed record ParticipantAttemptDto(
    Guid AttemptId,
    long AttemptNumber,
    string Status,
    string WorkspaceJson,
    string WorkspaceHash,
    long ProjectRevision,
    bool HasCheckpoint,
    string? CheckpointEngineVersion,
    DateTimeOffset? LastSavedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long RowVersion);
