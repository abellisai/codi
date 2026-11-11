using System.Text.Json;

namespace Qwerty.Codi.Application.Dispatch;

/// <summary>
/// Unico percorso di esecuzione delle richieste: risolve il nome nel registro, verifica canale e
/// policy, deserializza il corpo e delega all'handler attraverso il mediator. Il trasporto non
/// contiene regole applicative e l'handler non conosce il trasporto (§4.4).
/// </summary>
public sealed class CodiRequestExecutor(CodiRequestRegistry registry, IServiceProvider services)
{
    /// <summary>
    /// Esegue la richiesta indicata. La policy si controlla prima dell'handler e il canale prima
    /// della policy: una richiesta locale richiesta dal canale HTTP è indistinguibile da un nome
    /// inesistente, così non si rivela nemmeno che il nome esiste (§10.7).
    /// </summary>
    public async ValueTask<object?> ExecuteAsync(
        CodiRequestKind kind,
        string requestName,
        CodiRequestChannel channel,
        JsonElement payload,
        CodiRequestContext context,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestName);
        ArgumentNullException.ThrowIfNull(context);

        var descriptor = registry.Find(kind, requestName);
        if (descriptor is null || (descriptor.IsLocalOnly && channel != CodiRequestChannel.Local))
        {
            throw CodiRequestException.NotFound(kind, requestName);
        }

        if (!context.Allows(descriptor.Policy))
        {
            // Manca l'identità o manca il permesso: sono due risposte diverse (§10.7).
            throw context.IsAuthenticated
                ? CodiRequestException.PolicyDenied(descriptor.Name, descriptor.Policy)
                : CodiRequestException.AuthenticationRequired(descriptor.Name);
        }

        var request = Deserialize(descriptor, payload);
        if (request is ICodiContextualRequest contextualRequest)
        {
            request = contextualRequest.WithContext(context);
        }

        return await descriptor
            .Invoker
            .InvokeAsync(services, request, cancellationToken)
            .ConfigureAwait(false);
    }

    private static object Deserialize(CodiRequestDescriptor descriptor, JsonElement payload)
    {
        // Il corpo di un comando o di una query è un oggetto JSON: qualunque altra forma è un
        // client non allineato al record, non un valore da correggere (§10.1).
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw CodiRequestException.InvalidPayload(descriptor.Name);
        }

        try
        {
            return JsonSerializer.Deserialize(payload, descriptor.RequestType, CodiJson.Payload)
                ?? throw CodiRequestException.InvalidPayload(descriptor.Name);
        }
        catch (JsonException exception)
        {
            throw CodiRequestException.InvalidPayload(descriptor.Name, exception);
        }
    }
}
