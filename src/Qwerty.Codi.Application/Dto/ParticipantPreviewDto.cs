namespace Qwerty.Codi.Application.Dto;

/// <summary>Anteprima conservata in cache: il contenuto che il docente può leggere.</summary>
public sealed record ParticipantPreviewDto(Guid SessionId, Guid ParticipantId, Guid AttemptId,
    long ConnectionEpoch, long ExecutionGeneration, long ProjectRevision, string WorkspaceHash,
    string WorkspaceJson, DateTimeOffset ReceivedAtUtc);
