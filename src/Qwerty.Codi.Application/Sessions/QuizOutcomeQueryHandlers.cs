using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Exercises;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Exercises;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Corregge la risposta dell'alunno su un esercizio dichiarativo (§13.9).
///
/// La correzione avviene **sul server** e la risposta attesa non lascia mai il server: il confronto
/// usa la soluzione privata della versione pubblicata, la stessa riga che il catalogo riserva al
/// docente (§13.1). Se la versione non ha una soluzione, il server non può dire se la risposta è
/// giusta, e non lo dice: restituisce un esito negativo senza inventare una correzione, perché
/// dichiarare sbagliata una risposta corretta sarebbe peggio che ammettere di non poter correggere.
/// </summary>
public sealed class GetQuizOutcomeHandler(
    IUnitOfWorkContext unitOfWorkContext) : IRequestHandler<GetQuizOutcomeQuery, QuizOutcomeDto>
{
    public async ValueTask<QuizOutcomeDto> Handle(
        GetQuizOutcomeQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.ParticipantId is not { } participantId)
        {
            throw CodiRequestException.AuthenticationRequired(GetQuizOutcomeQuery.RequestName);
        }

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var loaded = await ParticipantWorkLoader
            .LoadAsync(work, participantId, cancellationToken)
            .ConfigureAwait(false);

        var definition = ExerciseDefinitionJson.Parse(
            loaded.Version.DefinitionJson, GetQuizOutcomeQuery.RequestName);

        if (definition.Data is not QuizData quiz)
        {
            // La lettura è del tipo dichiarativo: chiederla su un esercizio di percorso è una
            // richiesta che non corrisponde all'attività assegnata, non un esito negativo.
            throw CodiRequestException.InvalidPayload(GetQuizOutcomeQuery.RequestName);
        }

        var privateRow = await work.Repo<IExerciseVersionPrivateRepository>()
            .GetAsync(loaded.Version.Id, cancellationToken)
            .ConfigureAwait(false);
        var expected = QuizAnswerJson.ReadExpected(privateRow?.ReferenceSolutionJson);

        if (expected is null)
        {
            return new QuizOutcomeDto(false, quiz.Feedback.Success, quiz.Feedback.Collision);
        }

        var given = QuizAnswerJson.Read(loaded.Attempt.WorkspaceJson);

        return new QuizOutcomeDto(
            quiz.Accepts(given, expected), quiz.Feedback.Success, quiz.Feedback.Collision);
    }
}
