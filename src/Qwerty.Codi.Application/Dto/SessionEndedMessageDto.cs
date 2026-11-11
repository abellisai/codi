namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Chiusura della sessione per il singolo alunno (§11.3, §15.3): l'ultimo involucro da applicare,
/// i permessi di salvataggio conclusivo concessi alla sua credenziale e l'istante in cui scadono.
/// L'ordine dei campi è quello che il client già legge: <c>command</c>, <c>grants</c>,
/// <c>expiresAtUtc</c>.
///
/// Era l'unico messaggio del canale realtime costruito come oggetto anonimo: il nome dell'evento
/// stava in una stringa dell'adattatore e la forma della chiusura non era dichiarata da nessuna
/// parte, pur essendo descritta nella specifica.
/// </summary>
public sealed record SessionEndedMessageDto(
    CommandIssuedEnvelopeDto Command,
    IReadOnlyList<AttemptFinalizationGrantDto> Grants,
    DateTimeOffset ExpiresAtUtc);
