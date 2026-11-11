using Qwerty.Codi.Application.Dto;
using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Contesto del partecipante autenticato (§10.5): la sua partecipazione, lo stato di ammissione e
/// la vista che il server vuole per lui. È la lettura con cui la pagina degli alunni capisce chi è
/// e che cosa mostrare dopo un aggiornamento della pagina, senza fidarsi di quanto ha in memoria.
///
/// L'identità non è un parametro: arriva dalla credenziale verificata dal trasporto (§9.3), quindi
/// un partecipante non può chiedere il contesto di un altro.
/// </summary>
public sealed record GetParticipantContextQuery : ICodiQuery<ParticipantContextDto>, ICodiContextualRequest
{
    public static string RequestName => "GetParticipantContext";

    public static CodiPolicy Policy => CodiPolicy.AdmittedParticipant;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}
