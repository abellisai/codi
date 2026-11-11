using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Api.Endpoints;

/// <summary>Area HTTP: limita il catalogo esposto, senza sostituire la policy della richiesta.</summary>
internal enum CodiHttpRequestArea
{
    Admin,
    Public,
}

internal static class CodiHttpRequestAreas
{
    public const string AdminPrefix = "/api/v1/admin";
    public const string PublicPrefix = "/api/v1/public";

    /// <summary>
    /// Le richieste locali non sono mai HTTP; quelle anonime appartengono all'area del loro
    /// ingresso. Policy nuove o non riconosciute restano escluse finché vengono classificate.
    /// </summary>
    public static bool Allows(this CodiHttpRequestArea area, CodiRequestDescriptor descriptor)
    {
        if (descriptor.IsLocalOnly)
        {
            return false;
        }

        return descriptor.Policy switch
        {
            CodiPolicy.Admin or CodiPolicy.SessionController => area == CodiHttpRequestArea.Admin,
            CodiPolicy.AdmittedParticipant or CodiPolicy.ActiveParticipantConnection or CodiPolicy.FinalizingParticipant
                => area == CodiHttpRequestArea.Public,
            CodiPolicy.AnonymousBootstrap => descriptor.Name switch
            {
                AnonymousBootstrapRequests.LoginAdmin => area == CodiHttpRequestArea.Admin,
                AnonymousBootstrapRequests.JoinSession => area == CodiHttpRequestArea.Public,
                _ => false,
            },
            _ => false,
        };
    }
}
