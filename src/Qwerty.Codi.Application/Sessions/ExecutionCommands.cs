using Qwerty.Codi.Application.Dto;
using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Avvia il progetto corrente (§6.4, §10.4): scrive una **direttiva** di avvio nello stato
/// desiderato del destinatario. Non azzera la scena e non tocca il progetto: ripartire da capo è
/// `RestartExecution`. La direttiva è applicabile una volta sola, quindi ripeterla non riavvia un
/// programma concluso (§5.2, invariante 14).
///
/// Ambito: senza <c>ParticipantId</c> vale per tutti gli alunni ammessi, con l'identificativo per il
/// solo destinatario (§6.5). Nel caso collettivo la direttiva **non** dichiara una generazione,
/// perché le generazioni dei singoli possono divergere dopo un riavvio individuale: ognuno applica
/// la direttiva alla propria.
///
/// <c>ExpectedSessionRevision</c> assente ha l'esito proprio <c>428 EntityVersionRequired</c> (§10.2).
/// </summary>
public sealed record RunProgramCommand(
    Guid SessionId,
    long? ExpectedSessionRevision = null,
    Guid? ParticipantId = null) : ICodiCommand<CommandAcceptedDto>, ICodiContextualRequest
{
    public static string RequestName => "RunProgram";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Riavvia l'esecuzione (§6.4, §10.4): **incrementa la generazione**, conserva il progetto
/// dell'alunno, azzera la scena e la fa ripartire. La generazione è ciò che distingue un riavvio
/// imposto dal server da una corsa normale (§5.3), e la direttiva di riavvio la porta con sé perché
/// una direttiva di avvio rimasta in volo non riparta su un lavoro già ricominciato.
///
/// Precondizioni: tentativo esistente, attività **abilitata** e nessuna pausa — docente o di
/// recupero (§6.4). Come per ogni comando collettivo, si verificano su tutti i destinatari prima di
/// scrivere (§6.5).
/// </summary>
public sealed record RestartExecutionCommand(
    Guid SessionId,
    long? ExpectedSessionRevision = null,
    Guid? ParticipantId = null) : ICodiCommand<CommandAcceptedDto>, ICodiContextualRequest
{
    public static string RequestName => "RestartExecution";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Azzera il lavoro di un alunno (§6.4, §10.4): **archivia** il tentativo corrente e ne crea uno
/// nuovo dal modello iniziale della versione assegnata, pronto e bloccato; il lavoro precedente
/// resta nello storico e non si riprende.
///
/// Il comando dichiara il destinatario **e il tentativo mostrato nella conferma dell'interfaccia**
/// (§10.6): se nel frattempo il lavoro è cambiato, il server lo verifica di nuovo e rifiuta con
/// `409 SessionStateConflict`, perché una conferma riferita a un lavoro diverso non autorizza
/// l'azzeramento di quello corrente. La versione attesa della riga del tentativo è la seconda
/// verifica: senza corrispondenza, `412 EntityVersionConflict`.
///
/// L'ambito è il singolo alunno: l'azzeramento si conferma su un lavoro, e una sola conferma non
/// descrive quello di dodici destinatari.
/// </summary>
public sealed record ResetWorkCommand(
    Guid SessionId,
    Guid ParticipantId,
    Guid AttemptId,
    long AttemptRowVersion,
    long? ExpectedSessionRevision = null) : ICodiCommand<CommandAcceptedDto>, ICodiContextualRequest
{
    public static string RequestName => "ResetWork";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}
