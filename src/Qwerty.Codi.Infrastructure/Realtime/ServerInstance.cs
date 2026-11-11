using Qwerty.Codi.Application.Abstractions;

namespace Qwerty.Codi.Infrastructure.Realtime;

/// <summary>
/// Istanza del server in esecuzione (§11.4). L'identificativo si genera all'avvio e l'istante di
/// avvio è quello dell'orologio del processo: entrambi servono al client per riconoscere un riavvio,
/// che invalida epoche e autorizzazioni temporanee, e alla diagnosi.
/// </summary>
internal sealed class ServerInstance : IServerInstance
{
    public ServerInstance(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        InstanceId = Guid.NewGuid();
        StartedAtUtc = timeProvider.GetUtcNow();
    }

    public Guid InstanceId { get; }

    public DateTimeOffset StartedAtUtc { get; }
}
