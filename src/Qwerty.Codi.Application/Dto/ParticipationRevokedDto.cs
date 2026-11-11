namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// La partecipazione non è più utilizzabile (§11.3): la credenziale è stata revocata o la
/// partecipazione esclusa. Il client mostra il motivo e non ritenta: non è un errore di rete.
/// </summary>
public sealed record ParticipationRevokedDto(string Reason);
