using Qwerty.Codi.Application.Dto;
using System.Text.Json;
using System.Text.Json.Nodes;
using Qwerty.Codi.Api.Dispatch;
using Qwerty.Codi.Api.Hubs;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Sessions;

namespace Qwerty.Codi.Api.Endpoints.Public;

internal static class PublicAttemptEndpoints
{
    public static void MapCodiPublicAttempts(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/participant/attempts/{id:guid}/workspace", (Guid id, HttpContext context,
            CodiRequestExecutor executor, ICodiRequestContextProvider provider, CancellationToken ct) =>
            ExecuteAsync(id, SaveAttemptWorkspaceCommand.RequestName, context, executor, provider, ct));
        endpoints.MapPut("/participant/attempts/{id:guid}/checkpoint", (Guid id, HttpContext context,
            CodiRequestExecutor executor, ICodiRequestContextProvider provider, CancellationToken ct) =>
            ExecuteAsync(id, SaveAttemptCheckpointCommand.RequestName, context, executor, provider, ct));
        endpoints.MapPost("/participant/attempts/{id:guid}/finalization", (Guid id, HttpContext context,
            CodiRequestExecutor executor, ICodiRequestContextProvider provider, CancellationToken ct) =>
            ExecuteAsync(id, FinalizeAttemptCommand.RequestName, context, executor, provider, ct));

        // Anteprima oltre il limite SignalR (§10.3, §15.5): stesso comando dell'hub, stessa cache.
        // Dopo l'esecuzione i docenti che osservano la sessione ricevono l'avviso, come sul canale hub.
        endpoints.MapPut("/participant/attempts/{id:guid}/preview", async (Guid id, HttpContext context,
            CodiRequestExecutor executor, ICodiRequestContextProvider provider,
            WorkspacePreviewNotifier notifier, CancellationToken ct) =>
        {
            var identity = await provider.ResolveRequestContextAsync(context, ct).ConfigureAwait(false);
            using var adjusted = await ReadAdjustedAsync(id, PublishWorkspacePreviewCommand.RequestName, context, ct).ConfigureAwait(false);
            var preview = (WorkspacePreviewDto?)await executor.ExecuteAsync(CodiRequestKind.Command,
                PublishWorkspacePreviewCommand.RequestName, CodiRequestChannel.Http, adjusted.RootElement, identity, ct).ConfigureAwait(false);
            if (preview is not null)
            {
                await notifier.NotifyAsync(preview, ct).ConfigureAwait(false);
            }

            return Results.Ok(preview);
        });

        endpoints.MapGet("/participant/finalizations", async (HttpContext context,
            CodiRequestExecutor executor, ICodiRequestContextProvider provider, CancellationToken ct) =>
        {
            var identity = await provider.ResolveRequestContextAsync(context, ct).ConfigureAwait(false);
            using var body = JsonDocument.Parse("{}");
            return Results.Ok(await executor.ExecuteAsync(CodiRequestKind.Query, GetAttemptFinalizationsQuery.RequestName,
                CodiRequestChannel.Http, body.RootElement, identity, ct).ConfigureAwait(false));
        });
    }

    private static async Task<IResult> ExecuteAsync(Guid id, string name, HttpContext context,
        CodiRequestExecutor executor, ICodiRequestContextProvider provider, CancellationToken ct)
    {
        var identity = await provider.ResolveRequestContextAsync(context, ct).ConfigureAwait(false);
        using var adjusted = await ReadAdjustedAsync(id, name, context, ct).ConfigureAwait(false);
        return Results.Ok(await executor.ExecuteAsync(CodiRequestKind.Command, name,
            CodiRequestChannel.Http, adjusted.RootElement, identity, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Legge il corpo e vi impone l'identificativo del percorso: l'`id` della rotta è la risorsa, e un
    /// `attemptId` diverso nel corpo è un client non allineato al contratto, non una seconda volontà.
    /// </summary>
    private static async Task<JsonDocument> ReadAdjustedAsync(Guid id, string name, HttpContext context, CancellationToken ct)
    {
        using var body = await DispatcherEndpoints.ReadPayloadAsync(context.Request, name, ct).ConfigureAwait(false);
        if (body.RootElement.ValueKind != JsonValueKind.Object) throw CodiRequestException.InvalidPayload(name);
        var payload = JsonNode.Parse(body.RootElement.GetRawText())!.AsObject();
        if (body.RootElement.TryGetProperty("attemptId", out var supplied) &&
            (supplied.ValueKind != JsonValueKind.String || !Guid.TryParse(supplied.GetString(), out var suppliedId) || suppliedId != id))
            throw CodiRequestException.InvalidPayload(name);
        payload["attemptId"] = id.ToString("D");

        return JsonDocument.Parse(payload.ToJsonString());
    }
}
