namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Comando già risolto per uno specifico destinatario (§11.4): l'involucro che il trasporto invia
/// al gruppo del partecipante. I campi per destinatario — partecipante, epoca, istanza server —
/// li aggiunge il dispatcher all'invio, perché cambiano con la connessione e non con il comando.
/// </summary>
public sealed record CommandIssuedEnvelopeDto(
    int ProtocolVersion,
    Guid ServerInstanceId,
    Guid SessionId,
    Guid ParticipantId,
    Guid CommandId,
    long EffectiveRevision,
    long ConnectionEpoch,
    DateTimeOffset IssuedAtUtc,
    string CommandName,
    DesiredStateDto DesiredState);
