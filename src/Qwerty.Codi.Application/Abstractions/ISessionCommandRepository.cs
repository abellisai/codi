using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Comandi di regia registrati (§7, §12.1). La riga è immutabile, quindi il repository non offre
/// nulla oltre al CRUD comune: l'inserimento nella transazione del caso d'uso e le letture per
/// comando o per sessione. L'interfaccia esiste perché è il nome con cui la unità di lavoro lo
/// risolve.
/// </summary>
public interface ISessionCommandRepository : IRepository<SessionCommandEntity, string>;
