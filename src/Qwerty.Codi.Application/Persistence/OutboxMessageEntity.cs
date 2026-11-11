using Qwerty.Codi.Application.Abstractions;

namespace Qwerty.Codi.Application.Persistence;

/// <summary>
/// Entità di persistenza di <c>outbox_message</c> (§7, §12.2): la coda dei messaggi già decisi e non
/// ancora distribuiti. Il record esiste perché il commit del comando e il suo invio non sono la
/// stessa cosa: se il processo cade fra i due, il messaggio resta in coda invece di sparire.
///
/// Il completamento non si aggiorna con la concorrenza ottimistica — la riga non ha
/// <c>row_version</c> — perché è il dispatcher outbox a segnarla, con un aggiornamento condizionato
/// su «non ancora processato». <see cref="RowVersion"/> è quindi costante e non mappato.
/// </summary>
public sealed class OutboxMessageEntity : IPersistedEntity<string>
{
    /// <summary>Valore costante esposto da <see cref="RowVersion"/>: la riga non ha versione.</summary>
    public const long ImmutableRowVersion = 1;

    public string Id { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    /// <summary>Nome dell'evento hub (§11.3), per esempio <c>CommandIssued</c>.</summary>
    public string MessageType { get; set; } = string.Empty;

    /// <summary>Documento del messaggio, senza credenziali: è ciò che il dispatcher invierà.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>Istante da cui il messaggio è distribuibile: un ritardo è un'attesa, non un errore.</summary>
    public long AvailableAtUtcMs { get; set; }

    public long AttemptCount { get; set; }

    /// <summary>Istante del tentativo concluso, non della conferma del client (§12.2).</summary>
    public long? ProcessedAtUtcMs { get; set; }

    public string? LastErrorCode { get; set; }

    /// <summary>Non mappato: la tabella non ha una colonna <c>row_version</c>.</summary>
    public long RowVersion { get; set; } = ImmutableRowVersion;
}
