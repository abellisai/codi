namespace Qwerty.Codi.Infrastructure.Migrations;

/// <summary>
/// Fotografia dello schema rispetto agli script distribuiti (§8.3): versione applicata più alta,
/// versione attesa, migrazioni ancora da applicare e divergenze rilevate. Una divergenza è un
/// guasto, non un lavoro da fare: significa che il database contiene qualcosa che questa
/// versione dell'applicazione non riconosce.
/// </summary>
public sealed record MigrationStatus(
    int CurrentVersion,
    int ExpectedVersion,
    IReadOnlyList<int> PendingVersions,
    IReadOnlyList<string> Divergences)
{
    /// <summary>
    /// Vero soltanto se non ci sono divergenze né migrazioni in attesa: è la condizione che
    /// rende il database utilizzabile da questa versione dell'applicazione.
    /// </summary>
    public bool IsUpToDate => Divergences.Count == 0 && PendingVersions.Count == 0;
}
