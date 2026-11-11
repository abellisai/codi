namespace Qwerty.Codi.IntegrationTests;

/// <summary>
/// Tempo fisso per i test dipendenti dal tempo (§20.3). Senza questo, un valore come
/// <c>applied_at_utc_ms</c> si può solo verificare come «circa adesso», cioè non si verifica.
/// </summary>
public sealed class TestTimeProvider : TimeProvider
{
    /// <summary>Istante restituito da <see cref="GetUtcNow"/>.</summary>
    public static readonly DateTimeOffset Instant =
        new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Instant;
}
