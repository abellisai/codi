using Qwerty.Codi.Application.Dto;
using System.Text.Json;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Sessions;

internal static class SessionControlState
{
    public static async Task<CommandAcceptedDto> ApplyAsync(IUnitOfWork work, LessonSession session, Guid adminId,
        string name, Guid? participantId, DateTimeOffset now, IOutboxSignal signal, CancellationToken cancellationToken)
    {
        var targets = await SessionCommandTargets.ResolveAsync(work, session.Id, participantId, cancellationToken).ConfigureAwait(false);
        if (name == ResumeCommand.RequestName && participantId is not null &&
            targets.Any(state => state.TeacherPausedFlag == 0 && state.RecoveryBlockedFlag == 0 && state.ResumeContextJson is null))
        {
            throw new CodiRequestException(CodiErrorCodes.SessionStateConflict, "L'alunno non ha uno stato sospeso ripristinabile.");
        }

        foreach (var state in targets)
        {
            switch (name)
            {
                case "Pause":
                    state.ResumeContextJson ??= JsonSerializer.Serialize(DesiredStateProjection.Create(state), CodiJson.Payload);
                    state.TeacherPausedFlag = 1;
                    state.InteractionEnabledFlag = 0;
                    break;
                case "Resume":
                    var prior = state.ResumeContextJson is null ? null :
                        JsonSerializer.Deserialize<DesiredStateDto>(state.ResumeContextJson, CodiJson.Payload);
                    if (state.RecoveryBlockedFlag != 0) state.DirectiveJson = null;
                    state.TeacherPausedFlag = 0;
                    state.RecoveryBlockedFlag = 0;
                    state.InteractionEnabledFlag = prior?.InteractionEnabled == true ? 1 : 0;
                    if (prior is not null) state.View = prior.View;
                    state.ResumeContextJson = null;
                    break;
                case "EndSession":
                    await AttemptFinalization.IssueAsync(work, state, now, cancellationToken).ConfigureAwait(false);
                    state.View = nameof(ParticipantView.Goodbye);
                    state.InteractionEnabledFlag = 0;
                    state.TeacherPausedFlag = 1;
                    state.DirectiveJson = null;
                    state.ResumeContextJson = null;
                    break;
            }

            state.EffectiveRevision = session.SessionRevision;
            state.UpdatedAtUtcMs = now.ToUnixTimeMilliseconds();
            await SessionCommandPreamble.UpdateStateAsync(work, state, cancellationToken).ConfigureAwait(false);
        }

        return await DesiredStateCommandRegistration.RegisterAsync(work, signal, session,
            new DesiredStateCommandEffect(adminId, name, participantId,
                new DesiredStateDto(name == "EndSession" ? "Goodbye" : "Waiting", false), targets, now),
            cancellationToken, commit: false).ConfigureAwait(false);
    }
}
