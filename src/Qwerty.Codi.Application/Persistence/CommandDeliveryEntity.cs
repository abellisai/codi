namespace Qwerty.Codi.Application.Persistence;

/// <summary>
/// Entità di persistenza di <c>command_delivery</c> (§7, §12.2): lo stato di consegna di un comando
/// **per destinatario**. Una riga esiste anche per un client offline, perché il server non deduce la
/// consegna dalla presenza: la presenza dice che il trasporto è aperto, non che il player ha
/// applicato lo stato.
///
/// Non implementa <c>IPersistedEntity</c> perché la chiave è **composta** — comando e partecipante —
/// e il CRUD comune è costruito su una chiave sola. Le consegne si leggono per comando e si
/// aggiornano per coppia, con i metodi espliciti del repository.
/// </summary>
public sealed class CommandDeliveryEntity
{
    public string CommandId { get; set; } = string.Empty;

    public string ParticipantId { get; set; } = string.Empty;

    /// <summary>Revisione a cui il comando è stato applicato al destinatario (§6.2).</summary>
    public long EffectiveRevision { get; set; }

    /// <summary>Stato della consegna, fra quelli di §12.2 (<c>Pending</c>, <c>Sent</c>, <c>Applied</c>…).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Vero quando l'applicazione richiede un recupero: non è una normale applicazione.</summary>
    public long RequiresRecoveryFlag { get; set; }

    public long? SentAtUtcMs { get; set; }

    /// <summary>Istante della conferma applicativa (ACK), distinto dalla consegna (§11.4).</summary>
    public long? AckAtUtcMs { get; set; }

    public long? AppliedAtUtcMs { get; set; }

    /// <summary>Codice dell'errore riportato dal client, quando l'esito non è positivo.</summary>
    public string? ErrorCode { get; set; }

    public long SendAttempts { get; set; }
}
