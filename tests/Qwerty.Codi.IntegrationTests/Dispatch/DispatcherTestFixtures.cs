using Mediator;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.IntegrationTests.Dispatch;

/// <summary>
/// Voci di prova del registro di §4.4. Nessuna di queste è un nome di §10.4–§10.5: servono a
/// provare scoperta, policy, elenco chiuso dell'accesso anonimo e controllo del canale. I tipi
/// volutamente incoerenti restano qui perché i test compongono il catalogo con
/// <c>FromTypes</c>, mentre <c>FromAssemblies</c> scarta i tipi non pubblici o non richieste.
/// </summary>
public sealed record EchoCommand(string Text) : ICodiCommand<string>
{
    public static string RequestName => "PrototypeEcho";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

public sealed record ProbeDto(string? Filter, bool TokenWasActive);

public sealed record ProbeQuery(string? Filter = null) : ICodiQuery<ProbeDto>
{
    public static string RequestName => "PrototypeProbe";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

public sealed record JoinSessionCommand(string DisplayName) : ICodiCommand<string>
{
    public static string RequestName => AnonymousBootstrapRequests.JoinSession;

    public static CodiPolicy Policy => CodiPolicy.AnonymousBootstrap;
}

public sealed record BootstrapAdminAccountCommand(string Username) : ICodiCommand<string>, ILocalOnlyRequest
{
    public static string RequestName => AnonymousBootstrapRequests.BootstrapAdminAccount;

    public static CodiPolicy Policy => CodiPolicy.AnonymousBootstrap;
}

/// <summary>
/// Invocazione controllata: registra cosa ha ricevuto l'handler e restituisce una risposta
/// configurata. Il progetto di test non può ospitare il generatore di Mediator — la
/// registrazione diventerebbe ambigua fra due assembly — quindi il percorso di esecuzione del
/// dispatcher si verifica con questa collaborazione sostituita, mentre la registrazione reale
/// del mediator resta verificata dalla composizione dell'applicazione.
/// </summary>
public sealed class RecordingInvoker(object? response) : ICodiRequestInvoker
{
    private readonly List<object> _requests = [];
    private readonly List<CancellationToken> _tokens = [];

    /// <summary>Richieste consegnate all'handler, nell'ordine di esecuzione.</summary>
    public IReadOnlyList<object> Requests => _requests;

    /// <summary>Token ricevuti, per verificare la propagazione della cancellazione.</summary>
    public IReadOnlyList<CancellationToken> Tokens => _tokens;

    public ValueTask<object?> InvokeAsync(
        IServiceProvider services,
        object request,
        CancellationToken cancellationToken)
    {
        _requests.Add(request);
        _tokens.Add(cancellationToken);

        return ValueTask.FromResult(response);
    }
}

/// <summary>Richiesta del mediator senza metadati del dispatcher: il catalogo deve rifiutarla.</summary>
public sealed record RequestWithoutPolicy(string Text) : IRequest<string>;

/// <summary>Nome pubblico non conforme a §10.1.</summary>
public sealed record BadNameCommand : ICodiCommand<string>
{
    public static string RequestName => "echo test";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>Accesso anonimo fuori dall'elenco chiuso di §10.4.</summary>
public sealed record UnlistedAnonymousQuery : ICodiQuery<string>
{
    public static string RequestName => "PrototypeUnlisted";

    public static CodiPolicy Policy => CodiPolicy.AnonymousBootstrap;
}

/// <summary>Nome dell'elenco chiuso con una policy che non è quella anonima.</summary>
public sealed record LoginAdminWithAdminPolicyCommand : ICodiCommand<string>
{
    public static string RequestName => AnonymousBootstrapRequests.LoginAdmin;

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>Richiesta anonima locale che non è il bootstrap amministrativo.</summary>
public sealed record LocalJoinCommand : ICodiCommand<string>, ILocalOnlyRequest
{
    public static string RequestName => AnonymousBootstrapRequests.JoinSession;

    public static CodiPolicy Policy => CodiPolicy.AnonymousBootstrap;
}

/// <summary>Bootstrap amministrativo dichiarato raggiungibile via HTTP.</summary>
public sealed record BootstrapOverHttpCommand : ICodiCommand<string>
{
    public static string RequestName => AnonymousBootstrapRequests.BootstrapAdminAccount;

    public static CodiPolicy Policy => CodiPolicy.AnonymousBootstrap;
}

/// <summary>Prima delle due richieste con lo stesso nome pubblico.</summary>
public sealed record DuplicateFirstCommand : ICodiCommand<string>
{
    public static string RequestName => "PrototypeDuplicate";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>Seconda delle due richieste con lo stesso nome pubblico.</summary>
public sealed record DuplicateSecondCommand : ICodiCommand<string>
{
    public static string RequestName => "PrototypeDuplicate";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}
