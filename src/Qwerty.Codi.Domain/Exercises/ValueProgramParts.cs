namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Le parti dichiarate da un documento di definizione di un esercizio che conserva valori (§13.9):
/// le variabili, i casi di prova e i messaggi di esito, già letti e ancora da riunire nella forma
/// propria del tipo.
///
/// Esiste per tenere il lettore del documento generico: legge ciò che il formato dichiara — qui
/// variabili e casi di prova al posto di scena e completamento — senza sapere quale tipo lo userà.
/// </summary>
public sealed record ValueProgramParts(
    IReadOnlyList<ValueVariable> Variables,
    IReadOnlyList<ValueTestCase> TestCases,
    ExerciseFeedback Feedback);
