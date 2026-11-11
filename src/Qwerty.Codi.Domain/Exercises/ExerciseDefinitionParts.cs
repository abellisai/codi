namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Le parti dichiarate da un documento di definizione (§13.2), già lette e ancora da riunire nella
/// forma propria del tipo. Il documento contiene questi pezzi — scena, condizione di completamento e
/// messaggi di esito — e la versione del motore è l'unica a sapere che cosa farne.
///
/// Esiste per tenere il lettore del documento generico: legge ciò che il formato dichiara, senza
/// sapere quale tipo lo userà.
/// </summary>
public sealed record ExerciseDefinitionParts(
    ExerciseScene Scene,
    ExerciseCompletion Completion,
    ExerciseFeedback Feedback);
