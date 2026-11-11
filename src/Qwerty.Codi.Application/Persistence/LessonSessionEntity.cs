using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Persistence;

/// <summary>
/// Entità di persistenza di <c>lesson_session</c> (§7.4). Lo stato è il testo dell'enum, gli
/// ingressi un flag intero e gli istanti epoch millisecondi, con avvio e termine annullabili
/// quando la sessione non è ancora avviata o non è terminata.
/// </summary>
public sealed class LessonSessionEntity : IPersistedEntity<string>
{
    /// <summary>Testo dello stato terminale come memorizzato in <c>status</c> (§6.1).</summary>
    public const string EndedStatusText = nameof(SessionStatus.Ended);

    public string Id { get; set; } = string.Empty;
    public string LessonId { get; set; } = string.Empty;
    public string ClassroomId { get; set; } = string.Empty;
    public string ControllerAdminId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long AdmissionsOpenFlag { get; set; }
    public long SessionRevision { get; set; }
    public string ClassDefaultJson { get; set; } = string.Empty;

    /// <summary>
    /// Codice di ingresso dettato a voce (§9.4), otto cifre, nullo per le sessioni non ancora
    /// avviate e per quelle create prima che il codice esistesse.
    /// </summary>
    public string? JoinCode { get; set; }

    public long? StartedAtUtcMs { get; set; }
    public long? EndedAtUtcMs { get; set; }
    public long CreatedAtUtcMs { get; set; }
    public long UpdatedAtUtcMs { get; set; }
    public long RowVersion { get; set; }

    public static LessonSessionEntity FromDomain(LessonSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new LessonSessionEntity
        {
            Id = session.Id.ToString("D"),
            LessonId = session.LessonId.ToString("D"),
            ClassroomId = session.ClassroomId.ToString("D"),
            ControllerAdminId = session.ControllerAdminId.ToString("D"),
            Status = session.Status.ToString(),
            AdmissionsOpenFlag = session.AdmissionsOpen ? 1 : 0,
            SessionRevision = session.SessionRevision,
            ClassDefaultJson = session.ClassDefaultJson,
            JoinCode = session.JoinCode?.Value,
            StartedAtUtcMs = session.StartedAtUtc?.ToUnixTimeMilliseconds(),
            EndedAtUtcMs = session.EndedAtUtc?.ToUnixTimeMilliseconds(),
            CreatedAtUtcMs = session.CreatedAtUtc.ToUnixTimeMilliseconds(),
            UpdatedAtUtcMs = session.UpdatedAtUtc.ToUnixTimeMilliseconds(),
            RowVersion = session.RowVersion,
        };
    }

    /// <summary>Ricostruisce e valida anche dati scritti da strumenti esterni.</summary>
    public LessonSession ToDomain() => LessonSession.Restore(
        Guid.Parse(Id),
        Guid.Parse(LessonId),
        Guid.Parse(ClassroomId),
        Guid.Parse(ControllerAdminId),
        ToStatus(),
        ToAdmissionsOpen(),
        SessionRevision,
        ClassDefaultJson,
        JoinCode is null ? null : SessionJoinCode.Restore(JoinCode),
        StartedAtUtcMs is { } started ? DateTimeOffset.FromUnixTimeMilliseconds(started) : null,
        EndedAtUtcMs is { } ended ? DateTimeOffset.FromUnixTimeMilliseconds(ended) : null,
        DateTimeOffset.FromUnixTimeMilliseconds(CreatedAtUtcMs),
        DateTimeOffset.FromUnixTimeMilliseconds(UpdatedAtUtcMs),
        RowVersion);

    /// <summary>Lo stato è uno dei quattro valori di §6.1: un altro testo non è uno stato.</summary>
    private SessionStatus ToStatus() => Status switch
    {
        nameof(SessionStatus.Lobby) => SessionStatus.Lobby,
        nameof(SessionStatus.Active) => SessionStatus.Active,
        nameof(SessionStatus.Paused) => SessionStatus.Paused,
        nameof(SessionStatus.Ended) => SessionStatus.Ended,
        _ => throw new ArgumentException(
            $"Stato della sessione non riconosciuto: '{Status}'.", nameof(Status)),
    };

    /// <summary>Gli ingressi sono un flag intero: un altro valore non è uno stato.</summary>
    private bool ToAdmissionsOpen() => AdmissionsOpenFlag switch
    {
        0 => false,
        1 => true,
        _ => throw new ArgumentException(
            $"Stato degli ingressi non riconosciuto: '{AdmissionsOpenFlag}'.", nameof(AdmissionsOpenFlag)),
    };
}
