using Qwerty.Codi.Application.Dto;
using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Anteprima del progetto pubblicata dal player (§15.5). Non è un salvataggio: non tocca il
/// database e non sostituisce il progetto persistito. Aggiorna una cache in memoria, con scadenza,
/// che il docente legge per seguire i blocchi dell'alunno senza aprire un editor per ognuno
/// (§14.3).
///
/// L'identità non è nel corpo: arriva dal contesto verificato dal trasporto (§4.4, §9.3). Il
/// tentativo, la generazione e l'epoca dichiarati devono corrispondere allo stato corrente, così
/// un'anteprima in ritardo non descrive un lavoro che non è più quello.
/// </summary>
public sealed record PublishWorkspacePreviewCommand(Guid AttemptId, long ConnectionEpoch,
    long ExecutionGeneration, long ProjectRevision, string WorkspaceHash, string WorkspaceJson)
    : ICodiCommand<WorkspacePreviewDto>, ICodiContextualRequest
{
    public static string RequestName => "PublishWorkspacePreview";

    public static CodiPolicy Policy => CodiPolicy.ActiveParticipantConnection;

    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Dati dichiarati dal client al metodo hub <c>WorkspacePreviewUpdated</c> (§11.3, §15.5): la forma
/// compatta dell'anteprima, entro il limite SignalR.
/// </summary>
public sealed record WorkspacePreviewArguments(int ProtocolVersion, Guid AttemptId, long ConnectionEpoch,
    long ExecutionGeneration, long ProjectRevision, string WorkspaceHash, string WorkspaceJson);
