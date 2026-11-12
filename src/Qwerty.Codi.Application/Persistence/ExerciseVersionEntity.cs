using Qwerty.Codi.Application.Abstractions;

namespace Qwerty.Codi.Application.Persistence;

/// <summary>
/// Entità di persistenza di <c>exercise_version</c> (§7.3): una versione pubblicata di un
/// esercizio, immutabile. Il numero è unico per esercizio; la riga non ha <c>updated_at_utc_ms</c>
/// né <c>row_version</c>, perché una versione pubblicata si sostituisce con una nuova, non si
/// aggiorna.
///
/// <see cref="RowVersion"/> esiste perché <see cref="IPersistedEntity{TIdentity}"/> lo richiede,
/// ma non è mappato ad alcuna colonna e non è un token di concorrenza: vale sempre
/// <see cref="ImmutableRowVersion"/> e non esiste un percorso di aggiornamento della riga.
/// </summary>
public sealed class ExerciseVersionEntity : IPersistedEntity<string>
{
    /// <summary>Valore costante esposto da <see cref="RowVersion"/>: la riga non si aggiorna.</summary>
    public const long ImmutableRowVersion = 1;

    public string Id { get; set; } = string.Empty;
    public string ExerciseId { get; set; } = string.Empty;
    public long VersionNumber { get; set; }
    public string DefinitionJson { get; set; } = string.Empty;
    public string InitialWorkspaceJson { get; set; } = string.Empty;
    public string EngineVersion { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    /// <summary>
    /// Impronta della soluzione di riferimento congelata nella riga compagna privata (0013).
    /// <see langword="null"/> per le versioni pubblicate prima della migrazione, che non la
    /// registrarono mai.
    /// </summary>
    public string? ReferenceHash { get; set; }
    public string PublishedByAdminId { get; set; } = string.Empty;
    public long CreatedAtUtcMs { get; set; }

    /// <summary>Non mappato: la tabella non ha una colonna <c>row_version</c>.</summary>
    public long RowVersion { get; set; } = ImmutableRowVersion;
}
