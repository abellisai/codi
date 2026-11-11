using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Exercises;
using Qwerty.Codi.Application.Lessons;
using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.IntegrationTests.Realtime;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Exercises;

/// <summary>
/// Eliminazione di una versione pubblicata che non è più la più recente (§10.4, §13.8) dal percorso
/// reale: registro, canale, policy, deserializzazione, mediator, handler, repository e database
/// migrato.
///
/// La versione è immutabile e non ha <c>row_version</c>, quindi la cancellazione non passa dal CRUD
/// comune ma da un predicato sull'identificativo: questi casi verificano che spariscano la versione
/// **e** la sua riga privata, che l'ultima versione resti al suo posto e che una versione già
/// assegnata non si possa togliere sotto il lavoro degli alunni.
/// </summary>
public sealed class DeleteExerciseVersionTests
{
    /// <summary>Progetto iniziale della prima versione: il punto di ingresso segue un passo.</summary>
    private const string FirstWorkspace =
        """{"blocks":{"languageVersion":0,"blocks":[{"type":"aula_start","id":"start-1","next":{"block":{"type":"aula_move","id":"move-1"}}}]}}""";

    /// <summary>Progetto iniziale della seconda versione: diverso dal primo, così la versione è nuova.</summary>
    private const string SecondWorkspace =
        """{"blocks":{"languageVersion":0,"blocks":[{"type":"aula_start","id":"start-2","next":{"block":{"type":"aula_turn","id":"turn-1","fields":{"TURN":"Right"}}}}]}}""";

    /// <summary>Soluzione privata: un programma completo, con il suo punto di ingresso.</summary>
    private const string Solution =
        """{"blocks":{"languageVersion":0,"blocks":[{"type":"aula_start","id":"solution-start","next":{"block":{"type":"aula_move","id":"solution-move"}}}]}}""";

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteExerciseVersion_NonPiuRecente_RimuoveLaVersioneEIlSuoContenutoPrivato()
    {
        // Arrange: due versioni pubblicate dallo stesso esercizio, ciascuna con la sua riga privata.
        await using var scenario = await OutboxScenario.CreateAsync();
        var exercise = await CreateExerciseAsync(scenario);
        var first = await PublishAsync(scenario, exercise.Id, FirstWorkspace);
        var second = await PublishAsync(scenario, exercise.Id, SecondWorkspace, baseRowVersion: 1);
        Assert.Equal(1, first.VersionNumber);
        Assert.Equal(2, second.VersionNumber);
        Assert.Equal(2L, await CountAsync(scenario, "SELECT COUNT(*) FROM exercise_version_private;"));

        // Act: il numero viaggia come stringa decimale, la forma che usa il client (§10.2).
        var deleted = await scenario.Host.CommandAsAsync<ExerciseVersionDeletedDto>(
            DeleteExerciseVersionCommand.RequestName,
            new
            {
                exerciseId = exercise.Id,
                versionNumber = first.VersionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            scenario.RegiaContext());

        // Assert: la versione indicata non c'è più, la più recente resta e il materiale è invariato.
        Assert.Equal(exercise.Id, deleted.ExerciseId);
        Assert.Equal(1, deleted.VersionNumber);
        var versions = await scenario.Host.QueryAsync<ExerciseVersionDto>(
            ListExerciseVersionsQuery.RequestName, new { exerciseId = exercise.Id });
        Assert.Equal(new[] { second.Id }, versions.Select(version => version.Id));
        Assert.Equal(0L, await CountAsync(
            scenario,
            $"SELECT COUNT(*) FROM exercise_version_private WHERE exercise_version_id = '{first.Id:D}';"));
        Assert.Equal(1L, await CountAsync(
            scenario,
            $"SELECT COUNT(*) FROM exercise_version_private WHERE exercise_version_id = '{second.Id:D}';"));
        var detail = await scenario.Host.QueryOneAsync<ExerciseDto>(
            GetExerciseQuery.RequestName, new { id = exercise.Id });
        Assert.Equal("Available", detail.PublicationStatus);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteExerciseVersion_PiuRecente_RifiutaConConflittoDiStato()
    {
        // Arrange: due versioni, la seconda è quella che le prossime assegnazioni userebbero.
        await using var scenario = await OutboxScenario.CreateAsync();
        var exercise = await CreateExerciseAsync(scenario);
        await PublishAsync(scenario, exercise.Id, FirstWorkspace);
        var second = await PublishAsync(scenario, exercise.Id, SecondWorkspace, baseRowVersion: 1);

        // Act
        var failure = await scenario.Host.ExpectFailureAsAsync(
            DeleteExerciseVersionCommand.RequestName,
            new { exerciseId = exercise.Id, versionNumber = second.VersionNumber },
            scenario.RegiaContext());

        // Assert: il rifiuto è un conflitto di stato, e nulla è stato toccato.
        Assert.Equal(CodiErrorCodes.LatestExerciseVersion, failure.Code);
        Assert.Equal(2L, await CountAsync(scenario, "SELECT COUNT(*) FROM exercise_version;"));
        Assert.Equal(2L, await CountAsync(scenario, "SELECT COUNT(*) FROM exercise_version_private;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteExerciseVersion_AssegnataAUnaLezione_RifiutaConConflittoDiStato()
    {
        // Arrange: la prima versione è già il materiale di una sessione avviata.
        await using var scenario = await OutboxScenario.CreateAsync();
        var exercise = await CreateExerciseAsync(scenario);
        var first = await PublishAsync(scenario, exercise.Id, FirstWorkspace);
        await PublishAsync(scenario, exercise.Id, SecondWorkspace, baseRowVersion: 1);
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");
        await scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            AssignExerciseCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await RevisionAsync(scenario, seed.SessionId),
                exerciseVersionId = first.Id,
            },
            scenario.RegiaContext());

        // Act
        var failure = await scenario.Host.ExpectFailureAsAsync(
            DeleteExerciseVersionCommand.RequestName,
            new { exerciseId = exercise.Id, versionNumber = first.VersionNumber },
            scenario.RegiaContext());

        // Assert: la versione resta, perché gli alunni ci stanno lavorando.
        Assert.Equal(CodiErrorCodes.ExerciseVersionInUse, failure.Code);
        Assert.Equal(2L, await CountAsync(scenario, "SELECT COUNT(*) FROM exercise_version;"));
        Assert.Equal(1L, await CountAsync(scenario, "SELECT COUNT(*) FROM activity_assignment;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteExerciseVersion_Inesistente_RestituisceRisorsaNonTrovata()
    {
        // Arrange
        await using var scenario = await OutboxScenario.CreateAsync();
        var exercise = await CreateExerciseAsync(scenario);
        await PublishAsync(scenario, exercise.Id, FirstWorkspace);

        // Act: un numero che l'esercizio non ha, e un esercizio che non esiste.
        var missingVersion = await scenario.Host.ExpectFailureAsAsync(
            DeleteExerciseVersionCommand.RequestName,
            new { exerciseId = exercise.Id, versionNumber = 7 },
            scenario.RegiaContext());
        var missingExercise = await scenario.Host.ExpectFailureAsAsync(
            DeleteExerciseVersionCommand.RequestName,
            new { exerciseId = Guid.NewGuid(), versionNumber = 1 },
            scenario.RegiaContext());

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, missingVersion.Code);
        Assert.Equal(CodiErrorCodes.ResourceNotFound, missingExercise.Code);
        Assert.Equal(1L, await CountAsync(scenario, "SELECT COUNT(*) FROM exercise_version;"));
    }

    private static Task<long> CountAsync(OutboxScenario scenario, string sql) =>
        scenario.ScalarAsync<long>(sql);

    /// <summary>Revisione corrente come la legge il docente prima di un comando di regia (§6.4).</summary>
    private static Task<long> RevisionAsync(OutboxScenario scenario, Guid sessionId) =>
        scenario.ScalarAsync<long>(
            $"SELECT session_revision FROM lesson_session WHERE id = '{sessionId:D}';");

    private static Task<ExerciseDto> CreateExerciseAsync(OutboxScenario scenario) =>
        scenario.Host.CommandAsAsync<ExerciseDto>(
            CreateExerciseCommand.RequestName,
            new { exerciseType = "GridPath", title = "Raggiungi la stella" },
            scenario.RegiaContext());

    /// <summary>
    /// Salva la bozza con il progetto indicato e la pubblica. Il progetto iniziale diverso è ciò che
    /// rende nuova la seconda versione: la pubblicazione riconosce un contenuto identico (§13.8).
    /// </summary>
    private static async Task<ExerciseVersionDto> PublishAsync(
        OutboxScenario scenario,
        Guid exerciseId,
        string workspaceJson,
        long? baseRowVersion = null)
    {
        var save = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["exerciseId"] = exerciseId,
            ["definitionJson"] = DefinitionJson(),
            ["workspaceJson"] = workspaceJson,
            ["referenceSolutionJson"] = Solution,
        };

        if (baseRowVersion is not null)
        {
            save["baseRowVersion"] =
                baseRowVersion.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var draft = await scenario.Host.CommandAsAsync<ExerciseDraftDto>(
            SaveExerciseDraftCommand.RequestName, save, scenario.RegiaContext());

        return await scenario.Host.CommandAsAsync<ExerciseVersionDto>(
            PublishExerciseVersionCommand.RequestName,
            new
            {
                exerciseId,
                baseRowVersion = draft.RowVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                authorNotes = (string?)null,
            },
            scenario.RegiaContext());
    }

    /// <summary>Definizione di §13.2 usata dalla pubblicazione di prova.</summary>
    private static string DefinitionJson() =>
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
          "toolbox": { "allowedBlocks": ["aula_start", "aula_move", "aula_turn"] },
          "limits": { "maxBlocks": 30, "maxNestingDepth": 8, "maxExecutionSteps": 1000, "maxActiveDurationMs": 120000 },
          "completion": { "type": "ReachCell", "x": 3, "y": 1 },
          "feedback": { "success": "Bravo!", "collision": "Ostacolo." }
        }
        """;
}
