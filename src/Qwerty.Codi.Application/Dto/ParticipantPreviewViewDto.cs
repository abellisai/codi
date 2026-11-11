namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Anteprima letta dal docente, con la freschezza e il confronto col progetto persistito.
/// <see cref="Partial"/> è vero quando l'anteprima non corrisponde al progetto salvato: in quel caso
/// la si mostra come parziale, senza evidenziare un blocco che potrebbe non essere quello corrente
/// (§15.5).
/// </summary>
public sealed record ParticipantPreviewViewDto(Guid ParticipantId, Guid AttemptId, long ProjectRevision,
    string WorkspaceHash, string WorkspaceJson, DateTimeOffset ReceivedAtUtc, string? AttemptWorkspaceHash)
{
    public bool Partial =>
        AttemptWorkspaceHash is null ||
        !string.Equals(WorkspaceHash, AttemptWorkspaceHash, StringComparison.Ordinal);
}
