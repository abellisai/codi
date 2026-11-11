using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

/// <summary>
/// Il CRUD comune è tutto ciò che serve: l'unicità della chiave del browser è garantita
/// dall'indice <c>ux_station_browser_key</c> (§7.2) e il caso d'uso cerca la postazione per
/// impronta con un <c>QueryAsync</c> prima di crearla.
/// </summary>
internal sealed class StationRepository(DbContext context)
    : BaseRepository<StationEntity, string>(context), IStationRepository;
