using Qwerty.Codi.Domain.Sessions;
namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Sessione come la vede il client (§10.5). Espone stato e revisione correnti della sessione senza
/// dati dei partecipanti; <c>sessionRevision</c> e <c>rowVersion</c> viaggiano come stringhe
/// decimali (§10.2), come gli altri contatori a 64 bit del contratto.
///
/// <c>joinCode</c> è il codice di ingresso che il docente detta a voce (§9.4): è presente in questa
/// risposta — che appartiene all'area admin, protetta dalla policy di regia — e **non** nelle
/// letture pubbliche di scoperta, che non devono rivelare nulla di una sessione a chi non è entrato.
/// È nullo in attesa e per le sessioni avviate prima dell'introduzione del codice.
/// </summary>
public record SessionDto(
    Guid Id,
    Guid LessonId,
    Guid ClassroomId,
    string Status,
    bool AdmissionsOpen,
    string? JoinCode,
    long SessionRevision,
    DateTimeOffset? StartedAtUtc,
    long RowVersion)
{
    public static SessionDto FromDomain(LessonSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new SessionDto(
            session.Id,
            session.LessonId,
            session.ClassroomId,
            session.Status.ToString(),
            session.AdmissionsOpen,
            session.JoinCode?.Value,
            session.SessionRevision,
            session.StartedAtUtc,
            session.RowVersion);
    }
}
