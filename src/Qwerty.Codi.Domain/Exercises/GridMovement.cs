namespace Qwerty.Codi.Domain.Exercises;

/// <summary>Verso della rotazione del blocco <c>aula_turn</c> (§13.4).</summary>
public enum GridTurn
{
    Left,
    Right,
}

/// <summary>
/// Effetti di un passo e di una rotazione sul personaggio (§13.2, §13.4). Stanno nel dominio, e non
/// nell'interprete, perché sono regole della versione <c>gridpath-1</c>: l'interprete del browser e
/// quello lato server di §13.8 devono applicare le stesse, e una copia per ciascuno sarebbe un modo
/// silenzioso di avere due giochi diversi con lo stesso nome.
/// </summary>
public static class GridMovement
{
    /// <summary>Rotazione di un comando <c>aula_turn</c>, in gradi (§13.4).</summary>
    public const int TurnDegrees = 90;

    /// <summary>Orientamento dopo una rotazione; la posizione resta quella.</summary>
    public static GridDirection Turn(GridDirection direction, GridTurn turn)
    {
        EnsureDirection(direction);

        var quarters = Enum.GetValues<GridDirection>().Length;
        var step = turn == GridTurn.Right ? 1 : quarters - 1;

        return (GridDirection)(((int)direction + step) % quarters);
    }

    /// <summary>
    /// Cella adiacente nella direzione corrente. Può uscire dalla griglia: chi decide se il passo è
    /// ammesso è il motore, che deve distinguere «fuori griglia» da «ostacolo» nel messaggio
    /// mostrato al bambino (§13.2), e per farlo ha bisogno della cella tentata.
    /// </summary>
    public static GridPosition Step(GridPosition position, GridDirection direction) => direction switch
    {
        GridDirection.North => position with { Y = position.Y - 1 },
        GridDirection.East => position with { X = position.X + 1 },
        GridDirection.South => position with { Y = position.Y + 1 },
        GridDirection.West => position with { X = position.X - 1 },
        _ => throw new ArgumentOutOfRangeException(
            nameof(direction), direction, "L'orientamento non è uno dei quattro della griglia."),
    };

    private static void EnsureDirection(GridDirection direction)
    {
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(
                nameof(direction), direction, "L'orientamento non è uno dei quattro della griglia.");
        }
    }
}
