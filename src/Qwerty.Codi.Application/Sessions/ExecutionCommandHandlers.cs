using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Exercises;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Precondizioni condivise dai comandi che fanno eseguire il lavoro (§6.4): l'esercizio deve essere
/// caricato, l'attività abilitata, e nessuna pausa — docente o di recupero — deve essere in corso.
/// Si verificano su **tutti** i destinatari prima di qualunque scrittura, perché §6.5 vieta
/// l'applicazione parziale implicita: un alunno che non soddisfa le precondizioni fa fallire il
/// comando collettivo con l'elenco dei conflitti, e nessuno riceve una direttiva.
/// </summary>
internal static class ExecutionCommandRules
{
    /// <summary>Destinatari pronti a ricevere una direttiva di esecuzione, o il conflitto che lo impedisce.</summary>
    public static void EnsureExecutionReady(IReadOnlyList<ParticipantStateEntity> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        var exerciseView = ParticipantView.Exercise.ToString();
        var conflicts = new List<string>(targets.Count);

        foreach (var state in targets)
        {
            if (state.AttemptId is null)
            {
                conflicts.Add($"{state.Id}: nessun esercizio caricato");
            }

            if (state.InteractionEnabledFlag == 0)
            {
                conflicts.Add($"{state.Id}: attività non abilitata");
            }

            if (state.TeacherPausedFlag != 0)
            {
                conflicts.Add($"{state.Id}: in pausa docente");
            }

            if (state.RecoveryBlockedFlag != 0)
            {
                conflicts.Add($"{state.Id}: pausa di recupero in corso");
            }

            if (!string.Equals(state.View, exerciseView, StringComparison.Ordinal))
            {
                conflicts.Add($"{state.Id}: vista {state.View}, non {exerciseView}");
            }
        }

        ThrowOnConflicts(conflicts);
    }

    /// <summary>Conflitti raccolti in un solo rifiuto: il docente sa chi li ha prodotti (§6.5).</summary>
    public static void ThrowOnConflicts(List<string> conflicts)
    {
        if (conflicts.Count > 0)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionStateConflict,
                $"Il comando non si applica a tutti i destinatari ({string.Join("; ", conflicts)}).");
        }
    }
}

/// <summary>
/// Scrive una direttiva di avvio nello stato desiderato (§6.4, §10.4). Il comando non azzera la
/// scena, non tocca il progetto e non cambia la generazione: la direttiva dichiara la generazione
/// corrente del singolo destinatario, oppure nessuna generazione quando il comando è collettivo —
/// dopo un riavvio individuale le generazioni divergono e ognuno applica la direttiva alla propria.
///
/// La ripetizione del comando produce una direttiva **nuova**: il server non osserva lo stato di
/// esecuzione del browser, che non è persistito (§6.3, §15.1), quindi non può sapere se il programma
/// è già in corsa. «Nessuna modifica» è l'effetto del client che riconosce di essere già in
/// esecuzione, non una deduzione del server — decisione locale registrata nello stato di sviluppo.
/// </summary>
public sealed class RunProgramHandler(
    IUnitOfWorkContext unitOfWorkContext,
    ISessionCoordinator sessionCoordinator,
    IOutboxSignal outboxSignal,
    TimeProvider timeProvider) : IRequestHandler<RunProgramCommand, CommandAcceptedDto>
{
    public async ValueTask<CommandAcceptedDto> Handle(
        RunProgramCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } controllerAdminId)
        {
            throw CodiRequestException.AuthenticationRequired(RunProgramCommand.RequestName);
        }

        if (request.ExpectedSessionRevision is not { } expectedSessionRevision)
        {
            throw CodiRequestException.VersionRequired(RunProgramCommand.RequestName);
        }

        await using var work = await SessionCommandOperation.OpenAsync(
            unitOfWorkContext, sessionCoordinator, timeProvider, request.SessionId,
            request.Context, RunProgramCommand.RequestName, request, cancellationToken).ConfigureAwait(false);
        if (work.TryReplay<CommandAcceptedDto>(out var replay)) return replay;

        var session = await SessionCommandPreamble
            .LoadSessionAsync(
                work,
                request.SessionId,
                controllerAdminId,
                expectedSessionRevision,
                "La sessione è terminata: non si avvia più nulla.",
                cancellationToken)
            .ConfigureAwait(false);

        var targets = await SessionCommandTargets
            .ResolveAsync(work, request.SessionId, request.ParticipantId, cancellationToken)
            .ConfigureAwait(false);

        ExecutionCommandRules.EnsureExecutionReady(targets);

        var now = timeProvider.GetUtcNow();
        var single = request.ParticipantId is null ? null : targets[0];
        var directive = ExecutionDirectiveDto.Run(single?.ExecutionGeneration, now);

        await SessionCommandPreamble
            .AdvanceRevisionAsync(work, session, expectedSessionRevision, now, cancellationToken)
            .ConfigureAwait(false);

        foreach (var state in targets)
        {
            state.DirectiveJson = directive.ToJson();
            state.EffectiveRevision = session.SessionRevision;
            state.UpdatedAtUtcMs = now.ToUnixTimeMilliseconds();

            await SessionCommandPreamble.UpdateStateAsync(work, state, cancellationToken).ConfigureAwait(false);
        }

        return await DesiredStateCommandRegistration
            .RegisterAsync(
                work,
                outboxSignal,
                session,
                new DesiredStateCommandEffect(
                    controllerAdminId,
                    RunProgramCommand.RequestName,
                    request.ParticipantId,
                    new DesiredStateDto(
                        ParticipantView.Exercise.ToString(),
                        InteractionEnabled: true,
                        AssignmentId: single is null ? null : Guid.Parse(single.AssignmentId!),
                        AttemptId: single is null ? null : Guid.Parse(single.AttemptId!),
                        TeacherPaused: false,
                        RecoveryBlocked: false,
                        ExecutionGeneration: single?.ExecutionGeneration,
                        ExecutionDirective: directive),
                    targets,
                    now),
                cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// Riavvia l'esecuzione incrementando la generazione (§6.4, §10.4): il progetto resta quello
/// dell'alunno, la scena riparte e la direttiva di riavvio porta la generazione nuova, così una
/// direttiva di avvio rimasta in volo non riparte su un lavoro già ricominciato.
///
/// Come `RunProgram`, il comando è rifiutato se l'attività non è abilitata o una pausa è in corso, e
/// nel caso collettivo la generazione della direttiva resta quella di ciascun destinatario.
/// </summary>
public sealed class RestartExecutionHandler(
    IUnitOfWorkContext unitOfWorkContext,
    ISessionCoordinator sessionCoordinator,
    IOutboxSignal outboxSignal,
    TimeProvider timeProvider) : IRequestHandler<RestartExecutionCommand, CommandAcceptedDto>
{
    public async ValueTask<CommandAcceptedDto> Handle(
        RestartExecutionCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } controllerAdminId)
        {
            throw CodiRequestException.AuthenticationRequired(RestartExecutionCommand.RequestName);
        }

        if (request.ExpectedSessionRevision is not { } expectedSessionRevision)
        {
            throw CodiRequestException.VersionRequired(RestartExecutionCommand.RequestName);
        }

        await using var work = await SessionCommandOperation.OpenAsync(
            unitOfWorkContext, sessionCoordinator, timeProvider, request.SessionId,
            request.Context, RestartExecutionCommand.RequestName, request, cancellationToken).ConfigureAwait(false);
        if (work.TryReplay<CommandAcceptedDto>(out var replay)) return replay;

        var session = await SessionCommandPreamble
            .LoadSessionAsync(
                work,
                request.SessionId,
                controllerAdminId,
                expectedSessionRevision,
                "La sessione è terminata: non si riavvia più nulla.",
                cancellationToken)
            .ConfigureAwait(false);

        var targets = await SessionCommandTargets
            .ResolveAsync(work, request.SessionId, request.ParticipantId, cancellationToken)
            .ConfigureAwait(false);

        ExecutionCommandRules.EnsureExecutionReady(targets);

        var now = timeProvider.GetUtcNow();
        var single = request.ParticipantId is null ? null : targets[0];
        long? restartedGeneration = single is null ? null : single.ExecutionGeneration + 1;
        var directive = ExecutionDirectiveDto.Restart(restartedGeneration, now);

        await SessionCommandPreamble
            .AdvanceRevisionAsync(work, session, expectedSessionRevision, now, cancellationToken)
            .ConfigureAwait(false);

        foreach (var state in targets)
        {
            state.ExecutionGeneration = checked(state.ExecutionGeneration + 1);
            state.DirectiveJson = directive.ToJson();
            state.EffectiveRevision = session.SessionRevision;
            state.UpdatedAtUtcMs = now.ToUnixTimeMilliseconds();

            await SessionCommandPreamble.UpdateStateAsync(work, state, cancellationToken).ConfigureAwait(false);
        }

        return await DesiredStateCommandRegistration
            .RegisterAsync(
                work,
                outboxSignal,
                session,
                new DesiredStateCommandEffect(
                    controllerAdminId,
                    RestartExecutionCommand.RequestName,
                    request.ParticipantId,
                    new DesiredStateDto(
                        ParticipantView.Exercise.ToString(),
                        InteractionEnabled: true,
                        AssignmentId: single is null ? null : Guid.Parse(single.AssignmentId!),
                        AttemptId: single is null ? null : Guid.Parse(single.AttemptId!),
                        TeacherPaused: false,
                        RecoveryBlocked: false,
                        ExecutionGeneration: restartedGeneration,
                        ExecutionDirective: directive),
                    targets,
                    now),
                cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// Azzera il lavoro di un alunno (§6.4, §10.4, §10.6): archivia il tentativo corrente e ne crea uno
/// nuovo dal modello iniziale della versione fissata dall'assegnazione, pronto e bloccato, senza
/// direttiva e con la generazione azzerata. Il comando dichiara il tentativo che il docente ha visto
/// nella conferma: se non è più quello corrente è un conflitto di stato, se la sua versione di riga
/// non corrisponde è un conflitto di versione. Nessuna delle due verifiche si può rimandare dopo la
/// scrittura, perché l'azzeramento non è reversibile.
/// </summary>
public sealed class ResetWorkHandler(
    IUnitOfWorkContext unitOfWorkContext,
    ISessionCoordinator sessionCoordinator,
    IOutboxSignal outboxSignal,
    TimeProvider timeProvider) : IRequestHandler<ResetWorkCommand, CommandAcceptedDto>
{
    public async ValueTask<CommandAcceptedDto> Handle(
        ResetWorkCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } controllerAdminId)
        {
            throw CodiRequestException.AuthenticationRequired(ResetWorkCommand.RequestName);
        }

        if (request.ExpectedSessionRevision is not { } expectedSessionRevision)
        {
            throw CodiRequestException.VersionRequired(ResetWorkCommand.RequestName);
        }

        await using var work = await SessionCommandOperation.OpenAsync(
            unitOfWorkContext, sessionCoordinator, timeProvider, request.SessionId,
            request.Context, ResetWorkCommand.RequestName, request, cancellationToken).ConfigureAwait(false);
        if (work.TryReplay<CommandAcceptedDto>(out var replay)) return replay;

        var session = await SessionCommandPreamble
            .LoadSessionAsync(
                work,
                request.SessionId,
                controllerAdminId,
                expectedSessionRevision,
                "La sessione è terminata: non si azzera più il lavoro.",
                cancellationToken)
            .ConfigureAwait(false);

        var targets = await SessionCommandTargets
            .ResolveAsync(work, request.SessionId, request.ParticipantId, cancellationToken)
            .ConfigureAwait(false);

        var conflicts = new List<string>(1);
        var state = targets.Count == 1 ? targets[0] : null;

        if (state is null)
        {
            conflicts.Add("il destinatario non è un alunno ammesso della sessione");
        }
        else if (state.AttemptId is null)
        {
            conflicts.Add($"{state.Id}: nessun esercizio caricato");
        }
        else if (!string.Equals(state.AttemptId, request.AttemptId.ToString("D"), StringComparison.Ordinal))
        {
            conflicts.Add($"{state.Id}: il lavoro corrente non è quello indicato");
        }

        ExecutionCommandRules.ThrowOnConflicts(conflicts);

        var currentAttempt = state!.AttemptId!;
        var attempts = work.Repo<IExerciseAttemptRepository>();
        var stored = await attempts.GetAsync(currentAttempt, cancellationToken).ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        if (stored.RowVersion != request.AttemptRowVersion)
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        if (!string.Equals(stored.ParticipantId, request.ParticipantId.ToString("D"), StringComparison.Ordinal) ||
            !string.Equals(stored.SessionId, request.SessionId.ToString("D"), StringComparison.Ordinal))
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var assignment = await work.Repo<IActivityAssignmentRepository>()
            .GetAsync(stored.AssignmentId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var version = await work.Repo<IExerciseVersionRepository>()
            .GetAsync(assignment.ExerciseVersionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var now = timeProvider.GetUtcNow();
        var archived = stored.ToDomain();
        archived.Archive(now);
        await AttemptFinalization.IssueAsync(work, state, now, cancellationToken).ConfigureAwait(false);
        stored.Status = archived.Status.ToString();
        stored.UpdatedAtUtcMs = now.ToUnixTimeMilliseconds();

        if (!await attempts.UpdateAsync(stored, cancellationToken)
                .ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        // La numerazione è progressiva per coppia partecipante/assegnazione e non si riusa: un
        // azzeramento conserva il tentativo precedente invece di sovrascriverlo (§7.3).
        var history = await attempts
            .QueryByAssignmentAndParticipantAsync(
                Guid.Parse(stored.AssignmentId),
                request.ParticipantId,
                cancellationToken)
            .ConfigureAwait(false);

        var attemptNumber = history.Count == 0 ? 1 : history.Max(row => row.AttemptNumber) + 1;
        var fresh = ExerciseAttempt.Create(
            session.Id,
            request.ParticipantId,
            Guid.Parse(stored.AssignmentId),
            Guid.Parse(assignment.ExerciseVersionId),
            attemptNumber,
            version.InitialWorkspaceJson,
            ExerciseContentHash.ComputeWorkspace(version.InitialWorkspaceJson),
            now);

        await attempts.InsertAsync(ExerciseAttemptEntity.FromDomain(fresh), cancellationToken)
            .ConfigureAwait(false);

        await SessionCommandPreamble
            .AdvanceRevisionAsync(work, session, expectedSessionRevision, now, cancellationToken)
            .ConfigureAwait(false);

        state.AttemptId = fresh.Id.ToString("D");
        state.View = ParticipantView.Exercise.ToString();
        state.InteractionEnabledFlag = 0;
        state.RecoveryBlockedFlag = 0;
        state.ExecutionGeneration = 0;
        state.DirectiveJson = null;
        state.ResumeContextJson = null;
        state.EffectiveRevision = session.SessionRevision;
        state.UpdatedAtUtcMs = now.ToUnixTimeMilliseconds();

        await SessionCommandPreamble.UpdateStateAsync(work, state, cancellationToken).ConfigureAwait(false);

        return await DesiredStateCommandRegistration
            .RegisterAsync(
                work,
                outboxSignal,
                session,
                new DesiredStateCommandEffect(
                    controllerAdminId,
                    ResetWorkCommand.RequestName,
                    request.ParticipantId,
                    new DesiredStateDto(
                        ParticipantView.Exercise.ToString(),
                        InteractionEnabled: false,
                        AssignmentId: Guid.Parse(stored.AssignmentId),
                        AttemptId: fresh.Id,
                        TeacherPaused: state.TeacherPausedFlag != 0,
                        RecoveryBlocked: false,
                        ExecutionGeneration: 0),
                    [state],
                    now),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
