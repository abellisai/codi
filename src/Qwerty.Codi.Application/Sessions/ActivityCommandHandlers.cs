using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Abilita l'attività della classe o di un singolo alunno (§6.4, §6.5). Le precondizioni si
/// verificano su **tutti** i destinatari prima di scrivere: §6.5 vieta l'applicazione parziale
/// implicita, quindi un alunno senza tentativo, in pausa docente o con una vista diversa
/// dall'esercizio fa fallire il comando con <c>409 SessionStateConflict</c> e l'elenco dei conflitti
/// nel messaggio, senza che nessuno venga abilitato.
///
/// Il comando cambia il permesso, non il lavoro: il tentativo resta quello dell'assegnazione, il
/// progetto non si tocca e la generazione di esecuzione non avanza — quella la cambia
/// <c>RestartExecution</c>. La revisione della sessione avanza perché lo stato desiderato è
/// cambiato; il default di classe resta invece quello di prima, come nell'assegnazione.
///
/// Il coordinatore della sessione si acquisisce prima della transazione (§8.2), la revisione si
/// verifica dentro la transazione e comando, consegne e messaggio in coda si scrivono prima del
/// commit (§12.1).
/// </summary>
public sealed class EnableActivityHandler(
    IUnitOfWorkContext unitOfWorkContext,
    ISessionCoordinator sessionCoordinator,
    IOutboxSignal outboxSignal,
    TimeProvider timeProvider) : IRequestHandler<EnableActivityCommand, CommandAcceptedDto>
{
    public async ValueTask<CommandAcceptedDto> Handle(
        EnableActivityCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } controllerAdminId)
        {
            throw CodiRequestException.AuthenticationRequired(EnableActivityCommand.RequestName);
        }

        if (request.ExpectedSessionRevision is not { } expectedSessionRevision)
        {
            throw CodiRequestException.VersionRequired(EnableActivityCommand.RequestName);
        }

        await using var work = await SessionCommandOperation.OpenAsync(
            unitOfWorkContext, sessionCoordinator, timeProvider, request.SessionId,
            request.Context, EnableActivityCommand.RequestName, request, cancellationToken).ConfigureAwait(false);
        if (work.TryReplay<CommandAcceptedDto>(out var replay)) return replay;

        var session = await SessionCommandPreamble
            .LoadSessionAsync(
                work,
                request.SessionId,
                controllerAdminId,
                expectedSessionRevision,
                "La sessione è terminata: non si abilita attività nuova.",
                cancellationToken)
            .ConfigureAwait(false);

        var targets = await SessionCommandTargets
            .ResolveAsync(work, request.SessionId, request.ParticipantId, cancellationToken)
            .ConfigureAwait(false);

        EnsureTargetsAreReady(targets);

        var now = timeProvider.GetUtcNow();

        await SessionCommandPreamble
            .AdvanceRevisionAsync(work, session, expectedSessionRevision, now, cancellationToken)
            .ConfigureAwait(false);

        foreach (var state in targets)
        {
            state.InteractionEnabledFlag = 1;
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
                    EnableActivityCommand.RequestName,
                    request.ParticipantId,
                    new DesiredStateDto(
                        ParticipantView.Exercise.ToString(),
                        InteractionEnabled: true),
                    targets,
                    now),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Precondizioni di §6.4 su ogni destinatario, prima di qualunque scrittura: esercizio caricato
    /// (tentativo presente), docente non in pausa, vista dell'esercizio. L'elenco dei conflitti è
    /// nel messaggio perché il docente sappia **chi** impedisce il comando collettivo (§6.5).
    /// </summary>
    private static void EnsureTargetsAreReady(IReadOnlyList<ParticipantStateEntity> targets)
    {
        var exerciseView = ParticipantView.Exercise.ToString();
        var conflicts = new List<string>(targets.Count);

        foreach (var state in targets)
        {
            if (state.AttemptId is null)
            {
                conflicts.Add($"{state.Id}: nessun esercizio caricato");
            }

            if (state.TeacherPausedFlag != 0)
            {
                conflicts.Add($"{state.Id}: in pausa docente");
            }

            if (!string.Equals(state.View, exerciseView, StringComparison.Ordinal))
            {
                conflicts.Add($"{state.Id}: vista {state.View}, non {exerciseView}");
            }
        }

        if (conflicts.Count > 0)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionStateConflict,
                $"Il comando non si applica a tutti i destinatari ({string.Join("; ", conflicts)}).");
        }
    }
}
