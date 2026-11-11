namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Ricevuta dell'anteprima (§15.5): identità, revisione, impronta e istante di ricezione. Il
/// docente riceve <c>WorkspacePreviewAvailable</c> con questi dati e legge il contenuto con
/// <c>GetParticipantPreview</c>.
/// </summary>
public sealed record WorkspacePreviewDto(Guid ParticipantId, Guid SessionId, Guid AttemptId,
    long ProjectRevision, string WorkspaceHash, DateTimeOffset ReceivedAtUtc);
