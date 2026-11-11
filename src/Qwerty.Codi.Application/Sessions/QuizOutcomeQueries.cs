using Qwerty.Codi.Application.Dto;
using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Esito della propria risposta su un esercizio dichiarativo (§10.5). La correzione la fa il
/// server: la risposta attesa vive nella soluzione privata della versione pubblicata, che non esce
/// mai verso gli alunni (§13.1), e il browser riceve soltanto «giusto» o «sbagliato».
///
/// L'identità non è un parametro: arriva dalla credenziale verificata dal trasporto (§9.3), quindi
/// nessuno può chiedere l'esito del lavoro di un altro alunno.
/// </summary>
public sealed record GetQuizOutcomeQuery : ICodiQuery<QuizOutcomeDto>, ICodiContextualRequest
{
    public static string RequestName => "GetQuizOutcome";

    public static CodiPolicy Policy => CodiPolicy.AdmittedParticipant;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Esito del proprio programma su un esercizio che conserva valori (§10.5, §13.9).
///
/// Il server **esegue** il progetto dell'alunno sui casi di prova della versione pubblicata: non
/// riceve un esito dal browser, e non si fida di quello che il browser dichiara. I casi di prova
/// escono con la definizione perché l'alunno deve sapere che cosa il programma deve fare; la
/// correzione è l'esecuzione, e non c'è nulla da nascondere.
///
/// L'identità non è un parametro: arriva dalla credenziale verificata dal trasporto (§9.3), quindi
/// nessuno può chiedere l'esito del lavoro di un altro alunno.
/// </summary>
public sealed record GetValueProgramOutcomeQuery : ICodiQuery<ValueProgramOutcomeDto>, ICodiContextualRequest
{
    public static string RequestName => "GetValueProgramOutcome";

    public static CodiPolicy Policy => CodiPolicy.AdmittedParticipant;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}
