using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Persistence;

/// <summary>
/// Entità di persistenza di <c>activity_assignment</c> (§7.3, §6.5): la versione pubblicata di un
/// esercizio assegnata a una classe o a un solo partecipante. La riga non si aggiorna — un'altra
/// assegnazione è una riga nuova — quindi <see cref="RowVersion"/> resta costante e non è mappato.
///
/// <see cref="TargetParticipantId"/> esiste soltanto per un'assegnazione individuale: il CHECK della
/// tabella e il caso d'uso lo impongono, e qui il campo resta nullabile per non inventare un
/// destinatario quando l'assegnazione è di classe.
/// </summary>
public sealed class ActivityAssignmentEntity : IPersistedEntity<string>
{
    /// <summary>Valore costante esposto da <see cref="RowVersion"/>: la riga non si aggiorna.</summary>
    public const long ImmutableRowVersion = 1;

    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? LessonExerciseId { get; set; }

    /// <summary>Ambito dell'assegnazione, fra quelli di §6.5 (<c>Class</c>, <c>Participant</c>).</summary>
    public string Scope { get; set; } = string.Empty;

    public string? TargetParticipantId { get; set; }
    public string ExerciseVersionId { get; set; } = string.Empty;
    public long CreatedAtUtcMs { get; set; }

    /// <summary>Non mappato: la tabella non ha una colonna <c>row_version</c>.</summary>
    public long RowVersion { get; set; } = ImmutableRowVersion;

    /// <summary>Costruisce la riga da un'assegnazione del dominio appena creata.</summary>
    public static ActivityAssignmentEntity FromDomain(ActivityAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        return new ActivityAssignmentEntity
        {
            Id = assignment.Id.ToString("D"),
            SessionId = assignment.SessionId.ToString("D"),
            LessonExerciseId = assignment.LessonExerciseId?.ToString("D"),
            Scope = assignment.Scope.ToString(),
            TargetParticipantId = assignment.TargetParticipantId?.ToString("D"),
            ExerciseVersionId = assignment.ExerciseVersionId.ToString("D"),
            CreatedAtUtcMs = assignment.CreatedAtUtc.ToUnixTimeMilliseconds(),
        };
    }

    /// <summary>Ricostruisce e valida anche dati scritti da strumenti esterni.</summary>
    public ActivityAssignment ToDomain() => ActivityAssignment.Restore(
        Guid.Parse(Id),
        Guid.Parse(SessionId),
        LessonExerciseId is null ? null : Guid.Parse(LessonExerciseId),
        ToScope(),
        TargetParticipantId is null ? null : Guid.Parse(TargetParticipantId),
        Guid.Parse(ExerciseVersionId),
        DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtUtcMs));

    /// <summary>
    /// L'ambito è uno dei due valori di §6.5: un altro testo non è un ambito, e leggerlo come
    /// «classe» estenderebbe a tutti un'assegnazione destinata a uno solo.
    /// </summary>
    private AssignmentScope ToScope() => Scope switch
    {
        nameof(AssignmentScope.Class) => AssignmentScope.Class,
        nameof(AssignmentScope.Participant) => AssignmentScope.Participant,
        _ => throw new ArgumentException($"Ambito di assegnazione non riconosciuto: '{Scope}'.", nameof(Scope)),
    };
}
