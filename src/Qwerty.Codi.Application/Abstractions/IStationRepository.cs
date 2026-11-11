using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Postazioni (§7.2). Non aggiunge nulla al CRUD comune: l'unicità della chiave del browser è
/// garantita dall'indice <c>ux_station_browser_key</c> e il caso d'uso risolve la postazione
/// cercandola per impronta. L'interfaccia esiste perché è il nome con cui la unit of work risolve
/// il repository.
/// </summary>
public interface IStationRepository : IRepository<StationEntity, string>;
