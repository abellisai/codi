using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Stato corrente del partecipante autenticato (§10.5, §12.4). Serve al client che si è
/// riconnesso, al server che deve rimandare uno snapshot quando scopre che il client ha applicato
/// una revisione superata (§11.2 passo 7) e a chi vuole rileggere lo stato senza acquisire di nuovo
/// l'operatività.
///
/// È una lettura in una sola unità di lavoro con transazione: partecipazione, sessione e stato
/// desiderato devono appartenere allo stesso istante, e leggerli separatamente potrebbe produrre
/// uno snapshot con la revisione di una sessione e la vista di un'altra.
/// </summary>
public sealed class GetParticipantSnapshotHandler(
    IUnitOfWorkContext unitOfWorkContext,
    IServerInstance serverInstance,
    TimeProvider timeProvider) : IRequestHandler<GetParticipantSnapshotQuery, ParticipantSnapshotDto>
{
    public async ValueTask<ParticipantSnapshotDto> Handle(
        GetParticipantSnapshotQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.ParticipantId is not { } participantId)
        {
            throw CodiRequestException.AuthenticationRequired(GetParticipantSnapshotQuery.RequestName);
        }

        var participantKey = participantId.ToString("D");

        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var participantRow = await work.Repo<ISessionParticipantRepository>()
            .GetAsync(participantKey, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var participant = participantRow.ToDomain();

        var sessionRow = await work.Repo<ILessonSessionRepository>()
            .GetAsync(participant.SessionId.ToString("D"), cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var session = sessionRow.ToDomain();

        var state = await work.Repo<IParticipantStateRepository>()
            .GetAsync(participantKey, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        return ParticipantSnapshotFactory.Create(
            serverInstance,
            session,
            participant.Id,
            participant.ConnectionEpoch,
            timeProvider.GetUtcNow(),
            state,
            new ParticipantProfileDto(participant.Id, participant.DisplayName, participantRow.RowVersion));
    }
}
