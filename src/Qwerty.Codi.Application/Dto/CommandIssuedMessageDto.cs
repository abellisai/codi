namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Messaggio <c>CommandIssued</c> in coda nell'outbox (§11.3, §11.4): i campi che dipendono dal
/// comando e non dal destinatario. Quelli per destinatario — <c>participantId</c>,
/// <c>connectionEpoch</c>, <c>serverInstanceId</c> — li aggiunge il dispatcher al momento dell'invio,
/// perché cambiano con la connessione e non con il comando.
/// </summary>
public sealed record CommandIssuedMessageDto(
    int ProtocolVersion,
    Guid CommandId,
    Guid SessionId,
    long EffectiveRevision,
    DateTimeOffset IssuedAtUtc,
    string CommandName,
    DesiredStateDto DesiredState)
{
    /// <summary>Versione del protocollo di §11.4: la prima, e per ora l'unica.</summary>
    public IReadOnlyDictionary<Guid, DesiredStateDto>? DesiredStates { get; init; }

    public const int CurrentProtocolVersion = 1;

    /// <summary>
    /// Nome dell'evento hub di §11.3 in coda nell'outbox: distinto dal nome del comando che l'ha
    /// prodotto, che viaggia dentro il documento.
    /// </summary>
    public const string MessageType = "CommandIssued";
}
