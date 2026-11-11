using Qwerty.Codi.Application.Dto;
using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Application.Sessions;

public sealed record ReportRecoveryBlockedCommand(long ConnectionEpoch, Guid? AttemptId, long EffectiveRevision)
    : ICodiCommand<ParticipantSnapshotDto>, ICodiContextualRequest
{
    public static string RequestName => "ReportRecoveryBlocked";
    public static CodiPolicy Policy => CodiPolicy.ActiveParticipantConnection;
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;
    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Handshake applicativo del partecipante (§11.2, §10.4): il client dichiara le proprie versioni e
/// l'istanza di pagina, il server acquisisce l'operatività, incrementa l'epoca e restituisce lo
/// stato con cui il client deve sincronizzarsi.
///
/// Il comando non è la connessione: il collegamento SignalR è già aperto e autenticato quando
/// arriva, e da solo non abilita nulla (§11.2 passo 1). Ciò che si persiste è l'**epoca**: da quel
/// momento i messaggi con un'epoca precedente sono superati, e un upload che la dichiara vecchia
/// viene rifiutato (§12.5). L'associazione della connessione vive in memoria nel registro, perché
/// non sopravvive al processo che la ospita.
///
/// L'identità non è nel corpo: arriva dal contesto verificato dal trasporto (§4.4, §9.3), quindi
/// nessun corpo può far acquisire l'operatività a un altro partecipante.
/// </summary>
public sealed record InitializeParticipantConnectionCommand(
    int ProtocolVersion,
    Guid ClientInstanceId,
    string AppVersion,
    string EngineVersion) : ICodiCommand<ParticipantSnapshotDto>, ICodiContextualRequest
{
    public static string RequestName => "InitializeParticipantConnection";

    public static CodiPolicy Policy => CodiPolicy.AdmittedParticipant;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}
