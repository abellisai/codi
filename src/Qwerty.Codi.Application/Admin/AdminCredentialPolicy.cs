namespace Qwerty.Codi.Application.Admin;

/// <summary>
/// Politiche dell'accesso amministrativo (§9.1). Stanno nel caso d'uso e non nel dominio perché
/// sono decisioni di prodotto: la credenziale sa quanto dura solo perché glielo si dice.
/// </summary>
public static class AdminCredentialPolicy
{
    /// <summary>
    /// Durata proposta da §9.1 per il cookie amministrativo: otto ore, con scadenza assoluta. Una
    /// giornata scolastica sta dentro la finestra senza rinnovi, e una sessione dimenticata scade
    /// prima della lezione successiva.
    /// </summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
}
