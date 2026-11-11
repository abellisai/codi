using Qwerty.Codi.Application.Abstractions;

namespace Qwerty.Codi.Application.Persistence;

/// <summary>
/// Entità di persistenza di <c>exercise_draft</c> (§7.3): la bozza di autoraggio di un esercizio,
/// una sola per esercizio. L'identificativo è l'<c>exercise_id</c>, quindi <see cref="Id"/> coincide
/// con la chiave di <c>exercise</c> (§7.3). I documenti JSON sono validati dal database con
/// <c>json_valid</c>; la soluzione di riferimento è facoltativa e riservata al docente.
///
/// È una riga modificabile: <see cref="RowVersion"/> è il controllo di concorrenza ottimistica
/// della bozza, come per le altre tabelle di dominio.
/// </summary>
public sealed class ExerciseDraftEntity : IPersistedEntity<string>
{
    public string Id { get; set; } = string.Empty;
    public string DefinitionJson { get; set; } = string.Empty;
    public string WorkspaceJson { get; set; } = string.Empty;
    public string? ReferenceSolutionJson { get; set; }
    public long CreatedAtUtcMs { get; set; }
    public long UpdatedAtUtcMs { get; set; }
    public long RowVersion { get; set; }
}
