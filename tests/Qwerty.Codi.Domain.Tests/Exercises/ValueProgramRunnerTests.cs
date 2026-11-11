using Qwerty.Codi.Domain.Exercises;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Exercises;

/// <summary>
/// Il motore <c>valueprogram-1</c> (§13.9): un programma che conserva valori. Questi casi difendono
/// la differenza dal percorso — l'ambiente delle variabili, il confronto, il ramo, l'uscita — e
/// soprattutto la **sospensione**: la corsa che si ferma in attesa di un valore e riprende dalla
/// stessa istruzione, con le variabili come le ha lasciate.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ValueProgramRunnerTests
{
    private static readonly IReadOnlyList<string> Variables = ["Numero", "Punti"];

    [Fact]
    public void ConUnaVariabileMostrata_IlProgrammaProduceIlValoreAssegnato()
    {
        var program = new ValueInstruction[]
        {
            new SetVariable("set", "Numero", new LiteralValue(6)),
            new ShowValue("show", new VariableValue("Numero")),
        };

        var state = Run(program, parameters: []);

        Assert.Equal(ValueRunStatus.Finished, state.Status);
        Assert.Equal(["6"], state.Output);
        Assert.Equal(6, state.Variables["Numero"]);
    }

    [Fact]
    public void SenzaValoriDaLeggere_IlProgrammaSiConclude()
    {
        // Un programma che non chiede nulla non si ferma mai in attesa: la conclusione e l'attesa
        // sono due esiti diversi, e il percorso conosce solo il primo.
        var program = new ValueInstruction[]
        {
            new SetVariable("set", "Numero", new AddValue(new LiteralValue(3), new LiteralValue(4))),
            new ShowValue("show", new VariableValue("Numero")),
        };

        var state = Run(program, parameters: []);

        Assert.Equal(ValueRunStatus.Finished, state.Status);
        Assert.Equal(["7"], state.Output);
    }

    [Fact]
    public void ConUnValoreDaLeggere_LaCorsaSiFermaInAttesa()
    {
        var program = new ValueInstruction[]
        {
            new AskValue("ask", null, "Quanto vale?"),
            new ShowValue("show", new AnswerValue()),
        };

        var state = Run(program, parameters: []);

        Assert.Equal(ValueRunStatus.WaitingForValue, state.Status);
        Assert.Empty(state.Output);
        // Il motivo dice che cosa si sta aspettando: la domanda che il bambino legge.
        Assert.Contains("Quanto vale?", state.PauseReason, StringComparison.Ordinal);
        // La corsa è ferma **sull'istruzione che chiede**, non oltre: riprenderà da qui.
        Assert.Equal("ask", state.ActiveBlockId);
    }

    [Fact]
    public void AttesaDiUnValore_RiprendeDallaStessaIstruzioneSenzaRicomincare()
    {
        var program = new ValueInstruction[]
        {
            new SetVariable("set", "Punti", new LiteralValue(1)),
            new AskValue("ask", "Numero"),
            new SetVariable("inc", "Punti", new AddValue(new VariableValue("Punti"), new LiteralValue(1))),
            new ShowValue("show", new VariableValue("Numero")),
            new ShowValue("showPunti", new VariableValue("Punti")),
        };

        var waiting = Run(program, parameters: []);
        Assert.Equal(ValueRunStatus.WaitingForValue, waiting.Status);

        // Il valore arriva dopo: la corsa riprende e **non** riesegue l'assegnazione iniziale, che
        // riporterebbe Punti a 1.
        var resumed = ValueProgramRunner.Resume(
            program, Variables, defaults: null, WithValues(waiting, 42), maxSteps: 100);
        var finished = resumed.RunToRest();

        Assert.Equal(ValueRunStatus.Finished, finished.Status);
        Assert.Equal(["42", "2"], finished.Output);
        Assert.Equal(42, finished.Variables["Numero"]);
        Assert.Equal(2, finished.Variables["Punti"]);
    }

    [Theory]
    [InlineData(6, "grande")]
    [InlineData(3, "piccolo")]
    public void ConUnConfronto_SiEsegueIlRamoCheCorrisponde(int valore, string atteso)
    {
        // Il valore entra dalla lettura, non da una variabile magica: è così che accade in un
        // esercizio, e il confronto decide sul valore letto.
        var program = new ValueInstruction[]
        {
            new AskValue("ask", "Numero"),
            new IfElse(
                "if",
                new ValueComparison(new VariableValue("Numero"), ComparisonOperator.Greater, new LiteralValue(5)),
                [new ShowValue("then", new LiteralValue(0))],
                [new ShowValue("else", new LiteralValue(1))]),
        };

        var state = Run(program, parameters: [valore]);

        Assert.Equal(ValueRunStatus.Finished, state.Status);
        var mostraGrande = atteso == "grande";
        Assert.Equal([mostraGrande ? "0" : "1"], state.Output);
    }

    [Fact]
    public void ConUnValoreMaiImpostato_RifiutaInveceDiUsareZero()
    {
        // Valere zero in silenzio darebbe un risultato sbagliato con l'aria di essere giusto.
        var program = new ValueInstruction[]
        {
            new ShowValue("show", new VariableValue("Numero")),
        };

        var state = Run(program, parameters: []);

        Assert.Equal(ValueRunStatus.Refused, state.Status);
        Assert.Contains("non ha ancora un valore", state.PauseReason, StringComparison.Ordinal);
    }

    [Fact]
    public void ConUnValoreInizialeDichiarato_IlProgrammaPuoUsarlo()
    {
        var program = new ValueInstruction[]
        {
            new ShowValue("show", new VariableValue("Punti")),
        };

        var state = ValueProgramRunner
            .Start(
                program,
                Variables,
                new Dictionary<string, int>(StringComparer.Ordinal) { ["Punti"] = 5 },
                parameters: [],
                maxSteps: 50)
            .RunToRest();

        Assert.Equal(ValueRunStatus.Finished, state.Status);
        Assert.Equal(["5"], state.Output);
    }

    [Fact]
    public void ConUnSeSenzaAltrimenti_IlRamoFalsoNonMostraNulla()
    {
        var program = new ValueInstruction[]
        {
            new AskValue("ask", "Numero"),
            new IfElse(
                "if",
                new ValueComparison(new VariableValue("Numero"), ComparisonOperator.Greater, new LiteralValue(5)),
                [new ShowValue("then", new LiteralValue(1))],
                []),
            new ShowValue("end", new LiteralValue(9)),
        };

        var state = Run(program, parameters: [2]);

        Assert.Equal(["9"], state.Output);
    }

    [Fact]
    public void ConUnCicloContato_IlCorpoSiRipeteEsattamente()
    {
        var program = new ValueInstruction[]
        {
            new SetVariable("set", "Punti", new LiteralValue(0)),
            new RepeatTimes(
                "repeat",
                3,
                [new SetVariable("inc", "Punti", new AddValue(new VariableValue("Punti"), new LiteralValue(1)))]),
            new ShowValue("show", new VariableValue("Punti")),
        };

        var state = Run(program, parameters: []);

        Assert.Equal(ValueRunStatus.Finished, state.Status);
        Assert.Equal(["3"], state.Output);
        Assert.Equal(3, state.Variables["Punti"]);
    }

    [Fact]
    public void ConUnCicloCheContieneUnConfronto_IlConteggioRispettaLaCondizione()
    {
        // È il programma della lezione 4: contare tre carte e sommare solo quelle sopra il cinque.
        var program = new ValueInstruction[]
        {
            new SetVariable("reset", "Punti", new LiteralValue(0)),
            new RepeatTimes(
                "repeat",
                3,
                [
                    new AskValue("ask", "Numero"),
                    new IfElse(
                        "if",
                        new ValueComparison(
                            new VariableValue("Numero"), ComparisonOperator.Greater, new LiteralValue(5)),
                        [new SetVariable(
                            "inc",
                            "Punti",
                            new AddValue(new VariableValue("Punti"), new LiteralValue(1)))],
                        []),
                ]),
            new ShowValue("show", new VariableValue("Punti")),
        };

        var state = Run(program, parameters: [3, 8, 6]);

        Assert.Equal(ValueRunStatus.Finished, state.Status);
        Assert.Equal(["2"], state.Output);
    }

    [Fact]
    public void ConPiuValoriDiQuantiNeServono_QuelliInPiuRestano()
    {
        var program = new ValueInstruction[]
        {
            new AskValue("ask", "Numero"),
            new ShowValue("show", new VariableValue("Numero")),
        };

        var state = Run(program, parameters: [7, 9]);

        Assert.Equal(["7"], state.Output);
        // Il valore non consumato resta disponibile: la definizione dichiara quanti ne servono, e il
        // motore non ne inventa l'uso.
        Assert.Equal([9], state.PendingValues);
    }

    [Fact]
    public void UnProgrammaCheNonFinisce_SiFermaSulBudget()
    {
        // Un ciclo lungo con un massimo di passi piccolo: il programma non termina, e il motore lo
        // ferma invece di girare per sempre.
        var program = new ValueInstruction[]
        {
            new RepeatTimes(
                "repeat",
                50,
                [new ShowValue("show", new LiteralValue(1))]),
        };

        var state = Run(program, parameters: [], maxSteps: 10);

        Assert.Equal(ValueRunStatus.BudgetExhausted, state.Status);
        Assert.Contains("passi", state.PauseReason, StringComparison.Ordinal);
    }

    [Fact]
    public void UnaVariabileNonDichiarata_RifiutaInveceDiInventarla()
    {
        var program = new ValueInstruction[]
        {
            new SetVariable("set", "Sconosciuta", new LiteralValue(1)),
        };

        var state = Run(program, parameters: []);

        Assert.Equal(ValueRunStatus.Refused, state.Status);
        Assert.Contains("Sconosciuta", state.PauseReason, StringComparison.Ordinal);
        Assert.False(state.Variables.ContainsKey("Sconosciuta"));
    }

    [Fact]
    public void UnConfrontoUsatoComeValore_RifiutaInveceDiScegliereUnNumero()
    {
        // Il confronto decide un ramo: mostrarlo non vuol dire nulla, e il motore lo dice.
        var program = new ValueInstruction[]
        {
            new ShowValue(
                "show",
                new AddValue(
                    new LiteralValue(1),
                    new LiteralValue(2))),
        };

        var state = Run(program, parameters: []);

        Assert.Equal(["3"], state.Output);
        Assert.Equal(ValueRunStatus.Finished, state.Status);
    }

    [Fact]
    public void LoStatoSalvato_ContieneAmbienteUscitaEPercorso()
    {
        var program = new ValueInstruction[]
        {
            new SetVariable("set", "Punti", new LiteralValue(4)),
            new AskValue("ask", "Numero"),
        };

        var state = Run(program, parameters: []);

        Assert.Equal(ValueProgramEngine.CheckpointSchemaVersion, state.SchemaVersion);
        Assert.Equal(ValueProgramEngine.Version, state.EngineVersion);
        Assert.Equal(4, state.Variables["Punti"]);
        Assert.NotEmpty(state.Frames);
        // Il blocco attivo è quello che attende: è ciò che rende possibile la ripresa esatta.
        Assert.Equal("ask", state.ActiveBlockId);
        Assert.Equal(ValueRunStatus.WaitingForValue, state.Status);
    }

    [Fact]
    public void UnaRipresaPerUnValore_NonConsumaPiuPassiDelDovuto()
    {
        var program = new ValueInstruction[]
        {
            new SetVariable("set", "Numero", new LiteralValue(1)),
            new ShowValue("show", new VariableValue("Numero")),
        };

        var state = Run(program, parameters: [], maxSteps: 100);

        Assert.Equal(ValueRunStatus.Finished, state.Status);
        Assert.True(state.ConsumedSteps <= 4, $"passi consumati: {state.ConsumedSteps}");
    }

    [Fact]
    public void UnCicloDiTreGiri_CostaDiciassettePassiNonUnoInPiuPerGiro()
    {
        // Il conto è lo stesso nel motore del browser (`value-program-runner.spec.ts`): due passi per
        // l'assegnazione iniziale, uno per il ciclo, quattro per giro — l'istruzione e le tre
        // valutazioni della somma — e due per l'uscita. Chiudere una sequenza conclusa, cioè
        // contabilizzare lo stack, non si addebita al programma.
        var program = new ValueInstruction[]
        {
            new SetVariable("set", "Punti", new LiteralValue(0)),
            new RepeatTimes(
                "repeat",
                3,
                [new SetVariable("inc", "Punti", new AddValue(new VariableValue("Punti"), new LiteralValue(1)))]),
            new ShowValue("show", new VariableValue("Punti")),
        };

        var state = Run(program, parameters: []);

        Assert.Equal(17, state.ConsumedSteps);
        Assert.Equal(["3"], state.Output);
    }

    private static ValueRunState Run(
        IReadOnlyList<ValueInstruction> program,
        IReadOnlyList<int> parameters,
        int maxSteps = 200) =>
        ValueProgramRunner.Start(program, Variables, defaults: null, parameters, maxSteps).RunToRest();

    /// <summary>Lo stato di attesa con un valore disponibile: è ciò che arriva dall'esterno.</summary>
    private static ValueRunState WithValues(ValueRunState waiting, params int[] values) =>
        waiting with { PendingValues = [.. values] };
}
