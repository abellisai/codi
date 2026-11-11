namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Dati didattici del tipo <c>GridPath</c>: la scena da percorrere, la condizione che conclude
/// l'esercizio e i messaggi con cui il gioco risponde al bambino. Sono i tre dati che la definizione
/// di §13.2 dichiara accanto ai campi comuni, e vivono insieme perché appartengono allo stesso tipo.
///
/// È una classe e non un record con uguaglianza per valore: contiene la scena, che confronta le
/// celle per identità, e un'uguaglianza strutturale qui prometterebbe qualcosa che i suoi membri
/// non mantengono.
/// </summary>
public sealed class GridPathData(
    ExerciseScene scene,
    ExerciseCompletion completion,
    ExerciseFeedback feedback) : IExerciseTypeData
{
    /// <summary>Scena: griglia, partenza, ostacoli e traguardo.</summary>
    public ExerciseScene Scene { get; } = scene;

    /// <summary>Condizione che conclude l'esercizio.</summary>
    public ExerciseCompletion Completion { get; } = completion;

    /// <summary>Messaggi di esito mostrati al bambino.</summary>
    public ExerciseFeedback Feedback { get; } = feedback;
}
