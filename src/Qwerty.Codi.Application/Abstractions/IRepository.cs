using System.Linq.Expressions;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>Identifica i repository risolvibili da un'unità di lavoro.</summary>
public interface IRepository { }

/// <summary>
/// CRUD e filtri sulle entità di persistenza, senza dipendenze da RepoDB o ADO.NET.
/// Update e Delete confrontano la versione letta; un conflitto restituisce falso.
/// Nessuna operazione apre una connessione o conferma la transazione del chiamante.
/// </summary>
public interface IRepository<TEntity, TIdentity> : IRepository
    where TEntity : class, IPersistedEntity<TIdentity>
{
    Task<TEntity?> GetAsync(TIdentity id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TEntity>> QueryAsync(
        Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(
        Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    Task<TIdentity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(TEntity entity, CancellationToken cancellationToken = default);
}

/// <summary>Identificativo e versione della riga secondo §7.6; la versione iniziale è uno.</summary>
public interface IPersistedEntity<TIdentity>
{
    TIdentity Id { get; set; }
    long RowVersion { get; set; }
}
