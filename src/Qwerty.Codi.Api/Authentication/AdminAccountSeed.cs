using System.Text.Json;
using Microsoft.Extensions.Options;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Infrastructure.Configuration;

namespace Qwerty.Codi.Api.Authentication;

/// <summary>
/// Inserisce l'account amministrativo iniziale quando il database viene preparato (§9.1).
///
/// È una comodità di sviluppo richiesta esplicitamente il 30 settembre 2026, e **deroga** alla
/// regola di §9.1 che vuole il primo account creato solo da un comando locale con la password
/// fuori dal repository: la password sta nella configurazione dell'ambiente, quindi un ambiente
/// che non imposta <c>Codi:SeedAdmin</c> non crea nulla e il laboratorio non eredita una
/// credenziale conosciuta. Prima della consegna in laboratorio il valore di sviluppo va rimosso.
///
/// L'inserimento passa dalla stessa richiesta <c>BootstrapAdminAccount</c> del comando locale: le
/// regole dell'account iniziale restano in un punto solo, e un amministratore già attivo impedisce
/// l'inserimento invece di duplicarlo.
/// </summary>
public sealed class AdminAccountSeed(
    CodiRequestExecutor executor,
    IOptions<CodiOptions> options,
    ILogger<AdminAccountSeed> logger)
{
    /// <summary>
    /// Crea l'account se la configurazione lo chiede e non esiste un amministratore attivo.
    /// Restituisce <c>true</c> solo quando l'account viene creato: la configurazione assente e
    /// l'account già presente non sono errori, perché il secondo è la condizione di ogni avvio
    /// successivo al primo.
    /// </summary>
    public async Task<bool> EnsureAsync(CancellationToken cancellationToken)
    {
        var seed = options.Value.SeedAdmin;
        if (seed.Username.Length == 0 || seed.Password.Length == 0)
        {
            return false;
        }

        // Il payload passa dal formato dei corpi del dispatcher: il percorso automatico e quello
        // del comando locale non devono differire per come i dati arrivano al caso d'uso.
        using var payload = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new { username = seed.Username, password = seed.Password },
                CodiJson.Payload));

        try
        {
            await executor
                .ExecuteAsync(
                    CodiRequestKind.Command,
                    AnonymousBootstrapRequests.BootstrapAdminAccount,
                    CodiRequestChannel.Local,
                    payload.RootElement,
                    CodiRequestContext.Anonymous,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CodiRequestException exception) when (exception.Code == CodiErrorCodes.BootstrapNotAllowed)
        {
            return false;
        }

        logger.LogInformation(
            "Account amministrativo iniziale inserito dalla configurazione: {Username}.",
            seed.Username);

        return true;
    }
}
