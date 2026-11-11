using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;
using RepoDb;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

/// <summary>
/// Versioni pubblicate degli esercizi (§7.3): il CRUD comune per leggere e inserire. Le versioni di
/// un esercizio si filtrano dal chiamante con <c>QueryAsync</c> su <c>ExerciseId</c>.
///
/// La riga è immutabile: la tabella non ha <c>row_version</c> e un trigger rifiuta l'UPDATE, quindi
/// <c>UpdateAsync</c> ereditato dal contratto generico non è un percorso supportato — non esiste
/// un'operazione di modifica di una versione pubblicata, che si sostituisce con una nuova.
/// </summary>
internal sealed class ExerciseVersionRepository : BaseRepository<ExerciseVersionEntity, string>, IExerciseVersionRepository
{
    // Costruttore esplicito: il contesto serve sia alla base sia alla cancellazione per predicato,
    // e la cattura da un costruttore primario genererebbe CS9107.
    private readonly DbContext _context;

    public ExerciseVersionRepository(DbContext context)
        : base(context) => _context = context;

    public async Task<bool> DeletePublishedAsync(
        ExerciseVersionEntity version,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        cancellationToken.ThrowIfCancellationRequested();

        // Predicato sull'entità mappata: la traduzione della colonna la fa il mapping, come negli
        // altri repository che non possono usare il controllo di versione.
        return await _context.Connection
            .DeleteAsync<ExerciseVersionEntity>(
                row => row.Id == version.Id,
                transaction: _context.Transaction!,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false) == 1;
    }
}
