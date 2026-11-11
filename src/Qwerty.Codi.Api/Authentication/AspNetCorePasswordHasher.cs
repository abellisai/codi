using Microsoft.AspNetCore.Identity;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Domain.Admin;

namespace Qwerty.Codi.Api.Authentication;

/// <summary>
/// Impronta delle password amministrative con il componente di ASP.NET Core (§9.1). Vive nel
/// progetto dell'API perché è il framework condiviso a fornirlo: dominio e casi d'uso vedono
/// soltanto l'astrazione, quindi l'algoritmo si può sostituire senza toccarli.
/// </summary>
internal sealed class AspNetCorePasswordHasher : IAdminPasswordHasher
{
    private readonly PasswordHasher<AdminUser> _hasher = new();

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        // Nel formato adottato l'impronta dipende da password, sale e parametri, non dall'utente:
        // il primo argomento è richiesto dalla firma ma non partecipa al calcolo.
        return _hasher.HashPassword(null!, password);
    }

    public bool Verify(string passwordHash, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentNullException.ThrowIfNull(password);

        var result = _hasher.VerifyHashedPassword(null!, passwordHash, password);

        // Un'impronta calcolata con parametri superati resta valida: la rigenerazione appartiene
        // al momento in cui la password viene comunque reimpostata, non a ogni accesso.
        return result != PasswordVerificationResult.Failed;
    }
}
