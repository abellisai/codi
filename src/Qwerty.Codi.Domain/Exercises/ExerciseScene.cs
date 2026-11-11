namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Scena di un esercizio <c>GridPath</c> (§13.2): una griglia finita con celle libere, ostacoli e
/// un traguardo. La griglia è validata alla costruzione, quindi una scena che esiste è sempre
/// percorribile «sulla carta»: partenza e traguardo dentro i bordi, non su un ostacolo e non
/// coincidenti.
/// </summary>
public sealed class ExerciseScene
{
    private ExerciseScene(
        int width,
        int height,
        GridPosition start,
        GridDirection startDirection,
        IReadOnlyList<GridPosition> obstacles,
        GridPosition goal)
    {
        Width = width;
        Height = height;
        Start = start;
        StartDirection = startDirection;
        Obstacles = obstacles;
        Goal = goal;
    }

    /// <summary>Numero di colonne.</summary>
    public int Width { get; }

    /// <summary>Numero di righe.</summary>
    public int Height { get; }

    /// <summary>Cella di partenza del personaggio.</summary>
    public GridPosition Start { get; }

    /// <summary>Orientamento iniziale: le rotazioni successive cambiano solo l'orientamento.</summary>
    public GridDirection StartDirection { get; }

    /// <summary>Celle occupate da ostacoli, senza ripetizioni.</summary>
    public IReadOnlyList<GridPosition> Obstacles { get; }

    /// <summary>Cella del traguardo.</summary>
    public GridPosition Goal { get; }

    /// <summary>Vero se la cella è occupata da un ostacolo.</summary>
    public bool IsObstacle(GridPosition position) => Obstacles.Contains(position);

    public static ExerciseScene Create(
        int width,
        int height,
        GridPosition start,
        GridDirection startDirection,
        IReadOnlyList<GridPosition>? obstacles,
        GridPosition goal)
    {
        EnsureSide(width, nameof(width));
        EnsureSide(height, nameof(height));

        if (!start.IsInside(width, height))
        {
            throw new ArgumentException(
                $"La partenza {start} è fuori dalla griglia {width}×{height}.", nameof(start));
        }

        if (!goal.IsInside(width, height))
        {
            throw new ArgumentException(
                $"Il traguardo {goal} è fuori dalla griglia {width}×{height}.", nameof(goal));
        }

        if (start == goal)
        {
            // Un esercizio già risolto non è un esercizio: non c'è nulla da programmare e il
            // bambino vedrebbe un successo immediato.
            throw new ArgumentException(
                "La partenza e il traguardo coincidono: l'esercizio sarebbe già risolto.", nameof(goal));
        }

        if (!Enum.IsDefined(startDirection))
        {
            throw new ArgumentOutOfRangeException(
                nameof(startDirection), startDirection,
                "L'orientamento di partenza non è uno dei quattro della griglia.");
        }

        var cells = new List<GridPosition>();
        foreach (var obstacle in obstacles ?? [])
        {
            if (!obstacle.IsInside(width, height))
            {
                throw new ArgumentException(
                    $"L'ostacolo {obstacle} è fuori dalla griglia {width}×{height}.", nameof(obstacles));
            }

            if (obstacle == start)
            {
                throw new ArgumentException(
                    "Un ostacolo non può occupare la cella di partenza.", nameof(obstacles));
            }

            if (obstacle == goal)
            {
                throw new ArgumentException(
                    "Un ostacolo non può occupare la cella del traguardo.", nameof(obstacles));
            }

            if (cells.Contains(obstacle))
            {
                throw new ArgumentException($"L'ostacolo {obstacle} è ripetuto.", nameof(obstacles));
            }

            cells.Add(obstacle);
        }

        return new ExerciseScene(width, height, start, startDirection, cells, goal);
    }

    private static void EnsureSide(int side, string parameterName)
    {
        if (side < GridPathEngine.MinSide || side > GridPathEngine.MaxSide)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, side,
                $"Il lato della griglia deve stare fra {GridPathEngine.MinSide} e {GridPathEngine.MaxSide}.");
        }
    }
}
