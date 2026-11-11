namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Le parti proprie del tipo dichiarativo, già lette dal documento: che cosa si legge, che cosa si
/// chiede e come si confronta la risposta. I messaggi di esito sono quelli comuni a ogni tipo.
///
/// Esiste per la stessa ragione di <see cref="ExerciseDefinitionParts"/>: chi legge il documento
/// consegna ciò che il formato dichiara, e la versione del motore lo riunisce nella forma che le è
/// propria senza che il lettore debba conoscerla.
/// </summary>
public sealed record QuizDefinitionParts(
    string Stimulus,
    string Prompt,
    QuizAnswerMatch Match,
    ExerciseFeedback Feedback);
