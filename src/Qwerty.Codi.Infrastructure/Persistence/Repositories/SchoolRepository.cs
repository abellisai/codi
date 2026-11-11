using Microsoft.Data.Sqlite;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Organization;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;
using RepoDb;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

/// <summary>Al CRUD comune aggiunge soltanto gli esiti specifici dell'organizzazione.</summary>
internal sealed class SchoolRepository(DbContext context)
    : BaseRepository<SchoolEntity, string>(context), ISchoolRepository
{
    public async Task<bool> TryInsertAsync(School school, CancellationToken cancellationToken)
    {
        try
        {
            await InsertAsync(SchoolEntity.FromDomain(school), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SqliteException exception) when (
            exception.SqliteErrorCode == 19 && exception.SqliteExtendedErrorCode is 2067 or 1555)
        {
            // Solo nome normalizzato o PK già presenti sono conflitti previsti; gli altri
            // vincoli restano errori, perché segnalano un dato fuori dalle regole del dominio.
            return false;
        }
    }

    public async Task<School?> TryUpdateAsync(School school, CancellationToken cancellationToken)
    {
        var row = SchoolEntity.FromDomain(school);
        var fields = Field.Parse<SchoolEntity>(entity => new
        {
            entity.Name,
            entity.NormalizedName,
            entity.IsArchivedFlag,
            entity.UpdatedAtUtcMs,
            entity.RowVersion,
        });
        return await UpdateFieldsAsync(row, fields, extraPredicate: null, cancellationToken).ConfigureAwait(false)
            ? school.WithRowVersion(row.RowVersion) : null;
    }
}
