namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Stato di consegna di un destinatario (§12.2): la consegna e l'applicazione confermata restano
/// distinte, perché una pausa può risultare applicata prima che il progetto sia salvato.
/// </summary>
public sealed record CommandDeliveryDto(
    Guid ParticipantId,
    string Status,
    long EffectiveRevision,
    bool RequiresRecovery,
    int SendAttempts,
    DateTimeOffset? SentAtUtc,
    DateTimeOffset? AcknowledgedAtUtc,
    DateTimeOffset? AppliedAtUtc,
    string? ErrorCode);
