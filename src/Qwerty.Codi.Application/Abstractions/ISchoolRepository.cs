using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Organization;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>Filtri tramite QueryAsync/ExistsAsync; metodi specializzati solo per esiti del dominio.</summary>
public interface ISchoolRepository : IRepository<SchoolEntity, string>
{
    /// <summary>Falso se il nome normalizzato è già in uso; gli altri vincoli sono errori.</summary>
    Task<bool> TryInsertAsync(School school, CancellationToken cancellationToken);

    /// <summary>Persiste nome e archiviazione con controllo di versione; null se la versione letta è obsoleta.</summary>
    Task<School?> TryUpdateAsync(School school, CancellationToken cancellationToken);
}
