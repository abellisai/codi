using Qwerty.Codi.Application.Dto;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Exercises;
using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.Domain.Exercises;
using Qwerty.Codi.IntegrationTests.Realtime;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Sessions;

/// <summary>
/// Percorso completo di un esercizio dichiarativo (§13.9): il docente lo compone e lo pubblica con
/// la risposta attesa nella soluzione privata, l'alunno lo riceve, salva la sua risposta e legge un
/// esito corretto dal server.
///
/// È il caso che dimostra perché il tipo dichiarativo non è un esercizio di percorso travestito: il
/// lavoro del tentativo è un verbale di risposta, la correzione la fa il server, e la risposta
/// attesa non compare in nessun documento che l'alunno riceve.
/// </summary>
public sealed class QuizExerciseUseCaseTests : IAsyncLifetime
{
    private const string ExpectedAnswer = "6";

    private OutboxScenario scenario = null!;
    private Seed seed = null!;
    private JoinSessionOutcome joined = null!;
    private ParticipantSnapshotDto snapshot = null!;
    private AttemptDto attempt = null!;

    private CodiRequestContext Context => new(
        isAuthenticated: true,
        [CodiPolicy.AdmittedParticipant, CodiPolicy.ActiveParticipantConnection],
        joined.Joined.CredentialSessionId,
        participantId: joined.Joined.ParticipantId);

    public async Task InitializeAsync()
    {
        scenario = await OutboxScenario.CreateAsync();
        seed = await scenario.StartedSessionAsync();
        joined = await scenario.JoinAsync(seed, "Luca");

        var exercise = await scenario.Host.CommandAsAsync<ExerciseDto>(
            CreateExerciseCommand.RequestName, new { title = "Quanto vale 0110", exerciseType = "Quiz" }, scenario.RegiaContext());
        var draft = await scenario.Host.CommandAsAsync<ExerciseDraftDto>(
            SaveExerciseDraftCommand.RequestName,
            new
            {
                exerciseId = exercise.Id,
                definitionJson = QuizDefinition(),
                workspaceJson = QuizAnswerJson.Empty,
                // La risposta attesa vive nella soluzione privata: quello che l'alunno deve produrre
                // è il contenuto che il catalogo riserva al docente (§13.1).
                referenceSolutionJson = $"{{\"answer\":\"{ExpectedAnswer}\"}}",
            },
            scenario.RegiaContext());
        var version = await scenario.Host.CommandAsAsync<ExerciseVersionDto>(
            PublishExerciseVersionCommand.RequestName,
            new { exerciseId = exercise.Id, baseRowVersion = draft.RowVersion },
            scenario.RegiaContext());

        await AssignAsync(version.Id);
        await SynchronizeAsync();
        attempt = await scenario.Host.QueryOneAsAsync<AttemptDto>(
            GetAttemptQuery.RequestName, new { }, Context);
    }

    public async Task DisposeAsync() => await scenario.DisposeAsync();

    [Fact]
    public async Task Assegnato_IlLavoroInizialeEUnVerbaleVuoto()
    {
        // La copia di partenza dell'alunno non è un workspace di blocchi: è il verbale che dovrà
        // riempire, e il catalogo dei blocchi dell'esercizio è vuoto perché non c'è nulla da comporre.
        var exercise = await scenario.Host.QueryOneAsAsync<AssignedExerciseDto>(
            GetAssignedExerciseQuery.RequestName, new { }, Context);

        Assert.Equal(QuizEngine.ExerciseType, exercise.ExerciseType);
        Assert.Equal(QuizEngine.Version, exercise.EngineVersion);
        Assert.Equal(QuizAnswerJson.Empty, attempt.WorkspaceJson);

        // La definizione che l'alunno riceve non contiene la risposta attesa.
        Assert.DoesNotContain(ExpectedAnswer, exercise.DefinitionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RispostaCorretta_SalvataEConfermataDalServer()
    {
        await SaveAnswerAsync(ExpectedAnswer, revision: 2);

        var outcome = await OutcomeAsync();

        Assert.True(outcome.Correct);
        Assert.Equal("Giusto!", outcome.SuccessMessage);
    }

    [Fact]
    public async Task RispostaSbagliata_SalvataERifiutataDalServer()
    {
        await SaveAnswerAsync("5", revision: 2);

        var outcome = await OutcomeAsync();

        Assert.False(outcome.Correct);
        Assert.Equal("Riprova.", outcome.WrongMessage);
    }

    [Fact]
    public async Task RispostaNumerica_ConZeriInizialiEAccettata()
    {
        // Il confronto è numerico: «06» e «6» sono la stessa quantità, e un bambino che scrive lo
        // zero davanti non ha sbagliato l'esercizio.
        await SaveAnswerAsync("06", revision: 2);

        Assert.True((await OutcomeAsync()).Correct);
    }

    [Fact]
    public async Task VerbaleDiRisposta_ConProprietaNonPrevista_RifiutatoAlSalvataggio()
    {
        var request = new SaveAttemptWorkspaceCommand(
            attempt.AttemptId, Guid.NewGuid(), snapshot.ConnectionEpoch, attempt.RowVersion, 2,
            "{\"answer\":\"6\",\"punteggio\":3}",
            ExerciseContentHash.ComputeWorkspace("{\"answer\":\"6\",\"punteggio\":3}"));

        var failure = await scenario.Host.ExpectFailureAsAsync(
            SaveAttemptWorkspaceCommand.RequestName, request, Context);

        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
    }

    private async Task SaveAnswerAsync(string answer, long revision)
    {
        var workspace = $"{{\"answer\":\"{answer}\"}}";
        var request = new SaveAttemptWorkspaceCommand(
            attempt.AttemptId, Guid.NewGuid(), snapshot.ConnectionEpoch, attempt.RowVersion, revision,
            workspace, ExerciseContentHash.ComputeWorkspace(workspace));

        await scenario.Host.CommandAsAsync<AttemptSaveDto>(
            SaveAttemptWorkspaceCommand.RequestName, request, Context);
    }

    private Task<QuizOutcomeDto> OutcomeAsync() =>
        scenario.Host.QueryOneAsAsync<QuizOutcomeDto>(GetQuizOutcomeQuery.RequestName, new { }, Context);

    private async Task AssignAsync(Guid versionId) =>
        await scenario.Host.ExecuteAsAsync(
            CodiRequestKind.Command, AssignExerciseCommand.RequestName,
            new
            {
                sessionId = seed.SessionId,
                expectedSessionRevision = await scenario.ScalarAsync<long>(
                    "SELECT session_revision FROM lesson_session;"),
                exerciseVersionId = versionId,
            },
            scenario.RegiaContext());

    private async Task SynchronizeAsync()
    {
        snapshot = await scenario.Host.CommandAsAsync<ParticipantSnapshotDto>(
            InitializeParticipantConnectionCommand.RequestName,
            new
            {
                protocolVersion = 1,
                clientInstanceId = Guid.NewGuid(),
                appVersion = "test",
                engineVersion = QuizEngine.Version,
            },
            Context);
        var registry = scenario.Host.Services.GetRequiredService<IParticipantConnectionRegistry>();
        registry.Acquire(
            new(
                joined.Joined.ParticipantId, seed.SessionId, Guid.NewGuid(), "client",
                snapshot.ConnectionEpoch, snapshot.EffectiveRevision),
            CodiUseCaseHost.Now);
        registry.MarkSnapshotApplied(
            new(
                joined.Joined.ParticipantId, "client", snapshot.ConnectionEpoch,
                snapshot.EffectiveRevision),
            CodiUseCaseHost.Now);
    }

    /// <summary>Definizione di un esercizio dichiarativo: legge un numero binario e ne chiede il valore.</summary>
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
}
