using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Sessions;

namespace Qwerty.Codi.Application.Abstractions;

public interface IParticipantObservationCache
{
    bool Update(ParticipantObservationDto observation);
    ParticipantObservationDto? Get(Guid participantId);
    void Remove(Guid participantId);
}
