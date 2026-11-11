using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Lessons;
using Qwerty.Codi.Application.Organization;
using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.IntegrationTests.Realtime;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Sessions;

/// <summary>
/// Eliminazione definitiva di una sessione (§6.1, §10.4) dal percorso reale: registro, canale,
/// policy, deserializzazione, mediator, handler, rimozione dei figli e database migrato. La scena è
/// quella dei casi d'uso — scuola, classe, lezione, sessione avviata, alunno entrato, esercizio
/// assegnato e sessione conclusa — perché la cancellazione deve togliere **tutto** ciò che quella
/// sessione ha prodotto senza toccare il materiale didattico.
/// </summary>
public sealed class DeleteSessionTests
{
    private const string InitialWorkspaceJson = """{"blocks":[]}""";

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteSession_DaEnded_RimuoveLaSessioneEISuoiDatiSenzaToccareIlMateriale()
    {
        // Arrange: una lezione svolta fino in fondo, con un tentativo e un permesso conclusivo.
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        var joined = await scenario.JoinAsync(seed, "Luca");
        var lessonId = await scenario.ScalarAsync<string>(
            $"SELECT lesson_id FROM lesson_session WHERE id = '{seed.SessionId:D}';");
        var versionId = Guid.NewGuid();
        await SeedPublishedExerciseAsync(scenario, Guid.Parse(lessonId), versionId);
        await scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            AssignExerciseCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = await RevisionAsync(scenario, seed.SessionId), exerciseVersionId = versionId },
            scenario.RegiaContext());
        await scenario.Host.CommandAsAsync<SessionDto>(
            EndSessionCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = await RevisionAsync(scenario, seed.SessionId) },
            scenario.RegiaContext());

        // La premessa è verificata: senza tentativo e permesso conclusivo il caso non proverebbe nulla.
        Assert.Equal(1L, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM exercise_attempt;"));
        Assert.Equal(1L, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM attempt_finalization_grant;"));
        Assert.Equal(1L, await scenario.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM credential_session WHERE participant_id = '{joined.Joined.ParticipantId:D}';"));

        // Act
        var deleted = await scenario.Host.CommandAsAsync<SessionDeletedDto>(
            DeleteSessionCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = await RevisionAsync(scenario, seed.SessionId) },
            scenario.RegiaContext());

        // Assert: l'esito indica la sessione sparita e nessuna tabella della sessione conserva righe.
        Assert.Equal(seed.SessionId, deleted.SessionId);
        foreach (var table in new[]
                 {
                     "lesson_session", "session_participant", "participant_state", "session_command",
                     "command_delivery", "outbox_message", "activity_assignment", "exercise_attempt",
                     "attempt_finalization_grant", "credential_session",
                 })
        {
            Assert.Equal(0L, await scenario.ScalarAsync<long>($"SELECT COUNT(*) FROM {table};"));
        }

        // Il materiale didattico non appartiene alla sessione e resta: scuola, classe, corso,
        // lezione, esercizio, versione e casella di sequenza.
        foreach (var table in new[] { "school", "classroom", "lesson", "exercise", "exercise_version", "lesson_exercise" })
        {
            Assert.Equal(1L, await scenario.ScalarAsync<long>($"SELECT COUNT(*) FROM {table};"));
        }

        // La sessione eliminata non è più leggibile dall'elenco della regia.
        var sessions = await scenario.Host.QueryAsync<SessionSummaryDto>(ListSessionsQuery.RequestName, new { });
        Assert.Empty(sessions);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteSession_DaLobby_RimuoveUnaSessioneMaiAvviata()
    {
        // Arrange: istanziata e mai avviata, quindi senza partecipanti né comandi.
        await using var scenario = await OutboxScenario.CreateAsync();
        var session = await CreatedSessionAsync(scenario);

        // Act
        var deleted = await scenario.Host.CommandAsAsync<SessionDeletedDto>(
            DeleteSessionCommand.RequestName,
            new { sessionId = session.Id, expectedSessionRevision = session.SessionRevision },
            scenario.RegiaContext());

        // Assert
        Assert.Equal(session.Id, deleted.SessionId);
        Assert.Equal(0L, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM lesson_session;"));
        Assert.Equal(1L, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM lesson;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteSession_DuranteLaLezione_RifiutaConConflittoDiStatoEConservaIDati()
    {
        // Arrange: la sessione è in corso e ha già un alunno dentro.
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.JoinAsync(seed, "Luca");

        // Act: la revisione è corretta, ma la chiusura è un atto distinto e va fatta prima.
        var failure = await scenario.Host.ExpectFailureAsAsync(
            DeleteSessionCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = await RevisionAsync(scenario, seed.SessionId) },
            scenario.RegiaContext());

        // Assert
        Assert.Equal(CodiErrorCodes.SessionStateConflict, failure.Code);
        Assert.Equal(1L, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM lesson_session;"));
        Assert.Equal(1L, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
        Assert.Equal(1L, await scenario.ScalarAsync<long>("SELECT COUNT(*) FROM participant_state;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteSession_SenzaRevisioneAttesa_RichiedeLaVersioneERifiuta()
    {
        // Arrange: una sessione conclusa, cancellabile per stato e provenienza.
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.Host.CommandAsAsync<SessionDto>(
            EndSessionCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = await RevisionAsync(scenario, seed.SessionId) },
            scenario.RegiaContext());

        // Act: il corpo non dichiara la revisione letta dal docente (§10.2).
        var failure = await scenario.Host.ExpectFailureAsAsync(
            DeleteSessionCommand.RequestName,
            new { sessionId = seed.SessionId },
            scenario.RegiaContext());

        // Assert
        Assert.Equal(CodiErrorCodes.EntityVersionRequired, failure.Code);
        Assert.Equal(1L, await scenario.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM lesson_session WHERE id = '{seed.SessionId:D}';"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteSession_ConRevisioneSuperata_RifiutaConConflittoDiRevisione()
    {
        // Arrange
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        await scenario.Host.CommandAsAsync<SessionDto>(
            EndSessionCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = await RevisionAsync(scenario, seed.SessionId) },
            scenario.RegiaContext());

        // Act
        var failure = await scenario.Host.ExpectFailureAsAsync(
            DeleteSessionCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = 99L },
            scenario.RegiaContext());

        // Assert
        Assert.Equal(CodiErrorCodes.SessionRevisionConflict, failure.Code);
        Assert.Equal(1L, await scenario.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM lesson_session WHERE id = '{seed.SessionId:D}';"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteSession_DiUnAltroAmministratore_RestituisceRisorsaNonTrovata()
    {
        // Arrange: la sessione appartiene all'amministratore della scena.
        await using var scenario = await OutboxScenario.CreateAsync();
        var session = await CreatedSessionAsync(scenario);

        // Act: un altro amministratore non deve poterla eliminare, e non deve nemmeno sapere che esiste.
        var otherAdmin = new CodiRequestContext(
            isAuthenticated: true,
            [CodiPolicy.Admin, CodiPolicy.SessionController],
            adminUserId: Guid.NewGuid());
        var failure = await scenario.Host.ExpectFailureAsAsync(
            DeleteSessionCommand.RequestName,
            new { sessionId = session.Id, expectedSessionRevision = session.SessionRevision },
            otherAdmin);

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
        Assert.Equal(1L, await scenario.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM lesson_session WHERE id = '{session.Id:D}';"));
    }

    /// <summary>Revisione corrente come la legge il docente prima di un comando di regia (§6.4).</summary>
    private static Task<long> RevisionAsync(OutboxScenario scenario, Guid sessionId) =>
        scenario.ScalarAsync<long>(
            $"SELECT session_revision FROM lesson_session WHERE id = '{sessionId:D}';");

    /// <summary>Scuola, classe, corso, lezione e sessione in <c>Lobby</c>, dal percorso reale.</summary>
    private static async Task<SessionDto> CreatedSessionAsync(OutboxScenario scenario)
    {
        var school = await scenario.Host.CommandAsAsync<SchoolDto>(
            CreateSchoolCommand.RequestName, new { name = "Scuola Manzoni" }, scenario.RegiaContext());
        var classroom = await scenario.Host.CommandAsAsync<ClassroomDto>(
            CreateClassroomCommand.RequestName,
            new { schoolId = school.Id, name = "Terza A", schoolYear = "2026/2027" },
            scenario.RegiaContext());
        var course = await scenario.Host.CommandAsAsync<CourseDto>(
            CreateCourseCommand.RequestName, new { title = "Informatica di base" }, scenario.RegiaContext());
        var lesson = await scenario.Host.CommandAsAsync<LessonDto>(
            CreateLessonCommand.RequestName, new { courseId = course.Id, title = "Prima lezione" }, scenario.RegiaContext());

        return await scenario.Host.CommandAsAsync<SessionDto>(
            CreateSessionCommand.RequestName,
            new { lessonId = lesson.Id, classroomId = classroom.Id },
            scenario.RegiaContext());
    }

    /// <summary>
    /// Esercizio pubblicato e casella di sequenza della lezione: la pubblicazione ha i propri test,
    /// quindi qui si inseriscono soltanto l'identità e la versione immutabile da assegnare.
    /// </summary>
    private static async Task SeedPublishedExerciseAsync(
        OutboxScenario scenario,
        Guid lessonId,
        Guid versionId)
    {
        var exerciseId = Guid.NewGuid();

        await using var connection = await scenario.Host.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO exercise (id, title, description, is_archived, created_at_utc_ms, updated_at_utc_ms, row_version)
            VALUES ($id, 'Disegno con i blocchi', '', 0, 1000, 1000, 1);
            """,
            ("$id", exerciseId.ToString("D")));

        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO exercise_version (
                id, exercise_id, version_number, definition_json, initial_workspace_json,
                engine_version, schema_version, content_hash, published_by_admin_id, created_at_utc_ms)
            VALUES ($id, $exerciseId, 1, '{}', $workspace, '1.0.0', '1.0.0', 'impronta-di-prova', $adminId, 1000);
            """,
            ("$id", versionId.ToString("D")),
            ("$exerciseId", exerciseId.ToString("D")),
            ("$workspace", InitialWorkspaceJson),
            ("$adminId", scenario.AdminId.ToString("D")));

        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO lesson_exercise (id, lesson_id, exercise_id, position, created_at_utc_ms, updated_at_utc_ms, row_version)
            VALUES ($id, $lessonId, $exerciseId, 1, 1000, 1000, 1);
            """,
            ("$id", Guid.NewGuid().ToString("D")),
            ("$lessonId", lessonId.ToString("D")),
            ("$exerciseId", exerciseId.ToString("D")));

        await transaction.CommitAsync();
    }

    private static async Task ExecuteAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction? transaction,
        string sql,
        params (string Name, string Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync();
    }
}
