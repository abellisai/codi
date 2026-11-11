using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Contenuti privati delle versioni di esercizio (§7.3): soluzione di riferimento e note
/// dell'autore, riservati al docente. L'identificativo è l'<c>exercise_version_id</c>, quindi la
/// lettura è <c>GetAsync</c> con quell'identificativo. L'interfaccia esiste perché è il nome con cui
/// la unit of work risolve il repository.
/// </summary>
public interface IExerciseVersionPrivateRepository : IRepository<ExerciseVersionPrivateEntity, string>
{
    /// <summary>
    /// Elimina il completamento privato di una versione, quando esiste. La riga nasce e muore con la
    /// versione: eliminare la versione senza di lei lascerebbe una soluzione orfana.
    /// </summary>
    Task<bool> DeleteByVersionAsync(string versionId, CancellationToken cancellationToken = default);
}
