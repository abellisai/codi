using Qwerty.Codi.Api.Authentication;
using Qwerty.Codi.Api.Endpoints;
using Qwerty.Codi.Api.Hubs;

namespace Qwerty.Codi.Api.Configuration;

/// <summary>Pipeline ed endpoint condivisi dall'avvio reale e dai test HTTP di regressione.</summary>
public static class CodiApplicationExtensions
{
    public static WebApplication UseCodiApi(this WebApplication application)
    {
        application.UseExceptionHandler();
        application.UseCodiWeb();
        application.UseAuthentication();
        // Gli hub dichiarano `[Authorize]`: senza la middleware di autorizzazione la loro policy
        // non verrebbe applicata e la connessione risulterebbe sempre autorizzata.
        application.UseAuthorization();
        application.UseCodiAntiforgeryValidation();
        application.MapGet("/health/live", () => Results.Ok());
        application.MapHealthChecks("/health/ready");
        application.MapCodiApiEndpoints();
        application.MapCodiHubs();
        return application;
    }
}
