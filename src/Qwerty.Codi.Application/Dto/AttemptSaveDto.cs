namespace Qwerty.Codi.Application.Dto;

public sealed record AttemptSaveDto(Guid AttemptId, Guid SaveId, long RowVersion, long ProjectRevision,
    string WorkspaceHash, DateTimeOffset LastSavedAtUtc, bool CheckpointSaved);
