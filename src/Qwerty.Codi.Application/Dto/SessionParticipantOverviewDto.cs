using Qwerty.Codi.Application.Abstractions;
namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Un alunno nella griglia: identità della partecipazione, presenza, stato desiderato e sintesi del
/// lavoro corrente. I campi del lavoro sono assenti finché non c'è un'esercitazione assegnata, e la
/// loro assenza è un'informazione — non uno zero da interpretare.
/// </summary>
public sealed record SessionParticipantOverviewDto(
    Guid ParticipantId,
    string DisplayName,
    Guid? StationId,
    string AdmissionStatus,
    long ConnectionEpoch,
    DateTimeOffset JoinedAtUtc,
    DateTimeOffset? ExcludedAtUtc,
    ParticipantPresence Presence,
    DateTimeOffset? LastHeartbeatUtc,
    string? View,
    bool InteractionEnabled,
    bool TeacherPaused,
    bool RecoveryBlocked,
    long? ExecutionGeneration,
    bool HasDirective,
    Guid? AttemptId,
    long? AttemptNumber,
    // La versione della riga del tentativo: il comando di azzeramento dichiara il lavoro che il
    // docente ha visto nella conferma (§10.6), e la conferma la porta senza una seconda lettura.
    long? AttemptRowVersion,
    string? AttemptStatus,
    long? ProjectRevision,
    DateTimeOffset? LastSavedAtUtc,
    /// <summary>
    /// Messaggi scritti dall'alunno che il docente non ha ancora letto (§8, §15.1). È un dato della chat
    /// in memoria, non dello stato desiderato: la card lo mostra senza aprire il thread.
    /// </summary>
    int UnreadMessages)
{
    public long RowVersion { get; init; }

    public ParticipantObservationDto? ObservedState { get; init; }
}
