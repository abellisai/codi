namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Impronta e verifica delle password amministrative (§9.1). L'algoritmo e i parametri non
/// appartengono al dominio né ai casi d'uso: il caso d'uso pone la domanda «questa password
/// corrisponde?» e il componente consolidato risponde.
/// </summary>
public interface IAdminPasswordHasher
{
    /// <summary>Impronta da memorizzare. Non è reversibile e non contiene la password.</summary>
    string Hash(string password);

    /// <summary>Vero se la password corrisponde all'impronta memorizzata.</summary>
    bool Verify(string passwordHash, string password);
}
