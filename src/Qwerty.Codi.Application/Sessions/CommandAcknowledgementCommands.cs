using Qwerty.Codi.Application.Dto;
using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Esiti ammessi di una conferma applicativa (§12.2). Sono il valore che il client dichiara
/// nell'involucro dell'ACK: il server li traduce nello stato della consegna, non li interpreta come
/// testo libero.
/// </summary>
public static class CommandAcknowledgementOutcomes
{
    /// <summary>Il client ha applicato lo stato e ne è certo.</summary>
    public const string Applied = "Applied";

    /// <summary>Il client ha applicato lo stato ma non è certo di ciò che ha eseguito: serve una riconciliazione.</summary>
    public const string AppliedWithRecoveryRequired = "AppliedWithRecoveryRequired";

    /// <summary>Il client ha rifiutato il comando perché lo considera superato.</summary>
    public const string RejectedStale = "RejectedStale";

    /// <summary>Il client non è riuscito ad applicare lo stato.</summary>
    public const string Failed = "Failed";
}

/// <summary>
/// Conferma applicativa di un comando consegnato (§12.2, §10.4). Non è «il comando è arrivato»: è
/// «il client ha applicato questo stato», e il server la registra sulla consegna del destinatario.
/// L'arrivo del messaggio, da solo, non prova nulla: la consegna resta senza conferma proprio perché
/// inviare non è applicare.
///
/// Il comando è ammesso a una **connessione operativa** e non alla sola partecipazione: una conferma
/// può arrivare anche su una lezione appena terminata, perché il client deve poter dire che cosa ha
/// applicato prima che la sessione si chiudesse (A-04, §12.7). L'**epoca** dichiarata deve essere
/// quella corrente: una pagina superata non riabilita la propria consegna scrivendo da una
/// connessione vecchia (§12.5).
/// </summary>
public sealed record AcknowledgeCommandCommand(
    Guid CommandId,
    int ProtocolVersion,
    long EffectiveRevision,
    long ConnectionEpoch,
    string Outcome,
    string? RuntimeStatus = null,
    long? LastPersistedProjectRevision = null,
    string? ErrorCode = null) : ICodiCommand<CommandAcknowledgementDto>, ICodiContextualRequest
{
    public static string RequestName => "AcknowledgeCommand";

    public static CodiPolicy Policy => CodiPolicy.ActiveParticipantConnection;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>Dati dell'ACK che il client invia sull'hub (§11.3, §11.4).</summary>
public sealed record CommandAcknowledgementArguments(
    Guid CommandId,
    int ProtocolVersion,
    long EffectiveRevision,
    long ConnectionEpoch,
    string Outcome,
    string? RuntimeStatus,
    long? LastPersistedProjectRevision,
    string? ErrorCode);
