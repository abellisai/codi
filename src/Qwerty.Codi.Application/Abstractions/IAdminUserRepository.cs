using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Admin;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>Filtri tramite QueryAsync/ExistsAsync; metodi specializzati solo per esiti del dominio.</summary>
public interface IAdminUserRepository : IRepository<AdminUserEntity, string>
{
    /// <summary>Falso se PK o nome sono già presenti; gli altri vincoli sono errori.</summary>
    Task<bool> TryInsertAsync(AdminUser user, CancellationToken cancellationToken);
    /// <summary>Persiste solo lo stato di accesso; null se la versione letta è obsoleta.</summary>
    Task<AdminUser?> TryUpdateAsync(AdminUser user, CancellationToken cancellationToken);
}
