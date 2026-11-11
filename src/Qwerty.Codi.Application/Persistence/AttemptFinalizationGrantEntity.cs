using Qwerty.Codi.Application.Abstractions;

namespace Qwerty.Codi.Application.Persistence;

/// <summary>Permesso per una sola scrittura conclusiva del tentativo (§12.7).</summary>
public sealed class AttemptFinalizationGrantEntity : IPersistedEntity<string>
{
    public string Id { get; set; } = string.Empty;
    public string AttemptId { get; set; } = string.Empty;
    public string CredentialSessionId { get; set; } = string.Empty;
    public long ConnectionEpoch { get; set; }
    public long ExecutionGeneration { get; set; }
    public long BaseRowVersion { get; set; }
    public long ExpiresAtUtcMs { get; set; }
    public long? UsedAtUtcMs { get; set; }
    public string? SaveId { get; set; }
    public long RowVersion { get; set; } = 1;
}
