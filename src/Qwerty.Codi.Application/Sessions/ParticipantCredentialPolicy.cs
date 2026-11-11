namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Politica della credenziale dell'alunno (§9.2). Sta qui e non nel dominio perché è una scelta di
/// prodotto: il dominio sa emettere una credenziale di durata dichiarata, non quanto deve durare.
/// </summary>
public static class ParticipantCredentialPolicy
{
    /// <summary>
    /// Durata massima della credenziale di un partecipante: dodici ore, comunque non oltre la
    /// sessione, perché la scoperta e la regia non concedono permessi operativi dopo <c>EndSession</c>.
    /// </summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
}
