namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Limiti didattici della definizione (§13.2). Gli intervalli ammessi sono una scelta locale — la
/// specifica indica un esempio e non dei confini — e servono a rifiutare una definizione che
/// prometterebbe al motore un budget che non può rispettare, o che chiederebbe al bambino un
/// programma più grande dello spazio in cui comporlo.
/// </summary>
public sealed class ExerciseLimits
{
    public const int MinMaxBlocks = 1;
    public const int MaxMaxBlocks = 500;
    public const int MinMaxNestingDepth = 1;
    public const int MaxMaxNestingDepth = 32;
    public const long MinMaxExecutionSteps = 1;
    public const long MaxMaxExecutionSteps = 1_000_000;
    public const long MinMaxActiveDurationMs = 1;
    public const long MaxMaxActiveDurationMs = 3_600_000;

    private ExerciseLimits(long maxBlocks, long maxNestingDepth, long maxExecutionSteps, long maxActiveDurationMs)
    {
        MaxBlocks = maxBlocks;
        MaxNestingDepth = maxNestingDepth;
        MaxExecutionSteps = maxExecutionSteps;
        MaxActiveDurationMs = maxActiveDurationMs;
    }

    /// <summary>Numero massimo di blocchi del progetto, compresi quelli scollegati (§13.4).</summary>
    public long MaxBlocks { get; }

    /// <summary>Profondità massima di annidamento, per esempio delle ripetizioni dentro ripetizioni.</summary>
    public long MaxNestingDepth { get; }

    /// <summary>Passi massimi di esecuzione: è il limite che rende interrompibile un programma che non termina.</summary>
    public long MaxExecutionSteps { get; }

    /// <summary>Durata massima di esecuzione attiva, in millisecondi; la pausa non la consuma (§13.7).</summary>
    public long MaxActiveDurationMs { get; }

    public static ExerciseLimits Create(
        long maxBlocks,
        long maxNestingDepth,
        long maxExecutionSteps,
        long maxActiveDurationMs)
    {
        EnsureRange(maxBlocks, MinMaxBlocks, MaxMaxBlocks, nameof(maxBlocks), "Il numero massimo di blocchi");
        EnsureRange(
            maxNestingDepth, MinMaxNestingDepth, MaxMaxNestingDepth, nameof(maxNestingDepth),
            "La profondità massima di annidamento");
        EnsureRange(
            maxExecutionSteps, MinMaxExecutionSteps, MaxMaxExecutionSteps, nameof(maxExecutionSteps),
            "Il numero massimo di passi di esecuzione");
        EnsureRange(
            maxActiveDurationMs, MinMaxActiveDurationMs, MaxMaxActiveDurationMs, nameof(maxActiveDurationMs),
            "La durata massima di esecuzione attiva");

        return new ExerciseLimits(maxBlocks, maxNestingDepth, maxExecutionSteps, maxActiveDurationMs);
    }

    private static void EnsureRange(long value, long min, long max, string parameterName, string label)
    {
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, value, $"{label} deve stare fra {min} e {max}.");
        }
    }
}
