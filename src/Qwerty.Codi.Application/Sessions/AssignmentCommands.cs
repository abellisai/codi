using Qwerty.Codi.Application.Dto;
using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Assegna una versione pubblicata di esercizio a tutta la classe o a un solo alunno (§6.4, §10.4).
/// È il comando che rende operativo il lavoro: crea l'assegnazione, un **tentativo indipendente** per
/// ogni destinatario e porta lo stato desiderato alla vista esercizio con i controlli bloccati —
/// abilitarli è una decisione successiva del docente (`EnableActivity`).
///
/// La versione si dichiara in uno dei due modi: con `exerciseVersionId`, quando il docente sceglie
/// una versione precisa, oppure con `lessonExerciseId`, quando assegna la casella della sequenza e
/// vale l'ultima versione pubblicata di quell'esercizio. Indicarli entrambi o nessuno dei due è un
/// rifiuto: l'assegnazione deve dire **quale** materiale consegna, non dedurlo.
///
/// `participantId` distingue i due ambiti di §6.5: assente significa tutta la classe ammessa,
/// presente significa quel solo alunno. Gli altri comandi di regia dichiarano la revisione attesa
/// della sessione; questo la dichiara nello stesso modo.
/// </summary>
public sealed record AssignExerciseCommand(
    Guid SessionId,
    long? ExpectedSessionRevision = null,
    Guid? LessonExerciseId = null,
    Guid? ExerciseVersionId = null,
    Guid? ParticipantId = null) : ICodiCommand<CommandAcceptedDto>, ICodiContextualRequest
{
    public static string RequestName => "AssignExercise";

    public static CodiPolicy Policy => CodiPolicy.SessionController;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}
