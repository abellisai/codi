using System.Text.Json.Nodes;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Exercises;
using Qwerty.Codi.Domain.Exercises;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Exercises;

/// <summary>
/// Il server legge ed **esegue** il lavoro di un alunno su un esercizio che conserva valori (§13.9).
///
/// È il pezzo che rende la correzione una correzione: il browser manda il progetto a blocchi, e il
/// server lo compila e lo esegue sui casi di prova invece di fidarsi dell'esito che il browser
/// dichiara. Questi casi difendono due cose che devono restare vere: le diagnosi nominano il blocco
/// sbagliato, e un programma che mostra il risultato giusto supera il caso anche se ci arriva per una
/// strada diversa — perché la correzione guarda **che cosa mostra**, non come.
///
/// I progetti si compongono con i tipi JSON e non con stringhe scritte a mano: la forma del documento
/// Blockly ha abbastanza graffe annidate che contarle a occhio è un modo sicuro di provare il
/// documento sbagliato. La forma resta quella che Blockly produce e che il server verifica al
/// salvataggio: gli ingressi sono proprietà il cui valore è il blocco, la catena prosegue in `next`.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ValueProgramEvaluationTests
{
    private static readonly IReadOnlyList<string> Variables = ["Numero", "Punti"];

    /// <summary>
    /// Il programma della lezione 4 del corso, blocco per blocco come lo scrive la proposta:
    ///
    /// <code>
    /// quando si clicca sulla bandiera verde
    /// porta Punti a 0
    /// porta Numero a 0
    /// ripeti 3 volte
    ///     chiedi [Numero della carta?] e attendi
    ///     porta Numero a (risposta)
    ///     se &lt;(Numero) &gt; 5&gt; allora
    ///         cambia Punti di 1
    /// dire (Punti)
    /// </code>
    ///
    /// Il caso serve a due cose insieme: che il motore esegua il conteggio, e che il **vocabolario**
    /// del corso — il reporter `risposta`, il blocco `cambia`, la domanda dentro `chiedi` — esista
    /// davvero e non sia una riscrittura con altri nomi.
    /// </summary>
    private static string PuntiDelleCarte() => Doc(
        Start(
            Set("resetPunti", "Punti", Number("zero", 0)),
            Set("resetNumero", "Numero", Number("zero2", 0)),
            Repeat(
                "repeat",
                3,
                Ask("ask", "Numero della carta?"),
                Set("memo", "Numero", Answer("risposta")),
                If(
                    "if",
                    Compare("cmp", Variable("v", "Numero"), "Greater", Number("cinque", 5)),
                    Change("inc", "Punti", Number("uno", 1)))),
            Show("show", Variable("vend", "Punti"))));

    /// <summary>«chiedi [Quanto vale?] e attendi», «porta Numero a (risposta)», «dire (Numero + 1)».</summary>
    private static string SommaUno() => Doc(
        Start(
            Ask("ask", "Quanto vale?"),
            Set("memo", "Numero", Answer("risposta")),
            Set("set", "Punti", Add(Variable("v", "Numero"), Number("n", 1))),
            Show("show", Variable("vend", "Punti"))));

    private static ValueProgramData Data(params ValueTestCase[] cases) =>
        ValueProgramData.Create(
            [new ValueVariable("Numero", 0), new ValueVariable("Punti", 0)],
            cases.Length == 0 ? [new ValueTestCase([6], ["7"])] : cases,
            ExerciseFeedback.Create("Bravo!", "Riprova."));

    [Fact]
    public void UnProgrammaCheLeggeESomma_SuperaTuttiICasi()
    {
        var program = ValueProgramJson.Compile(SommaUno(), Variables);
        var data = Data(new ValueTestCase([6], ["7"]), new ValueTestCase([3], ["4"]));

        var esito = ValueProgramJson.Evaluate(program, data, maxSteps: 1000);

        Assert.True(esito.Passed, esito.Problem);
        Assert.True(esito.ProgramValid);
        Assert.Equal(string.Empty, esito.Problem);
    }

    [Fact]
    public void IlProgrammaDelleCarte_SuperaIlCasoDellaLezioneQuattro()
    {
        // Tre carte: 3, 8, 6. Solo l'8 e il 6 superano il cinque, quindi i punti sono 2.
        var program = ValueProgramJson.Compile(PuntiDelleCarte(), Variables);

        var esito = ValueProgramJson.Evaluate(program, Data(new ValueTestCase([3, 8, 6], ["2"])), 1000);

        Assert.True(esito.Passed, esito.Problem);
    }

    [Fact]
    public void IlProgrammaDelleCarte_ConUnaSogliaDiversa_RaccontaCheCosaHaMostrato()
    {
        // Soglia 1 invece di 5: tutte e tre le carte superano il confronto, quindi i punti sono 3
        // invece di 2. È l'errore che un bambino fa davvero — sbagliare il numero del confronto — e la
        // diagnosi deve dirgli che cosa ha mostrato e che cosa doveva mostrare.
        var program = ValueProgramJson.Compile(
            PuntiDelleCarte().Replace("\"NUM\":5", "\"NUM\":1", StringComparison.Ordinal),
            Variables);

        var esito = ValueProgramJson.Evaluate(program, Data(new ValueTestCase([3, 8, 6], ["2"])), 1000);

        Assert.False(esito.Passed);
        // Il programma è valido e funziona: mostra solo un risultato diverso. È una cosa diversa da un
        // programma che non si esegue, e la diagnosi lo distingue.
        Assert.True(esito.ProgramValid);
        Assert.Contains("mostra «3»", esito.Problem, StringComparison.Ordinal);
        Assert.Contains("invece di «2»", esito.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void UnProgrammaCorrettoMaDiverso_SuperaIlCaso()
    {
        // «mostra 7 + 0»: non chiede nulla e non usa variabili. La correzione guarda che cosa il
        // programma **mostra**, quindi un'altra strada per lo stesso risultato supera il caso.
        var program = ValueProgramJson.Compile(
            Doc(Start(Show("show", Add(Number("c", 7), Number("z", 0))))),
            Variables);

        var esito = ValueProgramJson.Evaluate(program, Data(new ValueTestCase([6], ["7"])), 1000);

        Assert.True(esito.Passed, esito.Problem);
    }

    [Fact]
    public void IlPrimoCasoFallito_EQuelloRaccontato()
    {
        var program = ValueProgramJson.Compile(
            SommaUno().Replace("\"NUM\":1", "\"NUM\":5", StringComparison.Ordinal),
            Variables);
        var data = Data(new ValueTestCase([1], ["2"]), new ValueTestCase([6], ["7"]));

        var esito = ValueProgramJson.Evaluate(program, data, 1000);

        Assert.False(esito.Passed);
        Assert.Contains("Con i valori 1", esito.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void UnCasoCheNonDaAbbastanzaValori_RifiutaInveceDiAccusareLAlunno()
    {
        // Il programma chiede un valore, il caso non ne dichiara nessuno: la corsa resta in attesa. La
        // colpa è dell'esercizio, e la diagnosi non deve farla sembrare un errore dell'alunno.
        var program = ValueProgramJson.Compile(SommaUno(), Variables);

        var esito = ValueProgramJson.Evaluate(program, Data(new ValueTestCase([], ["7"])), 1000);

        Assert.False(esito.Passed);
        Assert.False(esito.ProgramValid);
        Assert.Contains("chiede un valore", esito.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void UnProgrammaCheNominaUnaVariabileNonDichiarata_RifiutaNominandola()
    {
        var sbagliato = SommaUno().Replace("\"VAR\":\"Punti\"", "\"VAR\":\"Sconosciuta\"", StringComparison.Ordinal);

        var eccezione = Assert.Throws<CodiRequestException>(
            () => ValueProgramJson.Compile(sbagliato, Variables));

        Assert.Contains("Sconosciuta", eccezione.Message, StringComparison.Ordinal);
        Assert.Contains("Numero, Punti", eccezione.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnProgettoVuoto_RifiutaDicendoCheMancaIlPuntoDiIngresso()
    {
        var eccezione = Assert.Throws<CodiRequestException>(
            () => ValueProgramJson.Compile("""{"blocks":{"languageVersion":0,"blocks":[]}}""", Variables));

        Assert.Contains("quando si avvia", eccezione.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnProgettoCheNonSiLegge_Rifiuta()
    {
        Assert.Throws<CodiRequestException>(() => ValueProgramJson.Compile("non è json", Variables));
    }

    [Fact]
    public void UnConfrontoUsatoComeValore_RifiutaPrimaDiEseguire()
    {
        var progetto = Doc(Start(Show("show", Compare("cmp", Number("a", 1), "Less", Number("b", 2)))));

        var eccezione = Assert.Throws<CodiRequestException>(
            () => ValueProgramJson.Compile(progetto, Variables));

        Assert.Contains("non si può sommare né mostrare", eccezione.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnCicloSenzaConteggio_RifiutaPrimaDiEseguire()
    {
        // Il ciclo si compone senza il campo del conteggio: è il progetto che un alunno lascia quando
        // trascina il blocco e non scrive quante volte.
        var senzaConteggio = Block("val_repeat", "repeat");
        Input(senzaConteggio, "DO", Show("show", Number("n", 1)));
        var progetto = Doc(Start(senzaConteggio));

        var eccezione = Assert.Throws<CodiRequestException>(
            () => ValueProgramJson.Compile(progetto, Variables));

        Assert.Contains("quante volte ripetere", eccezione.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- composizione del documento

    /// <summary>
    /// Il documento completo. Il conteggio delle graffe è ciò che rende fragile una stringa scritta a
    /// mano: qui la struttura è un oggetto JSON e non c'è niente da contare.
    /// </summary>
    private static string Doc(params JsonObject[] chain) =>
        new JsonObject
        {
            ["blocks"] = new JsonObject
            {
                ["languageVersion"] = 0,
                ["blocks"] = new JsonArray([.. chain]),
            },
        }.ToJsonString();

    /// <summary>Il blocco dentro un ingresso o dentro una catena: è la forma del documento Blockly.</summary>
    private static JsonObject Connection(JsonObject block) => new() { ["block"] = block };

    private static JsonObject Block(string type, string id) => new() { ["type"] = type, ["id"] = id };

    /// <summary>Concatena i blocchi: il primo porta `next` al secondo, e così via.</summary>
    private static JsonObject Chain(params JsonObject[] blocks)
    {
        for (var index = blocks.Length - 2; index >= 0; index--)
        {
            blocks[index]["next"] = Connection(blocks[index + 1]);
        }

        return blocks[0];
    }

    private static JsonObject Start(params JsonObject[] body) =>
        body.Length == 0 ? Block("val_start", "s") : Chain([Block("val_start", "s"), .. body]);

    private static JsonObject Field(JsonObject block, string name, JsonNode value)
    {
        block["fields"] = new JsonObject { [name] = value };

        return block;
    }

    private static JsonObject Input(JsonObject block, string name, JsonObject inner)
    {
        var inputs = block["inputs"] as JsonObject ?? new JsonObject();
        inputs[name] = Connection(inner);
        block["inputs"] = inputs;

        return block;
    }

    private static JsonObject Number(string id, int value) =>
        Field(Block("val_number", id), "NUM", JsonValue.Create(value));

    private static JsonObject Variable(string id, string name) =>
        Field(Block("val_variable", id), "VAR", name);

    private static JsonObject Add(JsonObject left, JsonObject right) =>
        Input(Input(Block("val_add", "add"), "VALUE", left), "OTHER", right);

    private static JsonObject Compare(string id, JsonObject left, string op, JsonObject right) =>
        Input(Input(Field(Block("val_compare", id), "OP", op), "VALUE", left), "OTHER", right);

    private static JsonObject Set(string id, string variable, JsonObject value) =>
        Input(Field(Block("val_set", id), "VAR", variable), "VALUE", value);

    /** «cambia Punti di 1»: il blocco con cui il corso conta. */
    private static JsonObject Change(string id, string variable, JsonObject delta) =>
        Input(Field(Block("val_change", id), "VAR", variable), "DELTA", delta);

    /** «chiedi [domanda?] e attendi»: la domanda è il testo che il bambino legge. */
    private static JsonObject Ask(string id, string question) =>
        Field(Block("val_ask", id), "QUESTION", question);

    /** Il reporter «risposta»: non ha campi, perché la risposta non si sceglie. */
    private static JsonObject Answer(string id) => Block("val_answer", id);

    private static JsonObject Show(string id, JsonObject value) =>
        Input(Block("val_show", id), "VALUE", value);

    private static JsonObject Repeat(string id, int count, params JsonObject[] body) =>
        Input(
            Field(
                Block("val_repeat", id),
                "COUNT",
                JsonValue.Create(count)),
            "DO",
            Chain(body));

    private static JsonObject If(string id, JsonObject condition, params JsonObject[] then) =>
        Input(Input(Block("val_if", id), "VALUE", condition), "DO", Chain(then));
}
