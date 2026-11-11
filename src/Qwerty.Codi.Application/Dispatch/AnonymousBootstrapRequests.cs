namespace Qwerty.Codi.Application.Dispatch;

/// <summary>
/// Elenco chiuso delle richieste ammesse a <see cref="CodiPolicy.AnonymousBootstrap"/> (§9.3,
/// §10.4). Il registro rifiuta l'avvio se una richiesta anonima non compare qui: l'apertura
/// anonima è un'eccezione dichiarata, non una conseguenza di una svista.
/// </summary>
public static class AnonymousBootstrapRequests
{
    /// <summary>Ingresso idempotente di §9.4.</summary>
    public const string JoinSession = "JoinSession";

    /// <summary>Accesso amministrativo con cookie (§9.1).</summary>
    public const string LoginAdmin = "LoginAdmin";

    /// <summary>Primo account amministrativo: solo riga di comando locale, mai via HTTP.</summary>
    public const string BootstrapAdminAccount = "BootstrapAdminAccount";

    /// <summary>Nomi ammessi a <see cref="CodiPolicy.AnonymousBootstrap"/>, senza eccezioni.</summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        JoinSession,
        LoginAdmin,
        BootstrapAdminAccount,
    };

    /// <summary>
    /// Nomi anonimi raggiungibili dal dispatcher HTTP. È l'insieme di
    /// <see cref="All"/> meno <see cref="BootstrapAdminAccount"/>, che resta locale.
    /// </summary>
    public static IReadOnlySet<string> ExposedOverHttp { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        JoinSession,
        LoginAdmin,
    };
}
