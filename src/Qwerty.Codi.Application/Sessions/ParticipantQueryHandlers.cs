using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Contesto del partecipante autenticato (§10.5): legge la partecipazione, il suo stato desiderato
/// e la sessione a cui appartiene. La partecipazione arriva dalla credenziale, mai dal payload,
/// quindi leggere il contesto di un altro alunno non è un'operazione che questo caso d'uso offra.
///
/// Una riga mancante è una risorsa assente: succede se la partecipazione o la sessione sono state
/// rimosse sotto una credenziale ancora valida, e in quel caso la risposta è un 404, non un
/// contesto inventato.
/// </summary>
public sealed class GetParticipantContextHandler(IUnitOfWorkContext unitOfWorkContext)
    : IRequestHandler<GetParticipantContextQuery, ParticipantContextDto>
{
    public async ValueTask<ParticipantContextDto> Handle(
        GetParticipantContextQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.ParticipantId is not { } participantId)
        {
            throw CodiRequestException.AuthenticationRequired(GetParticipantContextQuery.RequestName);
        }

        var id = participantId.ToString("D");

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var participantRow = await work.Repo<ISessionParticipantRepository>()
            .GetAsync(id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var participant = participantRow.ToDomain();

        var state = await work.Repo<IParticipantStateRepository>()
            .GetAsync(id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var sessionRow = await work.Repo<ILessonSessionRepository>()
            .GetAsync(participant.SessionId.ToString("D"), cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var session = sessionRow.ToDomain();

        // I nomi del percorso — scuola, classe, lezione — sono contesto, non permessi: se una di
        // queste righe mancasse, l'alunno deve comunque poter lavorare, quindi il nome si omette
        // invece di far fallire la lettura. Le righe essenziali (partecipazione, stato, sessione)
        // restano invece obbligatorie.
        var lessonRow = await work.Repo<ILessonRepository>()
            .GetAsync(session.LessonId.ToString("D"), cancellationToken)
            .ConfigureAwait(false);

        var classroomRow = await work.Repo<IClassroomRepository>()
            .GetAsync(session.ClassroomId.ToString("D"), cancellationToken)
            .ConfigureAwait(false);

        var schoolRow = classroomRow is null
            ? null
            : await work.Repo<ISchoolRepository>()
                .GetAsync(classroomRow.SchoolId, cancellationToken)
                .ConfigureAwait(false);

        return new ParticipantContextDto(
            participant.Id,
            participant.SessionId,
            participant.DisplayName,
            participant.AdmissionStatus.ToString(),
            state.View.ToString(),
            session.Status.ToString(),
            schoolRow?.Name ?? string.Empty,
            classroomRow?.Name ?? string.Empty,
            lessonRow?.Title ?? string.Empty);
    }
}
