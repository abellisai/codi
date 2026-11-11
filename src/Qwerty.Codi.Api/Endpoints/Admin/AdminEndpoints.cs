using Qwerty.Codi.Api.Authentication;
using Qwerty.Codi.Api.Dispatch;

namespace Qwerty.Codi.Api.Endpoints.Admin;

internal static class AdminEndpoints
{
    public static RouteGroupBuilder MapCodiAdminEndpoints(this RouteGroupBuilder group)
    {
        group.MapCodiAntiforgeryBootstrap();
        group.MapCodiDispatcher(CodiHttpRequestArea.Admin);
        group.MapCodiAdminAuthentication();

        return group;
    }
}
