using Qwerty.Codi.Application.Dto;
using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Abilita l'attività assegnata (§6.4, §6.5, §10.4): accende i controlli locali del lavoro e
/// consente la prova, **senza avviare** il programma — quello lo chiede `RunProgram`. Il tentativo
/// resta quello creato dall'assegnazione: cambiare permesso non ricrea il lavoro dell'alunno.
///
/// Ambito: senza <c>ParticipantId</c> vale per tutti gli alunni ammessi della sessione, con
/// l'identificativo vale per il solo destinatario (§6.5). In entrambi i casi il comando è rifiutato
/// se un destinatario non ha un tentativo, se il docente lo ha messo in pausa o se la sua vista non
/// è quella dell'esercizio: §6.5 vieta l'applicazione parziale implicita, quindi le precondizioni
/// si verificano **prima** di scrivere.
///
/// <c>ExpectedSessionRevision</c> è la revisione letta dal docente (§6.4): la sua assenza ha
/// l'esito proprio <c>428 EntityVersionRequired</c> di §10.2. Il proprietario della sessione è
/// quello della credenziale verificata dal trasporto, non un identificativo del payload.
/// </summary>
public sealed record EnableActivityCommand(
    Guid SessionId,
    long? ExpectedSessionRevision = null,
    Guid? ParticipantId = null) : ICodiCommand<CommandAcceptedDto>, ICodiContextualRequest
{
    public static string RequestName => "EnableActivity";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}
