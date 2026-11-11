using Qwerty.Codi.Domain.Sessions;
namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Partecipazione come la vede la regia (§10.5): l'identità nella sessione, la postazione, lo stato
/// di ammissione e la versione di riga per la concorrenza ottimistica. Non è l'anagrafica di un
/// alunno — nomi uguali sono ammessi — e non contiene il lavoro assegnato, che appartiene allo
/// stato del partecipante e arriverà con i comandi di regia dei contenuti.
///
/// <c>rowVersion</c> viaggia come gli altri contatori a 64 bit del contratto (§10.2).
/// </summary>
public sealed record SessionParticipantDto(
    Guid Id,
    Guid SessionId,
    string DisplayName,
    Guid? StationId,
    string AdmissionStatus,
    long ConnectionEpoch,
    DateTimeOffset JoinedAtUtc,
    DateTimeOffset? ExcludedAtUtc,
    long RowVersion)
{
    public static SessionParticipantDto FromDomain(SessionParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        return new SessionParticipantDto(
            participant.Id,
            participant.SessionId,
            participant.DisplayName,
            participant.StationId,
            participant.AdmissionStatus.ToString(),
            participant.ConnectionEpoch,
            participant.JoinedAtUtc,
            participant.ExcludedAtUtc,
            participant.RowVersion);
    }
}
