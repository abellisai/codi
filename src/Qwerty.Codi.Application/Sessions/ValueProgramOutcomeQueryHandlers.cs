using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Exercises;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Exercises;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Corregge il programma di un alunno su un esercizio che conserva valori (§13.9).
///
/// La correzione è un'**esecuzione**: il server compila il progetto a blocchi e lo esegue sui casi di
/// prova della versione pubblicata, poi confronta ciò che il programma ha mostrato con ciò che doveva
/// mostrare. Il browser non manda un esito, manda il progetto — altrimenti la correzione sarebbe una
/// dichiarazione di una delle due parti, e la parte che dichiara sarebbe quella che ha interesse a
/// essere promossa.
///
/// Un progetto che non si compila non è un programma sbagliato: è un programma che non si esegue, e
/// la diagnosi lo dice con il nome del blocco. Confondere i due casi lascerebbe l'alunno a cercare
/// l'errore nel posto sbagliato.
/// </summary>
public sealed class GetValueProgramOutcomeHandler(
    IUnitOfWorkContext unitOfWorkContext) : IRequestHandler<GetValueProgramOutcomeQuery, ValueProgramOutcomeDto>
{
    public async ValueTask<ValueProgramOutcomeDto> Handle(
        GetValueProgramOutcomeQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.ParticipantId is not { } participantId)
        {
            throw CodiRequestException.AuthenticationRequired(GetValueProgramOutcomeQuery.RequestName);
        }

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var loaded = await ParticipantWorkLoader
            .LoadAsync(work, participantId, cancellationToken)
            .ConfigureAwait(false);

        var definition = ExerciseDefinitionJson.Parse(
            loaded.Version.DefinitionJson, GetValueProgramOutcomeQuery.RequestName);

        if (definition.Data is not ValueProgramData program)
        {
            // La lettura è di questo tipo: chiederla su un esercizio di percorso è una richiesta che
            // non corrisponde all'attività assegnata, non un esito negativo.
            throw CodiRequestException.InvalidPayload(GetValueProgramOutcomeQuery.RequestName);
        }

        var feedback = program.Feedback;

        IReadOnlyList<ValueInstruction> istruzioni;
        try
        {
            istruzioni = ValueProgramJson.Compile(
                loaded.Attempt.WorkspaceJson, program.VariableNames);
        }
        catch (CodiRequestException failure)
        {
            // Il progetto non si esegue: la diagnosi è quella della compilazione, e non va mostrata
            // come un programma che produce il risultato sbagliato.
            return new ValueProgramOutcomeDto(
                false, feedback.Success, feedback.Collision, failure.Message);
        }

        var esito = ValueProgramJson.Evaluate(
            istruzioni, program, (int)definition.Limits.MaxExecutionSteps);

        return new ValueProgramOutcomeDto(
            esito.Passed, feedback.Success, feedback.Collision, esito.Passed ? string.Empty : esito.Problem);
    }
}
