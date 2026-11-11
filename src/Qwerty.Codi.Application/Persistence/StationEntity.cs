using Qwerty.Codi.Application.Abstractions;

namespace Qwerty.Codi.Application.Persistence;

/// <summary>
/// Entità di persistenza di <c>station</c> (§7.2): la postazione fisica da cui un alunno entra.
/// Non è un concetto didattico e non ha un tipo di dominio: conserva l'impronta della chiave
/// casuale del browser — mai un'impronta digitale del dispositivo — la classe in cui la postazione
/// è stata vista e l'ultimo contatto, che serve a capire se una postazione è ancora in uso.
/// </summary>
public sealed class StationEntity : IPersistedEntity<string>
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Classe in cui la postazione è stata vista; assente quando non è nota.</summary>
    public string? ClassroomId { get; set; }

    /// <summary>Etichetta informativa assegnata dal docente; non identifica nulla.</summary>
    public string? Label { get; set; }

    /// <summary>Impronta della chiave casuale generata dal browser (§9.5).</summary>
    public string BrowserKeyHash { get; set; } = string.Empty;

    /// <summary>Ultimo contatto registrato, in epoch millisecondi; assente se non c'è stato.</summary>
    public long? LastSeenAtUtcMs { get; set; }

    public long CreatedAtUtcMs { get; set; }
    public long UpdatedAtUtcMs { get; set; }
    public long RowVersion { get; set; }
}
