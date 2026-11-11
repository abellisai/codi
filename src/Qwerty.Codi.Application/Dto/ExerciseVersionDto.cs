using Qwerty.Codi.Application.Exercises;
using Qwerty.Codi.Application.Persistence;
namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Versione pubblicata di un esercizio (§10.4, §10.5, §13.8). È immutabile: una versione non si
/// aggiorna, si sostituisce con una nuova, e il numero è progressivo per esercizio.
///
/// Il documento della definizione e il progetto iniziale escono così come sono stati pubblicati:
/// una versione è la fotografia di ciò che il docente ha verificato, e rileggerla in una forma
/// diversa significherebbe distribuire qualcosa che nessuno ha visto. Le <see cref="AuthorNotes"/> —
/// il promemoria con cui il docente accompagna la versione — arrivano dalla riga compagna e
/// restano una lettura riservata all'area admin (§13.1). La soluzione privata non compare: vive
/// nella stessa tabella e il catalogo la riserva a <c>GetReferenceSolution</c>.
///
/// <see cref="ExerciseType"/> non ha una colonna propria: vive dentro il documento della
/// definizione, che è l'unica fonte del tipo, e si legge da lì. È esposto perché chi riproduce
/// l'anteprima di una versione pubblicata deve usare **il suo** motore e non quello corrente.
/// </summary>
public sealed record ExerciseVersionDto(
    Guid Id,
    Guid ExerciseId,
    long VersionNumber,
    string DefinitionJson,
    string WorkspaceJson,
    string ExerciseType,
    string EngineVersion,
    string SchemaVersion,
    string ContentHash,
    DateTimeOffset CreatedAt,
    string? AuthorNotes)
{
    public static ExerciseVersionDto FromEntity(
        ExerciseVersionEntity entity,
        string? authorNotes = null)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return new ExerciseVersionDto(
            Guid.Parse(entity.Id),
            Guid.Parse(entity.ExerciseId),
            entity.VersionNumber,
            entity.DefinitionJson,
            entity.InitialWorkspaceJson,
            ExerciseDefinitionMetadata.ResolveExerciseType(entity.DefinitionJson) ?? string.Empty,
            entity.EngineVersion,
            entity.SchemaVersion,
            entity.ContentHash,
            DateTimeOffset.FromUnixTimeMilliseconds(entity.CreatedAtUtcMs),
            authorNotes);

        // `PublishedByAdminId` non esce: identifica l'autore della pubblicazione, non il contenuto
        // della versione, e il client non ne ha bisogno per usarla.
    }
}
