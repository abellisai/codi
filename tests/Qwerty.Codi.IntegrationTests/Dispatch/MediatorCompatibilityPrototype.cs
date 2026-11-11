using Mediator;

namespace Qwerty.Codi.IntegrationTests.Dispatch;

/// <summary>
/// Tipi del prototipo di compatibilità di T0 (D-01). Non sono voci del catalogo di
/// §10.4–§10.5 e non hanno policy: servono solo a provare che Mediator 2.1.7 con il suo
/// generatore di sorgenti compili ed esegua su .NET 10. Il dispatcher di §10.1 resta T1.
/// </summary>
public sealed record PrototypeEchoRequest(string Message) : IRequest<string>;

/// <summary>Handler dell'eco: la risposta contiene il testo ricevuto.</summary>
public sealed class PrototypeEchoHandler : IRequestHandler<PrototypeEchoRequest, string>
{
    public ValueTask<string> Handle(PrototypeEchoRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult($"echo:{request.Message}");
}

/// <summary>Richiesta che riporta se il token della richiesta è arrivato fino all'handler.</summary>
public sealed record PrototypeTokenRequest : IRequest<bool>;

/// <summary>Handler della sonda sul token.</summary>
public sealed class PrototypeTokenHandler : IRequestHandler<PrototypeTokenRequest, bool>
{
    public ValueTask<bool> Handle(PrototypeTokenRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(cancellationToken.CanBeCanceled);
}

/// <summary>Notifica con due destinatari, per provare il fan-out del mediator.</summary>
public sealed record PrototypeNotice(string Message) : INotification;

/// <summary>
/// Registro condiviso dai destinatari: evita stato statico mutabile nei test (§20.3).
/// </summary>
public sealed class PrototypeNoticeRecorder
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries => _entries;

    public void Record(string source) => _entries.Add(source);
}

/// <summary>Primo destinatario della notifica.</summary>
public sealed class FirstPrototypeNoticeHandler(PrototypeNoticeRecorder recorder)
    : INotificationHandler<PrototypeNotice>
{
    public ValueTask Handle(PrototypeNotice notification, CancellationToken cancellationToken)
    {
        recorder.Record($"first:{notification.Message}");
        return ValueTask.CompletedTask;
    }
}

/// <summary>Secondo destinatario della notifica.</summary>
public sealed class SecondPrototypeNoticeHandler(PrototypeNoticeRecorder recorder)
    : INotificationHandler<PrototypeNotice>
{
    public ValueTask Handle(PrototypeNotice notification, CancellationToken cancellationToken)
    {
        recorder.Record($"second:{notification.Message}");
        return ValueTask.CompletedTask;
    }
}
