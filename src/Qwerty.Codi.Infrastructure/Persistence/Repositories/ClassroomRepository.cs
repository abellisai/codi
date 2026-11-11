using Microsoft.Data.Sqlite;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Organization;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

/// <summary>Al CRUD comune aggiunge soltanto l'esito di conflitto previsto dell'inserimento.</summary>
internal sealed class ClassroomRepository(DbContext context)
    : BaseRepository<ClassroomEntity, string>(context), IClassroomRepository
{
    public async Task<bool> TryInsertAsync(Classroom classroom, CancellationToken cancellationToken)
    {
        try
        {
            await InsertAsync(ClassroomEntity.FromDomain(classroom), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SqliteException exception) when (
            exception.SqliteErrorCode == 19 && exception.SqliteExtendedErrorCode is 2067 or 1555)
        {
            // Solo nome e anno già usati nella scuola (o PK già presente) sono conflitti previsti.
            // Una chiave esterna violata resta un errore: significa che la scuola non esiste.
            return false;
        }
    }
}
