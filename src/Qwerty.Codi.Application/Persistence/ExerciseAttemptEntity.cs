using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Persistence;

/// <summary>
/// Entità di persistenza di <c>exercise_attempt</c> (§7.3): il lavoro di un alunno su
/// un'assegnazione. Il progetto vive nella riga (`workspace_json`) con la sua impronta e la revisione
/// del progetto, che è il contatore con cui un salvataggio dichiara su quale stato si basa: non è
/// `row_version`, perché la scrittura del progetto la fa il player con la propria numerazione (§15.2).
///
/// <see cref="SessionId"/> sta qui, e non nella tabella di §7.3, per rendere verificabili dal database
/// la coerenza con la partecipazione e con l'assegnazione: è una scelta locale registrata nella
/// migrazione `0008`.
/// </summary>
public sealed class ExerciseAttemptEntity : IPersistedEntity<string>
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ParticipantId { get; set; } = string.Empty;
    public string AssignmentId { get; set; } = string.Empty;
    public string ExerciseVersionId { get; set; } = string.Empty;

    /// <summary>Progressivo per coppia partecipante/assegnazione: un azzeramento non sovrascrive.</summary>
    public long AttemptNumber { get; set; }

    /// <summary>Stato del tentativo, fra quelli della migrazione <c>0008</c>.</summary>
    public string Status { get; set; } = string.Empty;

    public string WorkspaceJson { get; set; } = string.Empty;
    public string WorkspaceHash { get; set; } = string.Empty;
    public long ProjectRevision { get; set; }
    public string? CheckpointJson { get; set; }
    public string? CheckpointEngineVersion { get; set; }
    public long? LastSavedAtUtcMs { get; set; }
    public long? CompletedAtUtcMs { get; set; }
    public long CreatedAtUtcMs { get; set; }
    public long UpdatedAtUtcMs { get; set; }
    public long RowVersion { get; set; } = 1;

    /// <summary>Costruisce la riga da un tentativo del dominio.</summary>
    public static ExerciseAttemptEntity FromDomain(ExerciseAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        return new ExerciseAttemptEntity
        {
            Id = attempt.Id.ToString("D"),
            SessionId = attempt.SessionId.ToString("D"),
            ParticipantId = attempt.ParticipantId.ToString("D"),
            AssignmentId = attempt.AssignmentId.ToString("D"),
            ExerciseVersionId = attempt.ExerciseVersionId.ToString("D"),
            AttemptNumber = attempt.AttemptNumber,
            Status = attempt.Status.ToString(),
            WorkspaceJson = attempt.WorkspaceJson,
            WorkspaceHash = attempt.WorkspaceHash,
            ProjectRevision = attempt.ProjectRevision,
            CreatedAtUtcMs = attempt.CreatedAtUtc.ToUnixTimeMilliseconds(),
            UpdatedAtUtcMs = attempt.UpdatedAtUtc.ToUnixTimeMilliseconds(),
            RowVersion = attempt.RowVersion,
        };
    }

    /// <summary>Ricostruisce e valida anche dati scritti da strumenti esterni.</summary>
    public ExerciseAttempt ToDomain() => ExerciseAttempt.Restore(
        Guid.Parse(Id),
        Guid.Parse(SessionId),
        Guid.Parse(ParticipantId),
        Guid.Parse(AssignmentId),
        Guid.Parse(ExerciseVersionId),
        AttemptNumber,
        ToStatus(),
        WorkspaceJson,
        WorkspaceHash,
        ProjectRevision,
        DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtUtcMs),
        DateTimeOffset.FromUnixTimeMilliseconds(UpdatedAtUtcMs),
        RowVersion);

    /// <summary>
    /// Lo stato è uno di quelli scritti dalla migrazione: un altro testo non è uno stato, e
    /// interpretarlo come «pronto» riabiliterebbe un tentativo che il docente ha sospeso.
    /// </summary>
    private AttemptStatus ToStatus() => Status switch
    {
        nameof(AttemptStatus.Ready) => AttemptStatus.Ready,
        nameof(AttemptStatus.Running) => AttemptStatus.Running,
        nameof(AttemptStatus.Paused) => AttemptStatus.Paused,
        nameof(AttemptStatus.Completed) => AttemptStatus.Completed,
        nameof(AttemptStatus.Archived) => AttemptStatus.Archived,
        _ => throw new ArgumentException($"Stato del tentativo non riconosciuto: '{Status}'.", nameof(Status)),
    };
}
