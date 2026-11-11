using Qwerty.Codi.Application.Dto;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>Default dei futuri ingressi, senza tentativo o direttive storiche.</summary>
internal static class ClassActivityState
{
    private static readonly JsonSerializerOptions Options = new(CodiJson.Payload) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip, RespectRequiredConstructorParameters = false };
    public static DesiredStateDto Read(string json)
    {
        ParticipantClassDefault.ReadView(json);
        return JsonSerializer.Deserialize<DesiredStateDto>(json, Options)!;
    }
    public static DesiredStateDto? Suspended(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("resumeContext", out var context) && context.ValueKind == JsonValueKind.Object
            ? context.Deserialize<DesiredStateDto>(CodiJson.Payload) : null;
    }

    public static async Task UpdateAsync(IUnitOfWork work, LessonSession session, DesiredStateCommandEffect effect, CancellationToken cancellationToken)
    {
        if (effect.TargetParticipantId is not null || effect.CommandName is "RunProgram" or "RestartExecution" or "EndSession") return;
        var row = await work.Repo<ILessonSessionRepository>().GetAsync(session.Id.ToString("D"), cancellationToken).ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();
        var previous = effect.PreviousClassDefaultJson ?? row.ClassDefaultJson;
        var state = Read(previous);
        var resume = Suspended(previous);
        switch (effect.CommandName)
        {
            case "AssignExercise":
                state = effect.DesiredState with { AttemptId = null, TeacherPaused = session.Status == SessionStatus.Paused, ExecutionDirective = null };
                resume = null;
                break;
            case "EnableActivity": state = state with { InteractionEnabled = true }; break;
            case "ShowWaiting":
                if (state.View == "Exercise") resume = state;
                state = state with { View = "Waiting", InteractionEnabled = false };
                break;
            case "Pause":
                resume ??= state;
                state = state with { TeacherPaused = true, InteractionEnabled = false };
                break;
            case "Resume":
                state = (resume ?? state) with { TeacherPaused = false, RecoveryBlocked = false, ExecutionDirective = null };
                resume = null;
                break;
            default: return;
        }
        var document = (JsonObject)JsonSerializer.SerializeToNode(state, CodiJson.Payload)!;
        if (resume is not null) document["resumeContext"] = JsonSerializer.SerializeToNode(resume, CodiJson.Payload);
        var json = state.View == "Waiting" && state.AssignmentId is null && !state.TeacherPaused && resume is null
            ? LessonSession.WaitingClassJson : document.ToJsonString(CodiJson.Payload);
        if (!await work.Repo<ILessonSessionRepository>().CompleteClassDefaultAsync(row.Id, row.RowVersion, json, cancellationToken).ConfigureAwait(false))
            throw CodiRequestException.RowVersionMismatch();
    }
}
