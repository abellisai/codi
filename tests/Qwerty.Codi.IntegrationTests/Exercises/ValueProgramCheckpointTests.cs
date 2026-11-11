using System.Text.Json;
using Qwerty.Codi.Domain.Exercises;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Exercises;

/// <summary>
/// Lo stato di una corsa che conserva valori attraverso il **formato di trasporto** (§13.7). Il numero
/// di schema del checkpoint è 1, come quello del percorso, e per questo non basta a dire che il formato
/// è quello atteso: uno stato incoerente accettato farebbe riprendere la corsa **altrove**.
///
/// Questi casi provano le due cose che il server deve poter fare: ricevere uno stato e riprenderlo. La
/// verifica di coerenza vive nel server (`AttemptCommandHandlers`) e usa gli stessi nomi dei campi: il
/// caso che rilegge lo stato dal suo testo di trasporto è quello che percorre il confine vero, perché è
/// così che il server lo riceve.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ValueProgramCheckpointTests
{
    private static readonly IReadOnlyList<ValueInstruction> Program =
    [
        new SetVariable("set", "Punti", new LiteralValue(4)),
        new AskValue("ask", "Numero"),
        new ShowValue("show", new VariableValue("Numero")),
    ];

    private static ValueRunState Waiting() =>
        ValueProgramRunner.Start(Program, ["Numero", "Punti"], null, [], 100).RunToRest();

    [Fact]
    public void LoStatoDiUnaCorsaInAttesa_RaccontaDoveSiEfermaCheCosaAspetta()
    {
        var state = Waiting();

        Assert.Equal(ValueRunStatus.WaitingForValue, state.Status);
        Assert.Equal("valueprogram-1", state.EngineVersion);
        Assert.Equal(1, state.SchemaVersion);
        Assert.Equal(4, state.Variables["Punti"]);
        // Il blocco attivo è quello che attende: è ciò che rende possibile la ripresa esatta.
        Assert.Equal("ask", state.ActiveBlockId);
        Assert.NotEmpty(state.Frames);
    }

    [Fact]
    public void IlFormatoDiTrasporto_ConservaTuttoCioCheServeARiprendere()
    {
        var trasporto = JsonSerializer.SerializeToElement(Waiting(), Trasporto);

        // I nomi sono quelli che il server verifica: se cambiassero, il checkpoint verrebbe rifiutato
        // per un motivo che non ha niente a che vedere con la correttezza del lavoro dell'alunno.
        Assert.Equal(JsonValueKind.String, trasporto.GetProperty("status").ValueKind);
        Assert.Equal(JsonValueKind.Array, trasporto.GetProperty("frames").ValueKind);
        Assert.Equal(JsonValueKind.Object, trasporto.GetProperty("variables").ValueKind);
        Assert.Equal(JsonValueKind.Array, trasporto.GetProperty("pendingValues").ValueKind);
        Assert.True(trasporto.GetProperty("frames").GetArrayLength() > 0);
        // I nomi delle variabili sono quelli scelti dal docente, e non li tocca nessuna convenzione:
        // sono **chiavi** di un dizionario, non nomi di proprietà.
        Assert.Equal(4, trasporto.GetProperty("variables").GetProperty("Punti").GetInt32());
        Assert.False(trasporto.TryGetProperty("punti", out _));
    }

    [Fact]
    public void UnaCorsaRipresaDalloStato_RiprendeDallaStessaIstruzione()
    {
        // Il ciclo completo del server: stato serializzato, riletto dal formato di trasporto, ripreso
        // con il valore che l'alunno ha fornito.
        var trasporto = JsonSerializer.SerializeToElement(Waiting(), Trasporto);
        var ripresoDa = JsonSerializer.Deserialize<ValueRunState>(trasporto.GetRawText(), Trasporto)!;

        var ripreso = ValueProgramRunner
            .Resume(Program, ["Numero", "Punti"], null, ripresoDa with { PendingValues = [7] }, 100)
            .RunToRest();

        Assert.Equal(ValueRunStatus.Finished, ripreso.Status);
        Assert.Equal(["7"], ripreso.Output);
        // Punti è ancora quello salvato e Numero è il valore fornito: la ripresa non ricomincia da
        // capo, altrimenti il primo dei due non esisterebbe affatto.
        Assert.Equal(4, ripreso.Variables["Punti"]);
        Assert.Equal(7, ripreso.Variables["Numero"]);
    }

    /// <summary>Le impostazioni del canale HTTP: è con queste che il server riceve il checkpoint.</summary>
    private static readonly JsonSerializerOptions Trasporto = new(JsonSerializerDefaults.Web);

    [Fact]
    public void IlCheckpointDichiaratoDalMotore_EQuelloDelProprioFormato()
    {
        var descriptor = ExerciseEngineRegistry.Require(ValueProgramEngine.Version, "engineVersion");

        // Il numero di schema è 1 per entrambi i motori, e non significa la stessa cosa: il server lo
        // chiede alla versione del motore invece di fissarlo una volta per tutte.
        Assert.Equal(1, descriptor.CheckpointSchemaVersion);
        Assert.Equal(
            1,
            ExerciseEngineRegistry
                .Require(GridPathEngine.Version, "engineVersion").CheckpointSchemaVersion);
    }
}
