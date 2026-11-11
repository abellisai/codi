using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Sessions;

internal static class DesiredStateProjection
{
    public static DesiredStateDto Create(ParticipantStateEntity state) => new(
        state.View, state.InteractionEnabledFlag != 0,
        Guid.TryParse(state.AssignmentId, out var assignment) ? assignment : null,
        Guid.TryParse(state.AttemptId, out var attempt) ? attempt : null,
        state.TeacherPausedFlag != 0, state.RecoveryBlockedFlag != 0,
        state.ExecutionGeneration, ExecutionDirectiveDto.Read(state.DirectiveJson));
}
