using Qwerty.Codi.Application.Dto;
using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Griglia dei partecipanti di una sessione (§10.5): chi c'è, a che punto è la sua connessione e
/// che cosa il server si aspetta da lui. È la lettura con cui il docente guarda la classe mentre
/// conduce la lezione.
///
/// «Ultimo stato osservato» qui è **presenza e stato desiderato**: la telemetria del player
/// (`ActivityStateUpdated`, §6.3, §11.3) non è persistita, quindi ciò che il browser esegue davvero
/// non è in questa lettura e la specifica non lo inventa.
/// </summary>
public sealed record ListSessionParticipantsQuery(Guid SessionId)
    : ICodiQuery<IReadOnlyList<SessionParticipantOverviewDto>>, ICodiContextualRequest
{
    public static string RequestName => "ListSessionParticipants";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

public sealed record GetSessionOverviewQuery(Guid SessionId)
    : ICodiQuery<TeacherSnapshotDto>, ICodiContextualRequest
{
    public static string RequestName => "GetSessionOverview";
    public static CodiPolicy Policy => CodiPolicy.SessionController;
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;
    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Dettaglio del singolo alunno (§10.5): la riga della griglia più il materiale assegnato e il
/// lavoro corrente, con lo **storico dei tentativi** contato perché un azzeramento si vede dalla
/// numerazione (§7.3).
///
/// Il progetto dell'alunno è incluso: il docente è il proprietario della sessione e il lavoro è
/// della sua classe. Il checkpoint invece è dichiarato presente e datato ma non riportato per intero:
/// serve a sapere che esiste una posizione ripristinabile, e il contenuto è del player.
/// </summary>
public sealed record GetParticipantDetailQuery(Guid SessionId, Guid ParticipantId)
    : ICodiQuery<SessionParticipantDetailDto>, ICodiContextualRequest
{
    public static string RequestName => "GetParticipantDetail";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}
