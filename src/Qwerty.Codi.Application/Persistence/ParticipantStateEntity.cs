using Qwerty.Codi.Application.Abstractions;

namespace Qwerty.Codi.Application.Persistence;

/// <summary>
/// Entità di persistenza di <c>participant_state</c> (§7.4, §6.2): lo stato desiderato
/// materializzato di un partecipante. All'ingresso è la copia del default di classe; poi lo
/// modificano i comandi di regia, che verificano la revisione attesa.
///
/// Non ha un tipo di dominio in questa porzione: qui la riga si crea soltanto, mentre le
/// transizioni — pausa, ripresa, assegnazione, riavvio — arrivano con i comandi che le producono
/// (T4), e con esse l'oggetto che le governa. Scriverle prima significherebbe fissare invarianti
/// senza il caso d'uso che le usa.
/// </summary>
public sealed class ParticipantStateEntity : IPersistedEntity<string>
{
    /// <summary>Testo della vista di attesa, come memorizzato in <c>view</c> (§6.2).</summary>
    public const string WaitingViewText = "Waiting";

    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public long EffectiveRevision { get; set; }
    public string? AssignmentId { get; set; }
    public string? AttemptId { get; set; }
    public string View { get; set; } = WaitingViewText;
    public long InteractionEnabledFlag { get; set; }
    public long TeacherPausedFlag { get; set; }
    public long RecoveryBlockedFlag { get; set; }
    public long ExecutionGeneration { get; set; }
    public string? DirectiveJson { get; set; }
    public string? ResumeContextJson { get; set; }
    public long CreatedAtUtcMs { get; set; }
    public long UpdatedAtUtcMs { get; set; }
    public long RowVersion { get; set; }
}
