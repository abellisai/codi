using Qwerty.Codi.Domain.Exercises;
using Xunit;

namespace Qwerty.Codi.Domain.Tests.Exercises;

/// <summary>
/// La descrizione di <c>valueprogram-1</c> (§13.9): il catalogo con comandi ed espressioni distinte,
/// i dati del tipo con variabili e casi di prova, e le regole che fermano un esercizio impossibile
/// prima che lo incontri un bambino.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ValueProgramExerciseDataTests
{
    private static readonly ValueProgramExerciseData Descriptor = new();

    [Fact]
    public void IlCatalogo_HaComandiEspressioniEUnPuntoDiIngresso()
    {
        var comandi = Descriptor.Blocks.Where(block => block.Kind == BlockKind.Command).ToList();
        var espressioni = Descriptor.Blocks.Where(block => block.Kind == BlockKind.Expression).ToList();

        Assert.Equal(ValueBlockCatalog.Commands.Count, comandi.Count);
        Assert.Equal(ValueBlockCatalog.Expressions.Count, espressioni.Count);
        Assert.Contains(Descriptor.Blocks, block => block.Type == Descriptor.EntryPointBlock);
    }

    [Fact]
    public void IlPuntoDiIngresso_EUnComandoEUnEspressioneNo()
    {
        // Un blocco di espressione non apre una catena: se lo facesse, il programma non avrebbe nulla
        // da eseguire e il motore lo rifiuterebbe solo dopo la pubblicazione.
        Assert.Equal(BlockKind.Command, Descriptor.Block(ValueBlockCatalog.Start)!.Kind);
        Assert.Equal(BlockKind.Expression, Descriptor.Block(ValueBlockCatalog.Add)!.Kind);
        Assert.Equal(BlockKind.Expression, Descriptor.Block(ValueBlockCatalog.Compare)!.Kind);
    }

    [Fact]
    public void IBlocchiCheNominanoUnaVariabile_DichiaranoIlCampoDelNome()
    {
        // «chiedi … e attendi» non nomina una variabile: porta la domanda, e la risposta si memorizza
        // con «porta Numero a (risposta)». È il vocabolario del corso, dove chiedere e conservare sono
        // due blocchi e non uno.
        foreach (var tipo in new[]
        {
            ValueBlockCatalog.Set,
            ValueBlockCatalog.Change,
            ValueBlockCatalog.Variable,
        })
        {
            var blocco = Descriptor.Block(tipo);
            Assert.NotNull(blocco);
            Assert.Contains(blocco!.Fields, field => field.Name == ValueBlockCatalog.VariableField);
        }
    }

    [Fact]
    public void IlBloccoChiedi_PortaLaDomandaEIlReporterRisposta()
    {
        // Il blocco del corso: «chiedi [Numero della carta?] e attendi». La domanda è testo libero che
        // il bambino legge mentre il programma aspetta, e non un elenco di frasi ammesse.
        var chiedi = Descriptor.Block(ValueBlockCatalog.Ask);

        Assert.NotNull(chiedi);
        Assert.Empty(chiedi!.Fields);
        Assert.Contains(ValueBlockCatalog.QuestionField, chiedi.TextFields, StringComparer.Ordinal);

        // E il reporter con cui la risposta si usa, che non ha campi perché la risposta non si sceglie.
        var risposta = Descriptor.Block(ValueBlockCatalog.Answer);

        Assert.NotNull(risposta);
        Assert.Empty(risposta!.Fields);
        Assert.Equal(BlockKind.Expression, risposta.Kind);
    }

    [Fact]
    public void IlBloccoCambia_EUnComandoConUnaVariabileEUnValore()
    {
        var cambia = Descriptor.Block(ValueBlockCatalog.Change);

        Assert.NotNull(cambia);
        Assert.Equal(BlockKind.Command, cambia!.Kind);
        Assert.Contains(cambia.Fields, field => field.Name == ValueBlockCatalog.VariableField);
        Assert.Contains(ValueBlockCatalog.ChangeField, cambia.ValueInputs, StringComparer.Ordinal);
    }

    [Fact]
    public void IlConfronto_DichiaraSoloLeTreRelazioni()
    {
        var blocco = Descriptor.Block(ValueBlockCatalog.Compare);

        Assert.NotNull(blocco);
        var campo = Assert.Single(blocco!.Fields);
        Assert.Equal(ValueBlockCatalog.CompareField, campo.Name);
        // Le relazioni si verificano dal messaggio: un valore fuori elenco viene rifiutato.
        Assert.Throws<ArgumentException>(() =>
            campo.Validate(ValueBlockCatalog.Compare, BlockFieldValue.ChoiceValue("Diverso")));
        field_validation_passes_for_each_choice(campo);
    }

    [Fact]
    public void IDati_SiCostruisconoDallePartiDelDocumento()
    {
        var parti = new ValueProgramParts(
            [new ValueVariable("Numero", 0), new ValueVariable("Punti", 0)],
            [new ValueTestCase([6], ["1"]), new ValueTestCase([3], ["0"])],
            ExerciseFeedback.Create("Bravo!", "Riprova."));

        var dati = Assert.IsType<ValueProgramData>(Descriptor.CreateData(parti));

        Assert.Equal(["Numero", "Punti"], dati.VariableNames);
        Assert.Equal(2, dati.TestCases.Count);
        Assert.True(dati.KnowsVariable("Punti"));
        Assert.False(dati.KnowsVariable("Sconosciuta"));
    }

    [Fact]
    public void PartiDiUnAltroTipo_RifiutanoConUnMessaggioCheNominaLaFormaAttesa()
    {
        // Le parti di un esercizio dichiarativo non sono le sue: il motore lo dice invece di
        // costruire dati vuoti che si scoprirebbero solo alla prima esecuzione.
        var eccezione = Assert.Throws<ArgumentException>(() =>
            Descriptor.CreateData(new QuizDefinitionParts(
                "0110",
                "Quanto vale?",
                QuizAnswerMatch.Integer,
                ExerciseFeedback.Create("ok", "no"))));

        Assert.Contains("variabili, casi di prova", eccezione.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnEsercizioSenzaVariabili_Rifiuta()
    {
        Assert.Throws<ArgumentException>(() =>
            ValueProgramData.Create([], [new ValueTestCase([], ["1"])], ExerciseFeedback.Create("ok", "no")));
    }

    [Fact]
    public void UnEsercizioSenzaCasiDiProva_Rifiuta()
    {
        // Senza un caso di prova non si sa che cosa il programma debba mostrare: l'esercizio non
        // avrebbe un traguardo, e un traguardo mancante non si scopre correggendo.
        Assert.Throws<ArgumentException>(() =>
            ValueProgramData.Create([new ValueVariable("Punti", 0)], [], ExerciseFeedback.Create("ok", "no")));
    }

    [Fact]
    public void UnaVariabileDichiarataDueVolte_Rifiuta()
    {
        Assert.Throws<ArgumentException>(() =>
            ValueProgramData.Create([new ValueVariable("Punti", 0), new ValueVariable("Punti", 1)], [new ValueTestCase([], ["1"])], ExerciseFeedback.Create("ok", "no")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("con spazio")]
    [InlineData("1numero")]
    [InlineData("questo_nome_e_troppo_lungo_per_una_variabile")]
    public void UnNomeDiVariabileNonUtilizzabile_Rifiuta(string nome)
    {
        Assert.Throws<ArgumentException>(() =>
            ValueProgramData.Create([new ValueVariable(nome, 0)], [new ValueTestCase([], ["1"])], ExerciseFeedback.Create("ok", "no")));
    }

    [Fact]
    public void UnCasoDiProvaSenzaUscitaAttesa_Rifiuta()
    {
        Assert.Throws<ArgumentException>(() =>
            ValueProgramData.Create([new ValueVariable("Punti", 0)], [new ValueTestCase([1], [])], ExerciseFeedback.Create("ok", "no")));
    }

    [Fact]
    public void IlCheckpointDichiarato_EQuelloDelProprioMotore()
    {
        Assert.Equal(ValueProgramEngine.CheckpointSchemaVersion, Descriptor.CheckpointSchemaVersion);
        Assert.Equal(1, Descriptor.CheckpointSchemaVersion);
        // Non è lo stesso schema del percorso per il solo fatto di chiamarsi 1: sono due formati
        // diversi, e il registro li tiene separati per versione del motore.
        Assert.Equal("valueprogram-1", Descriptor.Version);
        Assert.Equal("ValueProgram", Descriptor.ExerciseType);
    }

    [Fact]
    public void IlRegistro_ConosceLaNuovaVersione()
    {
        var versione = ExerciseEngineRegistry.Find(ValueProgramEngine.Version);

        Assert.NotNull(versione);
        Assert.Equal(ValueProgramEngine.ExerciseType, versione!.ExerciseType);
        Assert.Contains(ValueProgramEngine.Version, ExerciseEngineRegistry.KnownVersions);
        Assert.Contains(ValueProgramEngine.ExerciseType, ExerciseEngineRegistry.KnownExerciseTypes);
    }

    private static void field_validation_passes_for_each_choice(BlockFieldRule campo)
    {
        foreach (var relazione in ValueBlockCatalog.ComparisonValues)
        {
            campo.Validate(ValueBlockCatalog.Compare, BlockFieldValue.ChoiceValue(relazione));
        }
    }
}
