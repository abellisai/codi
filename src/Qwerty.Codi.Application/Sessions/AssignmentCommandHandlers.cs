using Qwerty.Codi.Application.Dto;
using System.Text.Json;
using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Exercises;
using Qwerty.Codi.Application.Lessons;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Assegna una versione pubblicata alla classe o a un alunno (§6.4, §6.5). La transazione crea
/// l'assegnazione, **un tentativo per destinatario** con il progetto iniziale della versione, lo stato
/// desiderato di ognuno e — come ogni comando di regia (§12.1) — il comando registrato, le consegne e
/// il messaggio in coda.
///
/// Il tentativo nasce **pronto e bloccato**: la vista è quella dell'esercizio, ma i controlli restano
/// chiusi finché il docente non abilita l'attività. Un alunno che entra dopo non riceve un tentativo
/// dal comando: la sua assegnazione di classe resta valida e il tentativo arriverà quando la regia lo
/// assegnerà di nuovo — è il limite dichiarato di questa porzione.
///
/// Il coordinatore della sessione si acquisisce prima della transazione (§8.2): due assegnazioni
/// concorrenti sulla stessa sessione si mettono in fila e la seconda vede la revisione avanzata.
/// </summary>
public sealed class AssignExerciseHandler(
    IUnitOfWorkContext unitOfWorkContext,
    ISessionCoordinator sessionCoordinator,
    IOutboxSignal outboxSignal,
    TimeProvider timeProvider) : IRequestHandler<AssignExerciseCommand, CommandAcceptedDto>
{
    public async ValueTask<CommandAcceptedDto> Handle(
        AssignExerciseCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } controllerAdminId)
        {
            throw CodiRequestException.AuthenticationRequired(AssignExerciseCommand.RequestName);
        }

        if (request.ExpectedSessionRevision is not { } expectedSessionRevision)
        {
            throw CodiRequestException.VersionRequired(AssignExerciseCommand.RequestName);
        }

        EnsureMaterial(request);

        await using var work = await SessionCommandOperation.OpenAsync(
            unitOfWorkContext, sessionCoordinator, timeProvider, request.SessionId,
            request.Context, AssignExerciseCommand.RequestName, request, cancellationToken).ConfigureAwait(false);
        if (work.TryReplay<CommandAcceptedDto>(out var replay)) return replay;

        var session = await LoadSessionAsync(work, request, controllerAdminId, expectedSessionRevision, cancellationToken)
            .ConfigureAwait(false);

        var version = await ResolveVersionAsync(work, request, session, cancellationToken).ConfigureAwait(false);
        var targets = await ResolveTargetsAsync(work, request, cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();

        // La revisione avanza nel dominio, con la verifica della revisione attesa già fatta in
        // lettura: da qui in poi vale la revisione nuova, e ogni riga scritta la riferisce.
        await SessionCommandPreamble
            .AdvanceRevisionAsync(work, session, expectedSessionRevision, now, cancellationToken)
            .ConfigureAwait(false);

        var exerciseVersionId = Guid.Parse(version.Id);
        var assignment = ActivityAssignment.Create(
            session.Id,
            request.LessonExerciseId,
            request.ParticipantId is null ? AssignmentScope.Class : AssignmentScope.Participant,
            request.ParticipantId,
            exerciseVersionId,
            now);

        await work.Repo<IActivityAssignmentRepository>()
            .InsertAsync(ActivityAssignmentEntity.FromDomain(assignment), cancellationToken)
            .ConfigureAwait(false);

        var attempts = new List<ExerciseAttemptEntity>(targets.Count);
        Guid? singleAttemptId = null;

        foreach (var state in targets)
        {
            await AttemptFinalization.IssueAsync(work, state, now, cancellationToken).ConfigureAwait(false);
            var attempt = ExerciseAttempt.Create(
                session.Id,
                Guid.Parse(state.Id),
                assignment.Id,
                exerciseVersionId,
                attemptNumber: 1,
                version.InitialWorkspaceJson,
                ExerciseContentHash.ComputeWorkspace(version.InitialWorkspaceJson),
                now);

            attempts.Add(ExerciseAttemptEntity.FromDomain(attempt));

            if (request.ParticipantId is not null)
            {
                singleAttemptId = attempt.Id;
            }

            state.AssignmentId = assignment.Id.ToString("D");
            state.AttemptId = attempt.Id.ToString("D");
            state.View = ParticipantView.Exercise.ToString();
            state.InteractionEnabledFlag = 0;
            state.RecoveryBlockedFlag = 0;
            state.TeacherPausedFlag = session.Status == SessionStatus.Paused ? 1 : 0;

            // Un contesto nuovo non eredita la generazione del precedente: il conteggio riparte con
            // il tentativo appena creato, e una direttiva vecchia non vale più (direttiva azzerata).
            state.ExecutionGeneration = 0;
            state.DirectiveJson = null;
            state.ResumeContextJson = null;
            state.EffectiveRevision = session.SessionRevision;
            state.UpdatedAtUtcMs = now.ToUnixTimeMilliseconds();

            await SessionCommandPreamble.UpdateStateAsync(work, state, cancellationToken).ConfigureAwait(false);
        }

        var attemptsStore = work.Repo<IExerciseAttemptRepository>();
        foreach (var attempt in attempts)
        {
            await attemptsStore.InsertAsync(attempt, cancellationToken).ConfigureAwait(false);
        }

        return await DesiredStateCommandRegistration
            .RegisterAsync(
                work,
                outboxSignal,
                session,
                new DesiredStateCommandEffect(
                    controllerAdminId,
                    AssignExerciseCommand.RequestName,
                    request.ParticipantId,
                    new DesiredStateDto(
                        ParticipantView.Exercise.ToString(),
                        InteractionEnabled: false,
                        assignment.Id,
                        // L'identificativo del tentativo è del singolo alunno: in un'assegnazione di
                        // classe lo aggiunge il suo snapshot, non l'involucro uguale per tutti (§11.4).
                        singleAttemptId,
                        TeacherPaused: false,
                        RecoveryBlocked: false,
                        ExecutionGeneration: 0),
                    targets,
                    now),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Un'assegnazione dichiara il materiale in un modo solo: la versione precisa oppure la casella
    /// della sequenza. Nessuno dei due lascerebbe il comando senza oggetto, ed entrambi sarebbero
    /// due risposte diverse alla stessa domanda.
    /// </summary>
    private static void EnsureMaterial(AssignExerciseCommand request)
    {
        if ((request.LessonExerciseId is null) == (request.ExerciseVersionId is null))
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                "L'assegnazione dichiara la versione dell'esercizio oppure la casella della sequenza, non entrambe.");
        }
    }

    private static Task<LessonSession> LoadSessionAsync(
        IUnitOfWork work,
        AssignExerciseCommand request,
        Guid controllerAdminId,
        long expectedSessionRevision,
        CancellationToken cancellationToken) =>
        SessionCommandPreamble.LoadSessionAsync(
            work,
            request.SessionId,
            controllerAdminId,
            expectedSessionRevision,
            "La sessione è terminata: non si assegna materiale nuovo.",
            cancellationToken);

    /// <summary>
    /// Risolve il materiale da assegnare: la versione dichiarata, oppure l'ultima versione pubblicata
    /// dell'esercizio della casella di sequenza. Una casella di un'altra lezione è una risorsa
    /// assente, non un'assegnazione legittima.
    /// </summary>
    private static async Task<ExerciseVersionEntity> ResolveVersionAsync(
        IUnitOfWork work,
        AssignExerciseCommand request,
        LessonSession session,
        CancellationToken cancellationToken)
    {
        var versions = work.Repo<IExerciseVersionRepository>();

        if (request.ExerciseVersionId is { } versionId)
        {
            return await versions.GetAsync(versionId.ToString("D"), cancellationToken).ConfigureAwait(false)
                ?? throw CodiRequestException.ResourceNotFound();
        }

        var lessonExercise = await work.Repo<ILessonExerciseRepository>()
            .GetAsync(request.LessonExerciseId!.Value.ToString("D"), cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        if (!string.Equals(lessonExercise.LessonId, session.LessonId.ToString("D"), StringComparison.Ordinal))
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var published = await versions
            .QueryAsync(row => row.ExerciseId == lessonExercise.ExerciseId, cancellationToken)
            .ConfigureAwait(false);

        return published
            .OrderByDescending(row => row.VersionNumber)
            .FirstOrDefault()
            ?? throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                "L'esercizio della casella non ha ancora una versione pubblicata: pubblicane una prima di assegnarlo.");
    }

    /// <summary>
    /// Destinatari: gli alunni ammessi della sessione, oppure il solo indicato — che deve essere
    /// ammesso e appartenere alla sessione, altrimenti è una risorsa assente.
    /// </summary>
    private static Task<IReadOnlyList<ParticipantStateEntity>> ResolveTargetsAsync(
        IUnitOfWork work,
        AssignExerciseCommand request,
        CancellationToken cancellationToken) =>
        SessionCommandTargets.ResolveAsync(work, request.SessionId, request.ParticipantId, cancellationToken);
}
