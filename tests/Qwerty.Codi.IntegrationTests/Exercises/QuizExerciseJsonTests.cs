using System.Text.Json;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Exercises;
using Qwerty.Codi.Domain.Exercises;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Exercises;

/// <summary>
/// Il tipo dichiarativo (§13.9) attraversa gli stessi contratti di quello di percorso — definizione,
/// progetto del tentativo, soluzione privata — con forme proprie. Questi casi difendono la
/// differenza: un verbale di risposta non è un workspace di blocchi, e il server non deve leggere
/// l'uno come se fosse l'altro.
/// </summary>
[Trait("Category", "Unit")]
public sealed class QuizExerciseJsonTests
{
    [Fact]
    public void Parse_ConDefinizioneDichiarativa_ConservaRappresentazioneDomandaEModo()
    {
        var definition = ExerciseDefinitionJson.Parse(QuizDefinition(), "Test");

        var quiz = Assert.IsType<QuizData>(definition.Data);
        Assert.Equal("0110", quiz.Stimulus);
        Assert.Equal("Quanto vale in decimale?", quiz.Prompt);
        Assert.Equal(QuizAnswerMatch.Integer, quiz.Match);
        Assert.Equal(QuizEngine.ExerciseType, definition.ExerciseType);
        Assert.Equal(QuizEngine.Version, definition.EngineVersion);
        Assert.Empty(definition.AllowedBlocks);
    }

    [Fact]
    public void ToJson_ConDefinizioneDichiarativa_ScriveLeSezioniDelTipoENonQuelleDellaGriglia()
    {
        var definition = ExerciseDefinitionJson.Parse(QuizDefinition(), "Test");

        var written = ExerciseDefinitionJson.ToJson(definition);

        // Le sezioni della griglia non compaiono affatto: non sono `null` scritte, sono assenti.
        Assert.DoesNotContain("\"scene\"", written, StringComparison.Ordinal);
        Assert.DoesNotContain("\"completion\"", written, StringComparison.Ordinal);
        Assert.DoesNotContain("null", written, StringComparison.Ordinal);
        Assert.Contains("\"quiz\":{\"stimulus\":\"0110\"", written, StringComparison.Ordinal);
        Assert.Contains("\"match\":\"Integer\"", written, StringComparison.Ordinal);
    }

    [Fact]
    public void ToJson_ConDefinizioneDiPercorso_NonScriveLaSezioneDichiarativa()
    {
        // La scrittura canonica di un esercizio di percorso resta quella pubblicata: una sezione in
        // più cambierebbe l'impronta di contenuto di una versione già distribuita (§13.8).
        var definition = ExerciseDefinitionJson.Parse(GridPathDefinition(), "Test");

        var written = ExerciseDefinitionJson.ToJson(definition);

        Assert.DoesNotContain("\"quiz\"", written, StringComparison.Ordinal);
        Assert.Contains("\"scene\":", written, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ConModoDiConfrontoNonPrevisto_Rifiuta()
    {
        var definition = QuizDefinition().Replace("\"Integer\"", "\"Magia\"", StringComparison.Ordinal);

        var failure = Assert.Throws<CodiRequestException>(() => ExerciseDefinitionJson.Parse(definition, "Test"));

        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Contains("modo di confronto", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ConRappresentazioneMancante_Rifiuta()
    {
        var definition = QuizDefinition().Replace("\"0110\"", "\"   \"", StringComparison.Ordinal);

        var failure = Assert.Throws<CodiRequestException>(() => ExerciseDefinitionJson.Parse(definition, "Test"));

        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Contains("rappresentazione", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verifica_ConVerbaleDiRisposta_AccettaLaRispostaVuotaEIlTesto()
    {
        var definition = ExerciseDefinitionJson.Parse(QuizDefinition(), "Test");

        ExerciseWorkspaceValidator.Validate(
            QuizAnswerJson.Empty, definition, "Test", "la risposta");
        ExerciseWorkspaceValidator.Validate(
            "{\"answer\":\"6\"}", definition, "Test", "la risposta");
    }

    [Fact]
    public void Verifica_ConProprietaNonPrevista_Rifiuta()
    {
        var definition = ExerciseDefinitionJson.Parse(QuizDefinition(), "Test");

        var failure = Assert.Throws<CodiRequestException>(
            () => ExerciseWorkspaceValidator.Validate(
                "{\"answer\":\"6\",\"punteggio\":3}", definition, "Test", "la risposta"));

        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Contains("punteggio", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verifica_ConUnaRispostaNonTestuale_Rifiuta()
    {
        var definition = ExerciseDefinitionJson.Parse(QuizDefinition(), "Test");

        var failure = Assert.Throws<CodiRequestException>(
            () => ExerciseWorkspaceValidator.Validate(
                "{\"answer\":6}", definition, "Test", "la risposta"));

        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
    }

    [Fact]
    public void Verifica_ConUnProgettoADiacchiSuUnEsercizioDichiarativo_UsaLeRegoleDelTipo()
    {
        // Il verbo di un blocco non è una proprietà del verbale: un lavoro della forma sbagliata
        // viene rifiutato con il messaggio del tipo dichiarativo, non interpretato come Blockly.
        var definition = ExerciseDefinitionJson.Parse(QuizDefinition(), "Test");

        var failure = Assert.Throws<CodiRequestException>(
            () => ExerciseWorkspaceValidator.Validate(
                """{"blocks":{"blocks":[]}}""", definition, "Test", "la risposta"));

        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Contains("blocks", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Soluzione_LettaSoloDallaSoluzionePrivata_NonDallaDefinizione()
    {
        // La definizione non contiene la risposta attesa: se la contenesse, il documento che il
        // browser riceve per mostrare l'esercizio conterrebbe la soluzione.
        var definitionJson = QuizDefinition();
        Assert.DoesNotContain("answer", definitionJson, StringComparison.Ordinal);

        Assert.Equal("6", QuizAnswerJson.ReadExpected("{\"answer\":\"6\"}"));
        Assert.Null(QuizAnswerJson.ReadExpected("{\"answer\":\"\"}"));
        Assert.Null(QuizAnswerJson.ReadExpected(null));
    }

    [Theory]
    [InlineData("6", "6", true)]
    [InlineData(" 6 ", "6", true)]
    [InlineData("007", "7", true)]
    [InlineData("6", "7", false)]
    [InlineData("sei", "6", false)]
    [InlineData("", "6", false)]
    public void Confronto_Numerico_AccettaLaStessaQuantita(string given, string expected, bool correct)
    {
        var quiz = Quiz((QuizAnswerMatch.Integer));

        Assert.Equal(correct, quiz.Accepts(given, expected));
    }

    [Theory]
    [InlineData("rosso", "Rosso", true)]
    [InlineData("  ROSSO ", "rosso", true)]
    [InlineData("blu", "rosso", false)]
    public void Confronto_Testuale_IgnoraSpaziAiBordiEMaiuscole(string given, string expected, bool correct)
    {
        var quiz = Quiz(QuizAnswerMatch.Text);

        Assert.Equal(correct, quiz.Accepts(given, expected));
    }

    private static QuizData Quiz(QuizAnswerMatch match) =>
        QuizData.Create("0110", "Quanto vale?", match, Feedback());

    private static ExerciseFeedback Feedback() => ExerciseFeedback.Create("Giusto!", "Riprova.");

    private static string QuizDefinition() =>
        """
        {
          "schemaVersion": 1,
          "exerciseType": "Quiz",
          "engineVersion": "quiz-1",
          "title": "Quanto vale 0110",
          "instructions": "Leggi il numero binario e scrivi quanto vale in decimale.",
          "toolbox": { "allowedBlocks": [] },
          "limits": { "maxBlocks": 1, "maxNestingDepth": 1, "maxExecutionSteps": 1, "maxActiveDurationMs": 1000 },
          "quiz": {
            "stimulus": "0110",
            "prompt": "Quanto vale in decimale?",
            "match": "Integer"
          },
          "feedback": { "success": "Giusto!", "collision": "Riprova." }
        }
        """;

    private static string GridPathDefinition() =>
        """
        {
          "schemaVersion": 1,
          "exerciseType": "GridPath",
          "engineVersion": "gridpath-1",
          "title": "Raggiungi la stella",
          "instructions": "Porta il personaggio sulla stella usando i blocchi.",
          "scene": {
            "width": 5,
            "height": 4,
            "start": { "x": 0, "y": 1, "direction": "East" },
            "obstacles": [],
            "goal": { "x": 3, "y": 1 }
          },
          "toolbox": { "allowedBlocks": ["aula_start", "aula_move"] },
          "limits": { "maxBlocks": 30, "maxNestingDepth": 8, "maxExecutionSteps": 1000, "maxActiveDurationMs": 120000 },
          "completion": { "type": "ReachCell", "x": 3, "y": 1 },
          "feedback": { "success": "Bravo!", "collision": "Ostacolo." }
        }
        """;
}
