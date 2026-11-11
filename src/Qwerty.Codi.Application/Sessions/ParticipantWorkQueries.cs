using Qwerty.Codi.Application.Dto;
using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Definizione pubblica dell'attività assegnata al partecipante autenticato (§10.5, §11.2 passo 5):
/// è con questa lettura che il player carica la definizione dell'esercizio, e con `GetAttempt` il
/// progetto su cui lavorare.
///
/// L'identità non è un parametro: arriva dalla credenziale verificata dal trasporto (§9.3), quindi
/// nessuno può chiedere l'esercizio di un altro alunno. La soluzione di riferimento **non** è qui:
/// vive in `exercise_version_private` ed esce solo dalla lettura del docente (§13.1).
/// </summary>
public sealed record GetAssignedExerciseQuery : ICodiQuery<AssignedExerciseDto>, ICodiContextualRequest
{
    public static string RequestName => "GetAssignedExercise";

    public static CodiPolicy Policy => CodiPolicy.AdmittedParticipant;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Progetto e checkpoint del proprio tentativo (§10.5): è il secondo caricamento del player
/// (§11.2 passo 5) e la sorgente del lavoro da riprendere. Il tentativo è quello **corrente** dello
/// stato desiderato: se un azzeramento ne ha creato uno nuovo, questa lettura porta il nuovo.
/// </summary>
public sealed record GetAttemptQuery : ICodiQuery<AttemptDto>, ICodiContextualRequest
{
    public static string RequestName => "GetAttempt";

    public static CodiPolicy Policy => CodiPolicy.AdmittedParticipant;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}
