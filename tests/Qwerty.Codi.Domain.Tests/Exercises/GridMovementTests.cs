using Qwerty.Codi.Domain.Exercises;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Exercises;

/// <summary>
/// Effetti di passo e rotazione della versione <c>gridpath-1</c> (§13.2, §13.4). Sono le regole che
/// i due interpreti — quello del browser e quello lato server di §13.8 — devono applicare allo
/// stesso modo: qui si fissa che cosa significano «avanti» e «gira», non come si esegue.
/// </summary>
[Trait("Category", "Unit")]
public sealed class GridMovementTests
{
    [Theory]
    [InlineData(GridDirection.North, GridDirection.East)]
    [InlineData(GridDirection.East, GridDirection.South)]
    [InlineData(GridDirection.South, GridDirection.West)]
    [InlineData(GridDirection.West, GridDirection.North)]
    public void Turn_VersoDestra_GiraDiNovantaGradiInSensoOrario(GridDirection from, GridDirection expected)
    {
        Assert.Equal(expected, GridMovement.Turn(from, GridTurn.Right));
    }

    [Theory]
    [InlineData(GridDirection.North, GridDirection.West)]
    [InlineData(GridDirection.West, GridDirection.South)]
    [InlineData(GridDirection.South, GridDirection.East)]
    [InlineData(GridDirection.East, GridDirection.North)]
    public void Turn_VersoSinistra_GiraDiNovantaGradiInSensoAntiorario(GridDirection from, GridDirection expected)
    {
        Assert.Equal(expected, GridMovement.Turn(from, GridTurn.Left));
    }

    [Theory]
    [InlineData(GridDirection.North)]
    [InlineData(GridDirection.East)]
    [InlineData(GridDirection.South)]
    [InlineData(GridDirection.West)]
    public void Turn_QuattroVolteNelloStessoVerso_RiportaAllOrientamentoIniziale(GridDirection from)
    {
        var afterFourRights = GridMovement.Turn(
            GridMovement.Turn(GridMovement.Turn(GridMovement.Turn(from, GridTurn.Right), GridTurn.Right), GridTurn.Right),
            GridTurn.Right);
        var afterFourLefts = GridMovement.Turn(
            GridMovement.Turn(GridMovement.Turn(GridMovement.Turn(from, GridTurn.Left), GridTurn.Left), GridTurn.Left),
            GridTurn.Left);

        Assert.Equal(from, afterFourRights);
        Assert.Equal(from, afterFourLefts);
    }

    [Fact]
    public void Turn_ConLaRotazioneDichiarata_DiNovantaGradi()
    {
        Assert.Equal(90, GridMovement.TurnDegrees);
    }

    [Theory]
    [InlineData(GridDirection.North, 2, 2, 2, 1)]
    [InlineData(GridDirection.East, 2, 2, 3, 2)]
    [InlineData(GridDirection.South, 2, 2, 2, 3)]
    [InlineData(GridDirection.West, 2, 2, 1, 2)]
    public void Step_NellaDirezioneCorrente_SpostaDiUnaCella(
        GridDirection direction,
        int startX,
        int startY,
        int expectedX,
        int expectedY)
    {
        var destination = GridMovement.Step(new GridPosition(startX, startY), direction);

        Assert.Equal(new GridPosition(expectedX, expectedY), destination);
    }

    [Fact]
    public void Step_FuoriDallaGriglia_RestituisceLaCellaTentata()
    {
        // Il motore deve poter distinguere «fuori griglia» da «ostacolo» nel messaggio mostrato al
        // bambino (§13.2), quindi il passo restituisce la cella tentata anche quando non esiste.
        var destination = GridMovement.Step(new GridPosition(0, 0), GridDirection.North);

        Assert.Equal(new GridPosition(0, -1), destination);
        Assert.False(destination.IsInside(5, 4));
    }

    [Fact]
    public void Turn_ConOrientamentoNonDefinito_Rifiuta()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => GridMovement.Turn((GridDirection)9, GridTurn.Right));

        Assert.Equal("direction", exception.ParamName);
    }

    [Fact]
    public void Step_ConOrientamentoNonDefinito_Rifiuta()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => GridMovement.Step(new GridPosition(0, 0), (GridDirection)9));

        Assert.Equal("direction", exception.ParamName);
    }
}
