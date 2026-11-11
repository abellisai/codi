using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Versioni pubblicate degli esercizi (§7.3). Il CRUD comune basta per leggere e inserire: le
/// versioni di un esercizio si filtrano con <c>QueryAsync</c> su <c>ExerciseId</c>, così il numero
/// massimo pubblicato si ricava dai dati senza un metodo dedicato.
///
/// Una versione pubblicata è immutabile: non esiste una colonna <c>row_version</c> né un trigger
/// che consenta l'UPDATE, quindi <c>UpdateAsync</c> — presente solo perché ereditato dal contratto
/// generico — non è un percorso supportato. Una versione nuova si pubblica con <c>InsertAsync</c>.
/// </summary>
public interface IExerciseVersionRepository : IRepository<ExerciseVersionEntity, string>
{
    /// <summary>
    /// Elimina una versione pubblicata che il caso d'uso ha già verificato come non più recente e
    /// non assegnata (§13.8). La riga è immutabile e non ha <c>row_version</c>, quindi la condizione
    /// è l'identificativo: il controllo di versione del CRUD comune cercherebbe una colonna che non
    /// esiste. Falso quando la riga non c'è più — qualcuno l'ha già eliminata.
    /// </summary>
    Task<bool> DeletePublishedAsync(ExerciseVersionEntity version, CancellationToken cancellationToken = default);
}
