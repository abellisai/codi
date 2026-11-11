using Qwerty.Codi.Application.Dto;
using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Application.Admin;

/// <summary>
/// Accesso amministrativo (§9.1). È una richiesta anonima e raggiungibile via HTTP: chi non ha
/// ancora una credenziale deve poterla ottenere, ed è una delle due sole richieste anonime
/// pubblicate sull'API.
/// </summary>
public sealed record LoginAdminCommand(string Username, string Password)
    : ICodiCommand<AdminSignedInDto>
{
    public static string RequestName => AnonymousBootstrapRequests.LoginAdmin;

    public static CodiPolicy Policy => CodiPolicy.AnonymousBootstrap;
}

/// <summary>
/// Uscita amministrativa: revoca la credenziale dell'accesso corrente (§9.1). Il corpo è vuoto;
/// l'identità è associata dall'esecutore usando il contesto verificato dal trasporto.
/// La richiesta richiede la policy <see cref="CodiPolicy.Admin"/>.
/// </summary>
public sealed record LogoutAdminCommand : ICodiCommand<bool>, ICodiContextualRequest
{
    public static string RequestName => "LogoutAdmin";

    public static CodiPolicy Policy => CodiPolicy.Admin;

    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Primo account amministrativo dell'installazione (§9.1). È una richiesta anonima e **locale**:
/// non ha una rotta HTTP, perché il primo account si crea da riga di comando, quando non esiste
/// ancora nessun amministratore, e da quel momento non si ricrea più. La password viaggia nel
/// payload perché il canale locale è lo stesso processo, e non deve finire nei log.
/// </summary>
public sealed record BootstrapAdminAccountCommand(string Username, string Password)
    : ICodiCommand<AdminAccountCreatedDto>, ILocalOnlyRequest
{
    public static string RequestName => AnonymousBootstrapRequests.BootstrapAdminAccount;

    public static CodiPolicy Policy => CodiPolicy.AnonymousBootstrap;
}
