using RepoDb;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

/// <summary>
/// Il CRUD comune è tutto ciò che serve: l'unicità della sessione non terminata per classe è
/// garantita dall'indice parziale <c>ux_session_one_open_per_classroom</c> (§7.6) e il caso d'uso
/// verifica l'esistenza con una query prima di inserire.
/// </summary>
internal sealed class LessonSessionRepository : BaseRepository<LessonSessionEntity, string>, ILessonSessionRepository
{
    private readonly DbContext _context;
    public LessonSessionRepository(DbContext context) : base(context) => _context = context;
    public async Task<bool> CompleteClassDefaultAsync(string id, long rowVersion, string json, CancellationToken cancellationToken = default)
    {
        var row = new LessonSessionEntity { Id = id, RowVersion = rowVersion, ClassDefaultJson = json };
        return await _context.Connection.UpdateAsync(row, where: entity => entity.Id == id && entity.RowVersion == rowVersion,
            fields: Field.Parse<LessonSessionEntity>(entity => new { entity.ClassDefaultJson }),
            transaction: _context.Transaction!, cancellationToken: cancellationToken).ConfigureAwait(false) == 1;
    }
}
