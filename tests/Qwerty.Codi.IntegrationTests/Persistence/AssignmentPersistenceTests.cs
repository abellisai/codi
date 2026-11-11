using System.Data.Common;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Sessions;
using Qwerty.Codi.Infrastructure.Migrations;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Persistence;

/// <summary>
/// Persistenza di assegnazioni e tentativi (§7.3). Verifica ciò che solo un database può dire: che il
/// mapping nuovo trovi le colonne, che i vincoli rifiutino un tentativo con la versione sbagliata o un
/// numero ripetuto, e che la lettura per coppia restituisca il lavoro del solo partecipante richiesto.
/// </summary>
public sealed class AssignmentPersistenceTests : IAsyncLifetime
{
    private const string SessionId = "11111111-1111-1111-1111-111111111111";
    private const string ParticipantId = "22222222-2222-2222-2222-222222222222";
    private const string OtherParticipantId = "33333333-3333-3333-3333-333333333333";
    private const string VersionId = "44444444-4444-4444-4444-444444444444";

    private static readonly DateTimeOffset Now = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    private TempSqliteDatabase _database = null!;

    public async Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();
        await new MigrationRunner(_database.Factory, new TestTimeProvider()).ApplyAsync(
            MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly), CancellationToken.None);

        await using var connection = await _database.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = $$"""
            INSERT INTO school (id, name, normalized_name, created_at_utc_ms, updated_at_utc_ms)
                VALUES ('{{SessionId}}', 'Scuola', 'SCUOLA', 1000, 1000);
            INSERT INTO classroom (id, school_id, name, normalized_name, school_year, created_at_utc_ms, updated_at_utc_ms)
                VALUES ('{{SessionId}}', '{{SessionId}}', 'Terza A', 'TERZA A', '2026/2027', 1000, 1000);
            INSERT INTO course (id, title, created_at_utc_ms, updated_at_utc_ms)
                VALUES ('{{SessionId}}', 'Corso', 1000, 1000);
            INSERT INTO lesson (id, course_id, title, created_at_utc_ms, updated_at_utc_ms)
                VALUES ('{{SessionId}}', '{{SessionId}}', 'Lezione', 1000, 1000);
            INSERT INTO exercise (id, title, created_at_utc_ms, updated_at_utc_ms)
                VALUES ('{{SessionId}}', 'Esercizio', 1000, 1000);
            INSERT INTO admin_user (id, username, normalized_username, password_hash, security_stamp, created_at_utc_ms, updated_at_utc_ms)
                VALUES ('{{SessionId}}', 'Admin', 'ADMIN', 'hash', 'stamp', 1000, 1000);
            INSERT INTO exercise_version (id, exercise_id, version_number, definition_json, initial_workspace_json, engine_version, schema_version, content_hash, published_by_admin_id, created_at_utc_ms)
                VALUES ('{{VersionId}}', '{{SessionId}}', 1, '{"schemaVersion":1}', '{"blocks":[]}', 'gridpath-1', '1', 'impronta-versione', '{{SessionId}}', 1000);
            INSERT INTO lesson_session (id, lesson_id, classroom_id, controller_admin_id, status, admissions_open, session_revision, class_default_json, started_at_utc_ms, created_at_utc_ms, updated_at_utc_ms)
                VALUES ('{{SessionId}}', '{{SessionId}}', '{{SessionId}}', '{{SessionId}}', 'Active', 1, 1, '{"view":"Waiting"}', 1000, 1000, 1000);
            INSERT INTO session_participant (id, session_id, station_id, display_name, admission_status, connection_epoch, joined_at_utc_ms, created_at_utc_ms, updated_at_utc_ms)
                VALUES ('{{ParticipantId}}', '{{SessionId}}', NULL, 'Luca', 'Admitted', 0, 1000, 1000, 1000),
                       ('{{OtherParticipantId}}', '{{SessionId}}', NULL, 'Anna', 'Admitted', 0, 1000, 1000, 1000);
            """;

        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync()
    {
        _database.Dispose();

        return Task.CompletedTask;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AssegnazioneETentativo_SiScrivonoESiRileggonoConIlLoroMapping()
    {
        var assignment = await InsertAssignmentAsync(AssignmentScope.Class, targetParticipantId: null);
        var attempt = await InsertAttemptAsync(assignment.Id, ParticipantId, attemptNumber: 1);

        await using var read = await _database.Units.CreateAsync();
        var storedAssignment = (await read.Repo<IActivityAssignmentRepository>()
            .GetAsync(assignment.Id.ToString("D")))!.ToDomain();
        var storedAttempt = (await read.Repo<IExerciseAttemptRepository>()
            .GetAsync(attempt.Id.ToString("D")))!.ToDomain();

        Assert.Equal(AssignmentScope.Class, storedAssignment.Scope);
        Assert.Equal(Guid.Parse(VersionId), storedAssignment.ExerciseVersionId);
        Assert.True(storedAssignment.Reaches(Guid.Parse(OtherParticipantId)));

        Assert.Equal(AttemptStatus.Ready, storedAttempt.Status);
        Assert.Equal(1L, storedAttempt.AttemptNumber);
        Assert.Equal(0L, storedAttempt.ProjectRevision);
        Assert.Equal("impronta-1", storedAttempt.WorkspaceHash);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Lettura_DeiTentativi_TornaQuelliDelSoloPartecipanteRichiesto()
    {
        var assignment = await InsertAssignmentAsync(AssignmentScope.Class, targetParticipantId: null);
        await InsertAttemptAsync(assignment.Id, ParticipantId, attemptNumber: 1);
        await InsertAttemptAsync(assignment.Id, ParticipantId, attemptNumber: 2);
        await InsertAttemptAsync(assignment.Id, OtherParticipantId, attemptNumber: 1);

        await using var read = await _database.Units.CreateAsync();
        var attempts = await read.Repo<IExerciseAttemptRepository>()
            .QueryByAssignmentAndParticipantAsync(assignment.Id, Guid.Parse(ParticipantId));

        Assert.Equal([1L, 2L], attempts.Select(row => row.AttemptNumber));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Tentativo_ConNumeroRipetuto_Rifiutato()
    {
        // §7.3: il numero è progressivo per coppia partecipante/assegnazione, e non si riusa: è ciò
        // che rende riconoscibile il tentativo corrente dopo un azzeramento.
        var assignment = await InsertAssignmentAsync(AssignmentScope.Class, targetParticipantId: null);
        await InsertAttemptAsync(assignment.Id, ParticipantId, attemptNumber: 1);

        await Assert.ThrowsAnyAsync<Exception>(
            () => InsertAttemptAsync(assignment.Id, ParticipantId, attemptNumber: 1));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Tentativo_ConVersioneDiversaDaQuellaAssegnata_Rifiutato()
    {
        // §7.3: «riferimento versione coerente con assegnazione». La chiave esterna composta lo impone,
        // quindi una versione diversa non è una svista che il database accetta.
        var assignment = await InsertAssignmentAsync(AssignmentScope.Class, targetParticipantId: null);

        var stranger = new ExerciseAttemptEntity
        {
            Id = Guid.NewGuid().ToString("D"),
            SessionId = SessionId,
            ParticipantId = ParticipantId,
            AssignmentId = assignment.Id.ToString("D"),
            ExerciseVersionId = Guid.NewGuid().ToString("D"),
            AttemptNumber = 1,
            Status = nameof(AttemptStatus.Ready),
            WorkspaceJson = """{"blocks":[]}""",
            WorkspaceHash = "impronta-1",
            CreatedAtUtcMs = Now.ToUnixTimeMilliseconds(),
            UpdatedAtUtcMs = Now.ToUnixTimeMilliseconds(),
            RowVersion = 1,
        };

        await using var write = await _database.Units.CreateAsync(useTransaction: true);

        await Assert.ThrowsAnyAsync<Exception>(
            () => write.Repo<IExerciseAttemptRepository>().InsertAsync(stranger));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Assegnazione_ConDestinatarioDiUnAltraSessione_Rifiutata()
    {
        // Il destinatario di un'assegnazione individuale appartiene alla sessione dell'assegnazione:
        // la chiave esterna composta sulla partecipazione lo impone.
        await using var write = await _database.Units.CreateAsync(useTransaction: true);

        var assignment = new ActivityAssignmentEntity
        {
            Id = Guid.NewGuid().ToString("D"),
            SessionId = Guid.NewGuid().ToString("D"),
            LessonExerciseId = null,
            Scope = nameof(AssignmentScope.Participant),
            TargetParticipantId = ParticipantId,
            ExerciseVersionId = VersionId,
            CreatedAtUtcMs = Now.ToUnixTimeMilliseconds(),
        };

        await Assert.ThrowsAnyAsync<Exception>(
            () => write.Repo<IActivityAssignmentRepository>().InsertAsync(assignment));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Riga_ConAmbitoOStatoNonRiconosciuto_RifiutaLaConversione()
    {
        // Il testo nella colonna è un contratto: leggerlo come «classe» estenderebbe a tutti
        // un'assegnazione destinata a uno solo, e «pronto» riabiliterebbe un tentativo sospeso.
        var assignment = new ActivityAssignmentEntity
        {
            Id = Guid.NewGuid().ToString("D"),
            SessionId = SessionId,
            LessonExerciseId = null,
            Scope = "TuttaLaClasse",
            TargetParticipantId = null,
            ExerciseVersionId = VersionId,
            CreatedAtUtcMs = Now.ToUnixTimeMilliseconds(),
        };

        var attempt = new ExerciseAttemptEntity
        {
            Id = Guid.NewGuid().ToString("D"),
            SessionId = SessionId,
            ParticipantId = ParticipantId,
            AssignmentId = Guid.NewGuid().ToString("D"),
            ExerciseVersionId = VersionId,
            AttemptNumber = 1,
            Status = "QuasiPronto",
            WorkspaceJson = """{"blocks":[]}""",
            WorkspaceHash = "impronta-1",
            CreatedAtUtcMs = Now.ToUnixTimeMilliseconds(),
            UpdatedAtUtcMs = Now.ToUnixTimeMilliseconds(),
            RowVersion = 1,
        };

        Assert.Throws<ArgumentException>(() => assignment.ToDomain());
        Assert.Throws<ArgumentException>(() => attempt.ToDomain());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Tentativo_Riletto_ConservaRevisioneEStatoAggiornati()
    {
        // Il tentativo si modifica: la lettura riporta il progetto, la sua impronta e la revisione su
        // cui il player dichiara di basarsi.
        var assignment = await InsertAssignmentAsync(AssignmentScope.Participant, Guid.Parse(ParticipantId));
        var attempt = await InsertAttemptAsync(assignment.Id, ParticipantId, attemptNumber: 1);

        await using var write = await _database.Units.CreateAsync(useTransaction: true);
        var row = await write.Repo<IExerciseAttemptRepository>().GetAsync(attempt.Id.ToString("D"));
        row!.Status = nameof(AttemptStatus.Paused);
        row.WorkspaceHash = "impronta-2";
        row.ProjectRevision = 7;
        row.CheckpointJson = """{"step":3}""";
        row.CheckpointEngineVersion = "gridpath-1";
        row.LastSavedAtUtcMs = Now.AddMinutes(1).ToUnixTimeMilliseconds();
        row.UpdatedAtUtcMs = Now.AddMinutes(1).ToUnixTimeMilliseconds();

        Assert.True(await write.Repo<IExerciseAttemptRepository>().UpdateAsync(row));
        await write.CommitAsync(CancellationToken.None);

        await using var read = await _database.Units.CreateAsync();
        var storedRow = await read.Repo<IExerciseAttemptRepository>().GetAsync(attempt.Id.ToString("D"));
        var stored = storedRow!.ToDomain();

        Assert.Equal(AttemptStatus.Paused, stored.Status);
        Assert.Equal("impronta-2", stored.WorkspaceHash);
        Assert.Equal(7L, stored.ProjectRevision);
        Assert.Equal(2L, stored.RowVersion);

        // Il checkpoint vive nella riga e serve alla ripresa: sopravvive al salvataggio del progetto.
        Assert.Equal("""{"step":3}""", storedRow.CheckpointJson);
        Assert.Equal("gridpath-1", storedRow.CheckpointEngineVersion);
        Assert.Equal(Now.AddMinutes(1).ToUnixTimeMilliseconds(), storedRow.LastSavedAtUtcMs);
        Assert.Null(storedRow.CompletedAtUtcMs);
    }

    private async Task<ActivityAssignment> InsertAssignmentAsync(
        AssignmentScope scope,
        Guid? targetParticipantId)
    {
        var assignment = ActivityAssignment.Create(
            Guid.Parse(SessionId),
            lessonExerciseId: null,
            scope,
            targetParticipantId,
            Guid.Parse(VersionId),
            Now);

        await using var write = await _database.Units.CreateAsync(useTransaction: true);
        await write.Repo<IActivityAssignmentRepository>()
            .InsertAsync(ActivityAssignmentEntity.FromDomain(assignment));
        await write.CommitAsync(CancellationToken.None);

        return assignment;
    }

    private async Task<ExerciseAttempt> InsertAttemptAsync(
        Guid assignmentId,
        string participantId,
        long attemptNumber)
    {
        var attempt = ExerciseAttempt.Create(
            Guid.Parse(SessionId),
            Guid.Parse(participantId),
            assignmentId,
            Guid.Parse(VersionId),
            attemptNumber,
            """{"blocks":[]}""",
            "impronta-1",
            Now);

        await using var write = await _database.Units.CreateAsync(useTransaction: true);
        await write.Repo<IExerciseAttemptRepository>().InsertAsync(ExerciseAttemptEntity.FromDomain(attempt));
        await write.CommitAsync(CancellationToken.None);

        return attempt;
    }
}
