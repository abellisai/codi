using Microsoft.Data.Sqlite;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Admin;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;
using RepoDb;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

/// <summary>Al CRUD comune aggiunge soltanto gli esiti specifici dell'accesso amministrativo.</summary>
internal sealed class AdminUserRepository(DbContext context)
    : BaseRepository<AdminUserEntity, string>(context), IAdminUserRepository
{
    public async Task<bool> TryInsertAsync(AdminUser user, CancellationToken cancellationToken)
    {
        try
        {
            await InsertAsync(AdminUserEntity.FromDomain(user), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SqliteException exception) when (
            exception.SqliteErrorCode == 19 && exception.SqliteExtendedErrorCode is 2067 or 1555)
        {
            // Solo nome o PK già presenti sono conflitti previsti; gli altri vincoli restano errori.
            return false;
        }
    }

    public async Task<AdminUser?> TryUpdateAsync(AdminUser user, CancellationToken cancellationToken)
    {
        var row = AdminUserEntity.FromDomain(user);
        var fields = Field.Parse<AdminUserEntity>(entity => new
        {
            entity.FailedLoginAttempts,
            entity.LockedUntilUtcMs,
            entity.UpdatedAtUtcMs,
            entity.RowVersion,
        });
        return await UpdateFieldsAsync(row, fields, extraPredicate: null, cancellationToken).ConfigureAwait(false)
            ? user.WithRowVersion(row.RowVersion) : null;
    }
}
