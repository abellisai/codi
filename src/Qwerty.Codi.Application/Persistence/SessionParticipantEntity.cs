using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Persistence;

/// <summary>
/// Entità di persistenza di <c>session_participant</c> (§7.4). L'ammissione è il testo dell'enum,
/// l'epoca della connessione un intero e gli istanti epoch millisecondi, con l'esclusione
/// annullabile finché la partecipazione è ammessa.
/// </summary>
public sealed class SessionParticipantEntity : IPersistedEntity<string>
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? StationId { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Testo di <see cref="AdmissionStatus"/> come memorizzato in <c>admission_status</c>.</summary>
    public string AdmissionStatusText { get; set; } = string.Empty;

    public long ConnectionEpoch { get; set; }
    public long JoinedAtUtcMs { get; set; }
    public long? ExcludedAtUtcMs { get; set; }
    public long CreatedAtUtcMs { get; set; }
    public long UpdatedAtUtcMs { get; set; }
    public long RowVersion { get; set; }

    public static SessionParticipantEntity FromDomain(SessionParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        return new SessionParticipantEntity
        {
            Id = participant.Id.ToString("D"),
            SessionId = participant.SessionId.ToString("D"),
            StationId = participant.StationId?.ToString("D"),
            DisplayName = participant.DisplayName,
            AdmissionStatusText = participant.AdmissionStatus.ToString(),
            ConnectionEpoch = participant.ConnectionEpoch,
            JoinedAtUtcMs = participant.JoinedAtUtc.ToUnixTimeMilliseconds(),
            ExcludedAtUtcMs = participant.ExcludedAtUtc?.ToUnixTimeMilliseconds(),
            CreatedAtUtcMs = participant.CreatedAtUtc.ToUnixTimeMilliseconds(),
            UpdatedAtUtcMs = participant.UpdatedAtUtc.ToUnixTimeMilliseconds(),
            RowVersion = participant.RowVersion,
        };
    }

    /// <summary>Ricostruisce e valida anche dati scritti da strumenti esterni.</summary>
    public SessionParticipant ToDomain() => SessionParticipant.Restore(
        Guid.Parse(Id),
        Guid.Parse(SessionId),
        StationId is null ? null : Guid.Parse(StationId),
        DisplayName,
        ToAdmissionStatus(),
        ConnectionEpoch,
        DateTimeOffset.FromUnixTimeMilliseconds(JoinedAtUtcMs),
        ExcludedAtUtcMs is { } excluded ? DateTimeOffset.FromUnixTimeMilliseconds(excluded) : null,
        DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtUtcMs),
        DateTimeOffset.FromUnixTimeMilliseconds(UpdatedAtUtcMs),
        RowVersion);

    /// <summary>
    /// L'ammissione è uno dei due valori di §7.4: un altro testo non è uno stato, e leggerlo come
    /// «ammesso» darebbe operatività a chi è stato escluso.
    /// </summary>
    private AdmissionStatus ToAdmissionStatus() => AdmissionStatusText switch
    {
        nameof(AdmissionStatus.Admitted) => AdmissionStatus.Admitted,
        nameof(AdmissionStatus.Excluded) => AdmissionStatus.Excluded,
        _ => throw new ArgumentException(
            $"Stato di ammissione non riconosciuto: '{AdmissionStatusText}'.", nameof(AdmissionStatusText)),
    };
}
