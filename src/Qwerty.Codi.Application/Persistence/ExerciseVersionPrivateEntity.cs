using Qwerty.Codi.Application.Abstractions;

namespace Qwerty.Codi.Application.Persistence;

/// <summary>
/// Entità di persistenza di <c>exercise_version_private</c> (§7.3): i contenuti di una versione
/// riservati al docente — la soluzione di riferimento e le note dell'autore — mai inclusi nei DTO
/// degli alunni. L'identificativo è l'<c>exercise_version_id</c>, quindi la relazione con la
/// versione è uno-a-uno. La riga non ha <c>row_version</c> né istanti: nasce e muore con la
/// versione.
///
/// <see cref="RowVersion"/> esiste perché <see cref="IPersistedEntity{TIdentity}"/> lo richiede,
/// ma non è mappato ad alcuna colonna e vale sempre <see cref="ImmutableRowVersion"/>.
/// </summary>
public sealed class ExerciseVersionPrivateEntity : IPersistedEntity<string>
{
    /// <summary>Valore costante esposto da <see cref="RowVersion"/>: la riga non si aggiorna.</summary>
    public const long ImmutableRowVersion = 1;

    public string Id { get; set; } = string.Empty;
    public string? ReferenceSolutionJson { get; set; }
    public string? AuthorNotes { get; set; }

    /// <summary>Non mappato: la tabella non ha una colonna <c>row_version</c>.</summary>
    public long RowVersion { get; set; } = ImmutableRowVersion;
}
