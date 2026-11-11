namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// L'esercizio che l'alunno sta facendo: la versione **fissata dall'assegnazione**, con la sua
/// definizione e i metadati tecnici che il client confronta prima di caricare il progetto.
///
/// <see cref="ExerciseType"/> è il tipo di attività, e serve al client per sapere **che cosa** sta
/// per ricevere prima di interpretare la definizione: un tipo che non sa svolgere va detto al
/// bambino, non scoperto leggendo un documento che non riconosce.
/// </summary>
public sealed record AssignedExerciseDto(
    Guid SessionId,
    Guid AssignmentId,
    Guid ExerciseVersionId,
    Guid? LessonExerciseId,
    string Scope,
    Guid AttemptId,
    long AttemptNumber,
    Guid ExerciseId,
    string ExerciseTitle,
    string ExerciseType,
    string DefinitionJson,
    string EngineVersion,
    string SchemaVersion,
    string ContentHash);
