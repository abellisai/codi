namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Il tentativo corrente di un alunno: progetto, impronta, revisione dichiarata dal client e
/// checkpoint. La revisione del progetto e l'impronta sono ciò con cui il client riconosce un
/// salvataggio ripetuto o incoerente (§15.2).
/// </summary>
public sealed record AttemptDto(
    Guid AttemptId,
    Guid AssignmentId,
    Guid ExerciseVersionId,
    long AttemptNumber,
    string Status,
    string WorkspaceJson,
    string WorkspaceHash,
    long ProjectRevision,
    string? CheckpointJson,
    string? CheckpointEngineVersion,
    DateTimeOffset? LastSavedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long RowVersion = 1);
