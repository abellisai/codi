namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Segnale in-process che sveglia il dispatcher outbox quando un comando ha appena messo un
/// messaggio in coda (§12.1). Senza, il dispatcher lo troverebbe solo al giro successivo: il
/// segnale non sostituisce la lettura della coda — che resta la verità — ma le evita un'attesa.
/// </summary>
public interface IOutboxSignal
{
    /// <summary>Segnala che in coda c'è almeno un messaggio nuovo.</summary>
    void Notify();

    /// <summary>
    /// Attende un segnale o la scadenza, e restituisce <c>true</c> se un segnale è arrivato. Il
    /// dispatcher usa comunque una scadenza breve: una coda rimasta piena dopo un errore non deve
    /// restare ferma in attesa di un segnale che non arriverà.
    /// </summary>
    Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
