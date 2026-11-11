using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

/// <summary>
/// Il CRUD comune è tutto ciò che serve: la chiave primaria è l'identificativo del partecipante,
/// quindi lo stato di un partecipante si legge per identificativo e si aggiorna con il controllo
/// della versione di riga.
/// </summary>
internal sealed class ParticipantStateRepository(DbContext context)
    : BaseRepository<ParticipantStateEntity, string>(context), IParticipantStateRepository;
