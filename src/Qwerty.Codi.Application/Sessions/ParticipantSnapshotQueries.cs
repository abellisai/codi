using Qwerty.Codi.Application.Dto;
using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Stato corrente del partecipante autenticato (§10.5, §12.4): è lo snapshot che il server invia
/// quando la sincronizzazione non è più valida — dopo un aggiornamento della pagina, dopo un
/// riavvio o quando il client dichiara di avere applicato una revisione ormai superata.
///
/// È una lettura e non un comando: non incrementa l'epoca, perché l'operatività è già stata
/// acquisita. L'identità non è un parametro: arriva dalla credenziale verificata dal trasporto
/// (§9.3), quindi nessuno può chiedere lo stato di un altro alunno.
/// </summary>
public sealed record GetParticipantSnapshotQuery : ICodiQuery<ParticipantSnapshotDto>, ICodiContextualRequest
{
    public static string RequestName => "GetParticipantSnapshot";

    public static CodiPolicy Policy => CodiPolicy.AdmittedParticipant;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Costruzione dello snapshot di un partecipante. Sta in un solo posto perché la stessa forma deve
/// uscire dall'handshake e dalla lettura dello stato corrente: due costruzioni diverse
/// produrrebbero due snapshot con lo stesso nome e contenuto diverso.
/// </summary>
internal static class ParticipantSnapshotFactory
{
    public static ParticipantSnapshotDto Create(
        IServerInstance serverInstance,
        LessonSession session,
        Guid participantId,
        long connectionEpoch,
        DateTimeOffset nowUtc,
        ParticipantStateEntity state) =>
        new(
            ParticipantProtocol.CurrentVersion,
            serverInstance.InstanceId,
            session.Id,
            participantId,
            state.EffectiveRevision,
            connectionEpoch,
            nowUtc,
            DesiredState(state));

    /// <summary>
    /// Stato desiderato del destinatario (§6.2) nella forma dell'involucro: si legge dalla sua riga,
    /// perché la vista di un alunno è la sua e non una media della classe. È anche l'unico posto in
    /// cui la **direttiva** arriva al client quando il comando era di classe: l'involucro del comando
    /// porta una sola `desiredState` per tutti, e la rifinitura per destinatario passa da qui.
    /// </summary>
    private static DesiredStateDto DesiredState(ParticipantStateEntity state) =>
        new(
            state.View,
            state.InteractionEnabledFlag != 0,
            ParseIdentifier(state.AssignmentId),
            ParseIdentifier(state.AttemptId),
            state.TeacherPausedFlag != 0,
            state.RecoveryBlockedFlag != 0,
            state.ExecutionGeneration,
            ExecutionDirectiveDto.Read(state.DirectiveJson));

    private static Guid? ParseIdentifier(string? value) =>
        Guid.TryParse(value, out var identifier) ? identifier : null;
}
