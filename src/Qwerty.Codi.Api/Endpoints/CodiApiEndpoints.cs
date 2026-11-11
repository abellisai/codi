using Qwerty.Codi.Api.Endpoints.Admin;
using Qwerty.Codi.Api.Endpoints.Public;

namespace Qwerty.Codi.Api.Endpoints;

/// <summary>Un backend, due gruppi HTTP versionati, casi d'uso condivisi.</summary>
internal static class CodiApiEndpoints
{
    public static IEndpointRouteBuilder MapCodiApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup(CodiHttpRequestAreas.AdminPrefix).WithTags("Admin").MapCodiAdminEndpoints();
        endpoints.MapGroup(CodiHttpRequestAreas.PublicPrefix).WithTags("Public").MapCodiPublicEndpoints();

        return endpoints;
    }
}
