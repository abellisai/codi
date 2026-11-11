namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Bozza di autoraggio di un esercizio (§10.4, §10.5). I due documenti viaggiano come stringhe
/// JSON e non come oggetti annidati: sono documenti con una loro versione di schema, e il
/// trasporto non deve interpretarli. Chi legge li rilegge con il dominio, che è l'unico posto in
/// cui la forma di §13.2 è verificata.
///
/// La soluzione di riferimento non compare: il catalogo la riserva a <c>GetReferenceSolution</c>,
/// così il DTO che l'editor carica per comporre l'esercizio non porta con sé la risposta (§13.1).
/// </summary>
public sealed record ExerciseDraftDto(
    Guid ExerciseId,
    string DefinitionJson,
    string WorkspaceJson,
    long RowVersion,
    DateTimeOffset UpdatedAtUtc);
