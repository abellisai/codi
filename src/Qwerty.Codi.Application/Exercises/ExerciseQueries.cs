using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;

namespace Qwerty.Codi.Application.Exercises;

/// <summary>Dettaglio di un esercizio, compresi quelli archiviati (§10.5).</summary>
public sealed record GetExerciseQuery(Guid Id) : ICodiQuery<ExerciseDto>
{
    public static string RequestName => "GetExercise";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>Elenco degli esercizi in ordine di titolo (§10.5).</summary>
public sealed record ListExercisesQuery(bool IncludeArchived = false) : ICodiQuery<IReadOnlyList<ExerciseDto>>
{
    public static string RequestName => "ListExercises";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>
/// Bozza di un esercizio per l'editor (§10.5). Una bozza che non esiste è un <c>404</c>: la
/// definizione iniziale proposta da un editor vuoto è una scelta di interfaccia, non del server.
/// La soluzione di riferimento non è compresa: il catalogo la riserva a <c>GetReferenceSolution</c>.
/// </summary>
public sealed record GetExerciseDraftQuery(Guid ExerciseId) : ICodiQuery<ExerciseDraftDto>
{
    public static string RequestName => "GetExerciseDraft";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>
/// Soluzione di riferimento (§10.5): il progetto che risolve l'esercizio, riservato al docente e
/// mai incluso nei DTO degli alunni (§13.1). Senza <c>VersionNumber</c> legge la bozza — l'assenza
/// della bozza è un <c>404</c>, una bozza senza soluzione risponde con un valore nullo. Con
/// <c>VersionNumber</c> legge la snapshot congelata nella versione pubblicata (0013): una versione
/// restituisce la propria soluzione, non quella corrente della bozza; l'assenza della versione è
/// un <c>404</c>, una versione senza soluzione risponde con un valore nullo.
/// </summary>
public sealed record GetReferenceSolutionQuery(
    Guid ExerciseId,
    long? VersionNumber = null) : ICodiQuery<ExerciseReferenceSolutionDto>
{
    public static string RequestName => "GetReferenceSolution";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>
/// Versioni pubblicate di un esercizio (§10.5), dalla più recente alla prima. Sono immutabili: una
/// versione pubblicata non si aggiorna, e la sequenza di una lezione la riferisce per numero.
/// </summary>
public sealed record ListExerciseVersionsQuery(Guid ExerciseId) : ICodiQuery<IReadOnlyList<ExerciseVersionDto>>
{
    public static string RequestName => "ListExerciseVersions";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>Dettaglio di una versione pubblicata, indicata dal suo numero per esercizio (§10.5).</summary>
public sealed record GetExerciseVersionQuery(
    Guid ExerciseId,
    long VersionNumber) : ICodiQuery<ExerciseVersionDto>
{
    public static string RequestName => "GetExerciseVersion";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}
