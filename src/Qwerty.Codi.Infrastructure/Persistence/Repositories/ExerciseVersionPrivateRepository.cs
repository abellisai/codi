using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;
using RepoDb;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

/// <summary>
/// Contenuti privati delle versioni di esercizio (§7.3): il CRUD comune. La chiave primaria è
/// l'<c>exercise_version_id</c>, così <c>GetAsync</c> legge la riga compagna di una versione. I
/// contenuti privati restano in questa tabella separata e non compaiono fra le colonne della
/// versione, coerentemente con il divieto di esporli nei DTO degli alunni.
/// </summary>
internal sealed class ExerciseVersionPrivateRepository : BaseRepository<ExerciseVersionPrivateEntity, string>, IExerciseVersionPrivateRepository
{
    // Costruttore esplicito: il contesto serve sia alla base sia alla cancellazione per predicato,
    // e la cattura da un costruttore primario genererebbe CS9107.
    private readonly DbContext _context;

    public ExerciseVersionPrivateRepository(DbContext context)
        : base(context) => _context = context;

    public async Task<bool> DeleteByVersionAsync(
        string versionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        cancellationToken.ThrowIfCancellationRequested();

        return await _context.Connection
            .DeleteAsync<ExerciseVersionPrivateEntity>(
                row => row.Id == versionId,
                transaction: _context.Transaction!,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false) == 1;
    }
}
