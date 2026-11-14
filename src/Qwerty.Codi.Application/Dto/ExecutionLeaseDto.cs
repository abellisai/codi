namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Autorizzazione temporanea consegnata al client (§11.3, §12.6): nonce, revisione, epoca, durata
/// massima e istante di scadenza. Il nonce è monouso: riusarlo non rinnova.
/// </summary>
public sealed record ExecutionLeaseDto(
    string Nonce,
    long EffectiveRevision,
    long ConnectionEpoch,
    long MaxDurationSeconds,
    DateTimeOffset ValidUntilUtc,
    Guid ServerInstanceId,
    ParticipantProfileDto? Profile = null);
