using Qwerty.Codi.Domain.Exercises;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Exercises;

/// <summary>
/// Regole della scena di §13.2: confini della griglia fra 2×2 e 20×20, partenza e traguardo dentro
/// i bordi, non su un ostacolo e non coincidenti, ostacoli senza ripetizioni. Una scena che esiste
/// è quindi percorribile, e questi casi sono i rifiuti che lo garantiscono.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ExerciseSceneTests
{
    [Fact]
    public void Create_ConScenaDiSezione132_ConservaGrigliaPartenzaOstacoliETraguardo()
    {
        var scene = Scene();

        Assert.Equal(5, scene.Width);
        Assert.Equal(4, scene.Height);
        Assert.Equal(new GridPosition(0, 1), scene.Start);
        Assert.Equal(GridDirection.East, scene.StartDirection);
        Assert.Equal([new GridPosition(2, 2)], scene.Obstacles);
        Assert.Equal(new GridPosition(3, 1), scene.Goal);
        Assert.True(scene.IsObstacle(new GridPosition(2, 2)));
        Assert.False(scene.IsObstacle(new GridPosition(1, 1)));
    }

    [Fact]
    public void Create_SenzaOstacoli_AccettaUnPercorsoLibero()
    {
        var scene = Scene(obstacles: []);

        Assert.Empty(scene.Obstacles);
    }

    [Theory]
    [InlineData(1, 4, "width")]
    [InlineData(21, 4, "width")]
    [InlineData(5, 1, "height")]
    [InlineData(5, 21, "height")]
    public void Create_ConLatoFuoriIntervallo_Rifiuta(int width, int height, string expectedParameter)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => ExerciseScene.Create(
                width, height, new GridPosition(0, 0), GridDirection.East, [], new GridPosition(1, 0)));

        Assert.Equal(expectedParameter, exception.ParamName);
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(0, 5)]
    [InlineData(4, -1)]
    public void Create_ConPartenzaFuoriDallaGriglia_Rifiuta(int startX, int startY)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => ExerciseScene.Create(
                4, 4, new GridPosition(startX, startY), GridDirection.East, [], new GridPosition(3, 3)));

        Assert.Equal("start", exception.ParamName);
    }

    [Fact]
    public void Create_ConTraguardoFuoriDallaGriglia_Rifiuta()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => ExerciseScene.Create(
                4, 4, new GridPosition(0, 0), GridDirection.East, [], new GridPosition(4, 0)));

        Assert.Equal("goal", exception.ParamName);
    }

    [Fact]
    public void Create_ConPartenzaUgualeAlTraguardo_RifiutaUnEsercizioGiaRisolto()
    {
        var cell = new GridPosition(1, 1);

        var exception = Assert.Throws<ArgumentException>(
            () => ExerciseScene.Create(4, 4, cell, GridDirection.East, [], cell));

        Assert.Equal("goal", exception.ParamName);
    }

    [Fact]
    public void Create_ConOrientamentoNonDefinito_Rifiuta()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => ExerciseScene.Create(
                4, 4, new GridPosition(0, 0), (GridDirection)9, [], new GridPosition(1, 0)));

        Assert.Equal("startDirection", exception.ParamName);
    }

    [Fact]
    public void Create_ConOstacoloFuoriDallaGriglia_Rifiuta()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Scene(obstacles: [new GridPosition(5, 0)]));

        Assert.Equal("obstacles", exception.ParamName);
    }

    [Fact]
    public void Create_ConOstacoloSullaPartenza_Rifiuta()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Scene(obstacles: [new GridPosition(0, 1)]));

        Assert.Equal("obstacles", exception.ParamName);
    }

    [Fact]
    public void Create_ConOstacoloSulTraguardo_Rifiuta()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Scene(obstacles: [new GridPosition(3, 1)]));

        Assert.Equal("obstacles", exception.ParamName);
    }

    [Fact]
    public void Create_ConOstacoloRipetuto_Rifiuta()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Scene(obstacles: [new GridPosition(1, 1), new GridPosition(1, 1)]));

        Assert.Equal("obstacles", exception.ParamName);
    }

    private static ExerciseScene Scene(IReadOnlyList<GridPosition>? obstacles = null) =>
        ExerciseScene.Create(
            5, 4,
            new GridPosition(0, 1),
            GridDirection.East,
            obstacles ?? [new GridPosition(2, 2)],
            new GridPosition(3, 1));
}
