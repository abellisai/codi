namespace Qwerty.Codi.IntegrationTests.Persistence;

/// <summary>
/// Riga del prototipo di T0: serve solo a verificare che RepoDB mappi colonne
/// <c>snake_case</c> su proprietà PascalCase (§7.1). Non è una tabella del dizionario di §7.
/// L'identificativo resta la stringa canonica minuscola del UUID: RepoDB non converte da solo
/// una colonna <c>TEXT</c> in <see cref="System.Guid"/>, quindi la conversione appartiene al
/// livello di mapping dei repository (§7.1, A-06).
/// </summary>
public sealed class PrototypeRow
{
    public string Id { get; set; } = string.Empty;

    public long CreatedAtUtcMs { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string? Note { get; set; }

    public string PayloadJson { get; set; } = string.Empty;

    public long RowVersion { get; set; }
}
