using Qwerty.Codi.Api.Authentication;
using Qwerty.Codi.Api.Dispatch;

namespace Qwerty.Codi.Api.Endpoints.Public;

internal static class PublicEndpoints
{
    public static RouteGroupBuilder MapCodiPublicEndpoints(this RouteGroupBuilder group)
    {
        group.MapCodiAntiforgeryBootstrap();

        // §10.3: la scoperta pubblica precede il join e non passa dal dispatcher; i suoi endpoint
        // dedicati si mappano accanto al dispatcher dell'area, senza aggiungere rotte per richieste
        // del catalogo.
        group.MapCodiPublicDiscovery();
        group.MapCodiPublicParticipants();
        group.MapCodiPublicAttempts();
        group.MapCodiDispatcher(CodiHttpRequestArea.Public);

        return group;
    }
}
