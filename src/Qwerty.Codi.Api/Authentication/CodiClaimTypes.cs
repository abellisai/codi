namespace Qwerty.Codi.Api.Authentication;

/// <summary>
/// Nomi dei claim del ticket amministrativo. Sono il legame fra il cookie e la riga
/// <c>credential_session</c>: l'identificativo dice quale credenziale è stata emessa e la marca
/// dice se è ancora quella valida (§9.2).
/// </summary>
internal static class CodiClaimTypes
{
    /// <summary>Identificativo della credenziale persistita.</summary>
    public const string CredentialSessionId = "codi:credential_session";

    /// <summary>Marca di sicurezza della credenziale, confrontata con quella memorizzata.</summary>
    public const string CredentialStamp = "codi:credential_stamp";
}
