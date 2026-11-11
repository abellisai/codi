namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Esito di <c>DeleteSession</c> (§6.1, §10.4): la sessione indicata e i suoi dati sono stati
/// rimossi. Non porta revisione né versione di riga, perché la riga non esiste più: un secondo
/// tentativo con la stessa richiesta trova la sessione assente e risponde <c>404</c>, che è la
/// risposta onesta a «quella sessione non c'è».
/// </summary>
public sealed record SessionDeletedDto(Guid SessionId);
