using Qwerty.Codi.Domain.Common;

namespace Qwerty.Codi.Domain.Organization;

/// <summary>
/// Nome di una scuola: ammissibilità e normalizzazione (§7.2). La normalizzazione è la stessa
/// usata dagli altri nomi confrontabili, perché il nome della scuola è la chiave di unicità
/// dell'organizzazione e non una semplice etichetta.
/// </summary>
public static class SchoolName
{
    /// <summary>Lunghezza massima accettata, allineata al vincolo della tabella <c>school</c>.</summary>
    public const int MaxLength = 120;

    /// <summary>Vero se il nome può essere usato da una scuola.</summary>
    public static bool IsValid(string? name) => NameNormalization.IsUsable(name, MaxLength);

    /// <summary>Forma normalizzata su cui si decide l'unicità delle scuole.</summary>
    public static string Normalize(string name) => NameNormalization.Normalize(name);
}
