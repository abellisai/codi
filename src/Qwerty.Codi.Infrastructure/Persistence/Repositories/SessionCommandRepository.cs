using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

/// <summary>
/// Il CRUD comune è tutto ciò che serve: il comando si legge per identificativo o con un predicato
/// sulle sue colonne mappate, e la riga è immutabile, quindi non esiste un aggiornamento con
/// controllo di versione da usare.
/// </summary>
internal sealed class SessionCommandRepository(DbContext context)
    : BaseRepository<SessionCommandEntity, string>(context), ISessionCommandRepository;
