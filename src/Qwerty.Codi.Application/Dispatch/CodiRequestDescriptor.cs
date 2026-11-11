using Mediator;

namespace Qwerty.Codi.Application.Dispatch;

/// <summary>
/// Voce del registro di §4.4: metadati pubblici di una richiesta e modo per eseguirla. Il
/// descrittore è immutabile e viene costruito una volta sola all'avvio, insieme alla chiusura
/// generica dell'invocazione.
/// </summary>
public sealed record CodiRequestDescriptor(
    string Name,
    CodiRequestKind Kind,
    CodiPolicy Policy,
    Type RequestType,
    Type ResponseType,
    bool IsLocalOnly,
    ICodiRequestInvoker Invoker);
