namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Stato completo del destinatario all'handshake (§11.2 passo 4, §11.3): la revisione efficace, la
/// nuova epoca e lo stato desiderato da applicare. I riferimenti a esercizio e progetto si
/// aggiungono quando esistono assegnazioni e tentativi (§12.4).
/// </summary>
public sealed record ParticipantSnapshotDto(
    int ProtocolVersion,
    Guid ServerInstanceId,
    Guid SessionId,
    Guid ParticipantId,
    long EffectiveRevision,
    long ConnectionEpoch,
    DateTimeOffset IssuedAtUtc,
    DesiredStateDto DesiredState,
    ParticipantProfileDto? Profile = null);
