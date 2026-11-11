using Qwerty.Codi.Application.Abstractions;

namespace Qwerty.Codi.Application.Persistence;

/// <summary>
/// Entità di persistenza di <c>session_command</c> (§7, §12.1): il comando di regia come è stato
/// accettato — nome pubblico, ambito, revisione risultante e payload applicato. È ciò che permette
/// di ricostruire «che cosa è stato chiesto alla classe», anche dopo il riavvio del processo.
///
/// La riga è immutabile: un comando non si corregge, si emette di nuovo. <see cref="RowVersion"/>
/// esiste perché <see cref="IPersistedEntity{TIdentity}"/> lo richiede, ma non è mappato ad alcuna
/// colonna e non è un token di concorrenza.
/// </summary>
public sealed class SessionCommandEntity : IPersistedEntity<string>
{
    /// <summary>Valore costante esposto da <see cref="RowVersion"/>: la riga non si aggiorna.</summary>
    public const long ImmutableRowVersion = 1;

    /// <summary>Identificativo del comando, citato dall'envelope e dalle consegne (§11.4).</summary>
    public string Id { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    /// <summary>Amministratore che ha impartito il comando; nullo per le azioni di sistema.</summary>
    public string? IssuedByAdminId { get; set; }

    /// <summary>Nome pubblico del comando di §10.4, non il nome del tipo C#.</summary>
    public string CommandName { get; set; } = string.Empty;

    /// <summary>Ambito di §6.5: <c>Class</c> o <c>Participant</c>.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>Destinatario del comando individuale; nullo per l'ambito di classe.</summary>
    public string? TargetParticipantId { get; set; }

    /// <summary>Revisione della sessione risultante dal comando.</summary>
    public long Revision { get; set; }

    /// <summary>Stato desiderato applicato, nello stesso formato dei DTO (§10.2).</summary>
    public string PayloadJson { get; set; } = string.Empty;

    public long IssuedAtUtcMs { get; set; }

    /// <summary>Non mappato: la tabella non ha una colonna <c>row_version</c>.</summary>
    public long RowVersion { get; set; } = ImmutableRowVersion;
}
