using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Messaggio scritto dal docente nella chat del singolo alunno (§8, §15.1). La chat non è un comando
/// di regia: non cambia lo stato desiderato, non produce consegne né ACK, e non si persiste.
///
/// Il partecipante è un parametro perché il docente scrive verso un alunno; la sessione arriva dalla
/// credenziale verificata e serve a controllare la proprietà (§6.4).
/// </summary>
public sealed record PostTeacherMessageCommand(
    Guid SessionId,
    Guid ParticipantId,
    string Text,
    /// <summary>Chiave dichiarata dal client: una ripetizione con la stessa chiave non duplica il messaggio (§12.2).</summary>
    string ClientMessageKey)
    : ICodiCommand<ParticipantMessageDto>, ICodiContextualRequest
{
    public static string RequestName => "PostTeacherMessage";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Messaggio scritto dall'alunno nella propria chat (§8, §15.1). Né la sessione né il partecipante
/// sono parametri: l'identità arriva dalla credenziale verificata, e la sessione si ricava dalla
/// connessione operativa (§9.3). Un alunno non può scrivere verso un altro alunno perché non esiste
/// una chat fra bambini.
/// </summary>
public sealed record PostParticipantMessageCommand(
    string Text,
    /// <inheritdoc cref="PostTeacherMessageCommand.ClientMessageKey"/>
    string ClientMessageKey)
    : ICodiCommand<ParticipantMessageDto>, ICodiContextualRequest
{
    public static string RequestName => "PostParticipantMessage";

    public static CodiPolicy Policy => CodiPolicy.ActiveParticipantConnection;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}
