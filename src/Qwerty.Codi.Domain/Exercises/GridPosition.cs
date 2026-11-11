namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Cella della griglia di §13.2: origine in alto a sinistra, <c>x</c> crescente verso destra e
/// <c>y</c> verso il basso. Il personaggio occupa una sola cella, quindi una posizione è una cella
/// e non ha coordinate frazionarie.
/// </summary>
public readonly record struct GridPosition(int X, int Y)
{
    /// <summary>Vero se la cella sta dentro una griglia delle dimensioni indicate.</summary>
    public bool IsInside(int width, int height) =>
        X >= 0 && Y >= 0 && X < width && Y < height;

    /// <summary>Cella in forma breve, per i messaggi di validazione e i test.</summary>
    public override string ToString() => $"({X}, {Y})";
}
