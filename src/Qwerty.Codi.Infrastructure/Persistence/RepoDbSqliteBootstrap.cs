using RepoDb;
using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Infrastructure.Persistence;

/// <summary>
/// Inizializza una sola volta il provider SQLite di RepoDB realmente adottato dalla
/// soluzione (§8.1, D-01): <c>RepoDb.Sqlite.Microsoft</c> su <c>Microsoft.Data.Sqlite</c>.
/// RepoDB risolve il <c>DbSetting</c> a partire dal tipo di connessione: senza questa
/// inizializzazione le operazioni su una <c>Microsoft.Data.Sqlite.SqliteConnection</c> non
/// hanno un mapping e falliscono a runtime. Il bootstrap dell'applicazione deve invocarla
/// prima del primo accesso ai repository.
/// </summary>
public static class RepoDbSqliteBootstrap
{
    private static readonly object Gate = new();

    private static bool _initialized;

    /// <summary>
    /// Inizializza il provider se non è già stato inizializzato. Idempotente e sicuro
    /// rispetto alle chiamate concorrenti.
    /// </summary>
    public static void EnsureInitialized()
    {
        if (Volatile.Read(ref _initialized))
        {
            return;
        }

        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            // `SqliteBootstrap.Initialize()` è deprecato: il provider segnala che la classe
            // diventerà interna e indica di usare questa API (CS0618).
            GlobalConfiguration.Setup().UseSqlite();
            MapEntities();
            Volatile.Write(ref _initialized, true);
        }
    }

    private static void MapEntities()
    {
        FluentMapper.Entity<AdminUserEntity>()
            .Table("admin_user").Primary(entity => entity.Id)
            .Column(entity => entity.Id, "id")
            .Column(entity => entity.Username, "username")
            .Column(entity => entity.NormalizedUsername, "normalized_username")
            .Column(entity => entity.PasswordHash, "password_hash")
            .Column(entity => entity.SecurityStamp, "security_stamp")
            .Column(entity => entity.IsActiveFlag, "is_active")
            .Column(entity => entity.FailedLoginAttempts, "failed_login_count")
            .Column(entity => entity.LockedUntilUtcMs!, "locked_until_utc_ms")
            .Column(entity => entity.CreatedAtUtcMs, "created_at_utc_ms")
            .Column(entity => entity.UpdatedAtUtcMs, "updated_at_utc_ms")
            .Column(entity => entity.RowVersion, "row_version");

        FluentMapper.Entity<SchoolEntity>()
            .Table("school").Primary(entity => entity.Id)
            .Column(entity => entity.Id, "id")
            .Column(entity => entity.Name, "name")
            .Column(entity => entity.NormalizedName, "normalized_name")
            .Column(entity => entity.IsArchivedFlag, "is_archived")
            .Column(entity => entity.CreatedAtUtcMs, "created_at_utc_ms")
            .Column(entity => entity.UpdatedAtUtcMs, "updated_at_utc_ms")
            .Column(entity => entity.RowVersion, "row_version");

        FluentMapper.Entity<ClassroomEntity>()
            .Table("classroom").Primary(entity => entity.Id)
            .Column(entity => entity.Id, "id")
            .Column(entity => entity.SchoolId, "school_id")
            .Column(entity => entity.Name, "name")
            .Column(entity => entity.NormalizedName, "normalized_name")
            .Column(entity => entity.SchoolYear, "school_year")
            .Column(entity => entity.IsArchivedFlag, "is_archived")
            .Column(entity => entity.CreatedAtUtcMs, "created_at_utc_ms")
            .Column(entity => entity.UpdatedAtUtcMs, "updated_at_utc_ms")
            .Column(entity => entity.RowVersion, "row_version");

        FluentMapper.Entity<CourseEntity>()
            .Table("course").Primary(entity => entity.Id)
            .Column(entity => entity.Id, "id")
            .Column(entity => entity.Title, "title")
            .Column(entity => entity.Description, "description")
            .Column(entity => entity.IsArchivedFlag, "is_archived")
            .Column(entity => entity.CreatedAtUtcMs, "created_at_utc_ms")
            .Column(entity => entity.UpdatedAtUtcMs, "updated_at_utc_ms")
            .Column(entity => entity.RowVersion, "row_version");

        FluentMapper.Entity<LessonEntity>()
            .Table("lesson").Primary(entity => entity.Id)
            .Column(entity => entity.Id, "id")
            .Column(entity => entity.CourseId, "course_id")
            .Column(entity => entity.Title, "title")
            .Column(entity => entity.Description, "description")
            .Column(entity => entity.IsArchivedFlag, "is_archived")
            .Column(entity => entity.CreatedAtUtcMs, "created_at_utc_ms")
            .Column(entity => entity.UpdatedAtUtcMs, "updated_at_utc_ms")
            .Column(entity => entity.RowVersion, "row_version");

        FluentMapper.Entity<ExerciseEntity>()
            .Table("exercise").Primary(entity => entity.Id)
            .Column(entity => entity.Id, "id")
            .Column(entity => entity.ExerciseType, "exercise_type")
            .Column(entity => entity.Title, "title")
            .Column(entity => entity.Description, "description")
            .Column(entity => entity.IsArchivedFlag, "is_archived")
            .Column(entity => entity.CreatedAtUtcMs, "created_at_utc_ms")
            .Column(entity => entity.UpdatedAtUtcMs, "updated_at_utc_ms")
            .Column(entity => entity.RowVersion, "row_version");

        FluentMapper.Entity<LessonExerciseEntity>()
            .Table("lesson_exercise").Primary(entity => entity.Id)
            .Column(entity => entity.Id, "id")
            .Column(entity => entity.LessonId, "lesson_id")
            .Column(entity => entity.ExerciseId, "exercise_id")
            .Column(entity => entity.Position, "position")
            .Column(entity => entity.CreatedAtUtcMs, "created_at_utc_ms")
            .Column(entity => entity.UpdatedAtUtcMs, "updated_at_utc_ms")
            .Column(entity => entity.RowVersion, "row_version");

        FluentMapper.Entity<CredentialSessionEntity>()
            .Table("credential_session").Primary(entity => entity.Id)
            .Column(entity => entity.Id, "id")
            .Column(entity => entity.PrincipalKind, "principal_kind")
            .Column(entity => entity.AdminUserId!, "admin_user_id")
            .Column(entity => entity.ParticipantId!, "participant_id")
            .Column(entity => entity.SecurityStamp, "security_stamp")
            .Column(entity => entity.IssuedAtUtcMs, "issued_at_utc_ms")
            .Column(entity => entity.ExpiresAtUtcMs, "expires_at_utc_ms")
            .Column(entity => entity.RevokedAtUtcMs!, "revoked_at_utc_ms")
            .Column(entity => entity.CreatedAtUtcMs, "created_at_utc_ms")
            .Column(entity => entity.UpdatedAtUtcMs, "updated_at_utc_ms")
            .Column(entity => entity.RowVersion, "row_version");

        FluentMapper.Entity<LessonSessionEntity>()
            .Table("lesson_session").Primary(entity => entity.Id)
            .Column(entity => entity.Id, "id")
            .Column(entity => entity.LessonId, "lesson_id")
            .Column(entity => entity.ClassroomId, "classroom_id")
            .Column(entity => entity.ControllerAdminId, "controller_admin_id")
            .Column(entity => entity.Status, "status")
            .Column(entity => entity.AdmissionsOpenFlag, "admissions_open")
            .Column(entity => entity.SessionRevision, "session_revision")
            .Column(entity => entity.ClassDefaultJson, "class_default_json")
            .Column(entity => entity.JoinCode!, "join_code")
            .Column(entity => entity.StartedAtUtcMs!, "started_at_utc_ms")
            .Column(entity => entity.EndedAtUtcMs!, "ended_at_utc_ms")
            .Column(entity => entity.CreatedAtUtcMs, "created_at_utc_ms")
            .Column(entity => entity.UpdatedAtUtcMs, "updated_at_utc_ms")
            .Column(entity => entity.RowVersion, "row_version");

        FluentMapper.Entity<ExerciseDraftEntity>()
            .Table("exercise_draft").Primary(entity => entity.Id)
            .Column(entity => entity.Id, "exercise_id")
            .Column(entity => entity.DefinitionJson, "definition_json")
            .Column(entity => entity.WorkspaceJson, "workspace_json")
            .Column(entity => entity.ReferenceSolutionJson!, "reference_solution_json")
            .Column(entity => entity.CreatedAtUtcMs, "created_at_utc_ms")
            .Column(entity => entity.UpdatedAtUtcMs, "updated_at_utc_ms")
            .Column(entity => entity.RowVersion, "row_version");

        // `RowVersion` non compare di proposito: la tabella è immutabile e non ha una colonna
        // `row_version`. La proprietà resta il valore costante richiesto da IPersistedEntity.
        FluentMapper.Entity<ExerciseVersionEntity>()
            .Table("exercise_version").Primary(entity => entity.Id)
            .Column(entity => entity.Id, "id")
            .Column(entity => entity.ExerciseId, "exercise_id")
            .Column(entity => entity.VersionNumber, "version_number")
            .Column(entity => entity.DefinitionJson, "definition_json")
            .Column(entity => entity.InitialWorkspaceJson, "initial_workspace_json")
            .Column(entity => entity.EngineVersion, "engine_version")
            .Column(entity => entity.SchemaVersion, "schema_version")
            .Column(entity => entity.ContentHash, "content_hash")
            .Column(entity => entity.ReferenceHash!, "reference_hash")
            .Column(entity => entity.PublishedByAdminId, "published_by_admin_id")
            .Column(entity => entity.CreatedAtUtcMs, "created_at_utc_ms");

        FluentMapper.Entity<ExerciseVersionPrivateEntity>()
            .Table("exercise_version_private").Primary(entity => entity.Id)
            .Column(entity => entity.Id, "exercise_version_id")
            .Column(entity => entity.ReferenceSolutionJson!, "reference_solution_json")
            .Column(entity => entity.AuthorNotes!, "author_notes");

        FluentMapper.Entity<StationEntity>()
            .Table("station").Primary(entity => entity.Id)
            .Column(entity => entity.Id, "id")
            .Column(entity => entity.ClassroomId!, "classroom_id")
            .Column(entity => entity.Label!, "label")
            .Column(entity => entity.BrowserKeyHash, "browser_key_hash")
            .Column(entity => entity.LastSeenAtUtcMs!, "last_seen_at_utc_ms")
            .Column(entity => entity.CreatedAtUtcMs, "created_at_utc_ms")
            .Column(entity => entity.UpdatedAtUtcMs, "updated_at_utc_ms")
            .Column(entity => entity.RowVersion, "row_version");

        // La proprietÃ  si chiama `AdmissionStatusText` perchÃ© quella dell'enum omonimo non
        // potrebbe portare lo stesso nome senza oscurarlo; la colonna resta `admission_status`.
        FluentMapper.Entity<SessionParticipantEntity>()
            .Table("session_participant").Primary(entity => entity.Id)
            .Column(entity => entity.Id, "id")
            .Column(entity => entity.SessionId, "session_id")
            .Column(entity => entity.StationId!, "station_id")
            .Column(entity => entity.DisplayName, "display_name")
            .Column(entity => entity.AdmissionStatusText, "admission_status")
            .Column(entity => entity.LeftAtUtcMs!, "left_at_utc_ms")
            .Column(entity => entity.ConnectionEpoch, "connection_epoch")
            .Column(entity => entity.JoinedAtUtcMs, "joined_at_utc_ms")
            .Column(entity => entity.ExcludedAtUtcMs!, "excluded_at_utc_ms")
            .Column(entity => entity.CreatedAtUtcMs, "created_at_utc_ms")
            .Column(entity => entity.UpdatedAtUtcMs, "updated_at_utc_ms")
            .Column(entity => entity.RowVersion, "row_version");

        // La chiave primaria dello stato Ã¨ l'identificativo del partecipante, come per la bozza Ã¨
        // l'esercizio: una riga per partecipante, e nessuna riga porta un identificativo proprio.
        FluentMapper.Entity<ParticipantStateEntity>()
            .Table("participant_state").Primary(entity => entity.Id)
            .Column(entity => entity.Id, "participant_id")
            .Column(entity => entity.SessionId, "session_id")
            .Column(entity => entity.EffectiveRevision, "effective_revision")
            .Column(entity => entity.AssignmentId!, "assignment_id")
            .Column(entity => entity.AttemptId!, "attempt_id")
            .Column(entity => entity.View, "view")
            .Column(entity => entity.InteractionEnabledFlag, "interaction_enabled")
            .Column(entity => entity.TeacherPausedFlag, "teacher_paused")
            .Column(entity => entity.RecoveryBlockedFlag, "recovery_blocked")
            .Column(entity => entity.ExecutionGeneration, "execution_generation")
            .Column(entity => entity.DirectiveJson!, "directive_json")
            .Column(entity => entity.ResumeContextJson!, "resume_context_json")
            .Column(entity => entity.CreatedAtUtcMs, "created_at_utc_ms")
            .Column(entity => entity.UpdatedAtUtcMs, "updated_at_utc_ms")
            .Column(entity => entity.RowVersion, "row_version");

        // Il registro di idempotenza si legge e si scrive per coppia: il mapping dichiara le colonne
        // e nessuna chiave singola, perchÃ© le operazioni per chiave non si usano (Â§7.5).
        FluentMapper.Entity<IdempotencyRecordEntity>()
            .Table("idempotency_record")
            .Column(entity => entity.ScopeKey, "scope_key")
            .Column(entity => entity.RequestKey, "request_key")
            .Column(entity => entity.RequestHash, "request_hash")
            .Column(entity => entity.ResponseCode, "response_code")
            .Column(entity => entity.ResponseJson, "response_json")
            .Column(entity => entity.ExpiresAtUtcMs, "expires_at_utc_ms");

        // Il comando registrato Ã¨ immutabile: `RowVersion` esiste per il contratto del repository
        // comune ma non ha una colonna, quindi non viene mappato (Â§7, Â§12.1).
        FluentMapper.Entity<SessionCommandEntity>()
            .Table("session_command").Primary(entity => entity.Id)
            .Column(entity => entity.Id, "id")
            .Column(entity => entity.SessionId, "session_id")
            .Column(entity => entity.IssuedByAdminId!, "issued_by_admin_id")
            .Column(entity => entity.CommandName, "command_name")
            .Column(entity => entity.Scope, "scope")
            .Column(entity => entity.TargetParticipantId!, "target_participant_id")
            .Column(entity => entity.Revision, "revision")
            .Column(entity => entity.PayloadJson, "payload_json")
            .Column(entity => entity.IssuedAtUtcMs, "issued_at_utc_ms");

        // La consegna ha la chiave composta â€” comando e partecipante â€” quindi il mapping dichiara le
        // colonne e nessuna chiave singola, come per il registro di idempotenza (Â§7, Â§12.2).
        FluentMapper.Entity<CommandDeliveryEntity>()
            .Table("command_delivery")
            .Column(entity => entity.CommandId, "command_id")
            .Column(entity => entity.ParticipantId, "participant_id")
            .Column(entity => entity.EffectiveRevision, "effective_revision")
            .Column(entity => entity.Status, "status")
            .Column(entity => entity.RequiresRecoveryFlag, "requires_recovery")
            .Column(entity => entity.SentAtUtcMs!, "sent_at_utc_ms")
            .Column(entity => entity.AckAtUtcMs!, "ack_at_utc_ms")
            .Column(entity => entity.AppliedAtUtcMs!, "applied_at_utc_ms")
            .Column(entity => entity.ErrorCode!, "error_code")
            .Column(entity => entity.SendAttempts, "send_attempts");

        // La riga dell'outbox non ha versione di riga: il completamento lo segna il dispatcher con un
        // aggiornamento condizionato, non con la concorrenza ottimistica (Â§7, Â§12.2).
        FluentMapper.Entity<OutboxMessageEntity>()
            .Table("outbox_message").Primary(entity => entity.Id)
            .Column(entity => entity.Id, "id")
            .Column(entity => entity.SessionId, "session_id")
            .Column(entity => entity.MessageType, "message_type")
            .Column(entity => entity.PayloadJson, "payload_json")
            .Column(entity => entity.AvailableAtUtcMs, "available_at_utc_ms")
            .Column(entity => entity.AttemptCount, "attempt_count")
            .Column(entity => entity.ProcessedAtUtcMs!, "processed_at_utc_ms")
            .Column(entity => entity.LastErrorCode!, "last_error_code");

        // L'assegnazione è immutabile: una versione diversa è una riga nuova, quindi `RowVersion`
        // esiste per il contratto del repository comune e non viene mappato (Â§7.3).
        FluentMapper.Entity<ActivityAssignmentEntity>()
            .Table("activity_assignment").Primary(entity => entity.Id)
            .Column(entity => entity.Id, "id")
            .Column(entity => entity.SessionId, "session_id")
            .Column(entity => entity.LessonExerciseId!, "lesson_exercise_id")
            .Column(entity => entity.Scope, "scope")
            .Column(entity => entity.TargetParticipantId!, "target_participant_id")
            .Column(entity => entity.ExerciseVersionId, "exercise_version_id")
            .Column(entity => entity.CreatedAtUtcMs, "created_at_utc_ms");

        // Il tentativo si modifica — il progetto cambia a ogni salvataggio — quindi porta la versione
        // di riga, oltre alla revisione del progetto che è del client (Â§7.3, Â§15.2).
        FluentMapper.Entity<AttemptFinalizationGrantEntity>()
            .Table("attempt_finalization_grant").Primary(entity => entity.Id)
            .Column(entity => entity.Id, "id")
            .Column(entity => entity.AttemptId, "attempt_id")
            .Column(entity => entity.CredentialSessionId, "credential_session_id")
            .Column(entity => entity.ConnectionEpoch, "connection_epoch")
            .Column(entity => entity.ExecutionGeneration, "execution_generation")
            .Column(entity => entity.ExpiresAtUtcMs, "expires_at_utc_ms")
            .Column(entity => entity.BaseRowVersion, "base_row_version")
            .Column(entity => entity.UsedAtUtcMs!, "used_at_utc_ms")
            .Column(entity => entity.SaveId!, "save_id")
            .Column(entity => entity.RowVersion, "row_version");

        FluentMapper.Entity<ExerciseAttemptEntity>()
            .Table("exercise_attempt").Primary(entity => entity.Id)
            .Column(entity => entity.Id, "id")
            .Column(entity => entity.SessionId, "session_id")
            .Column(entity => entity.ParticipantId, "participant_id")
            .Column(entity => entity.AssignmentId, "assignment_id")
            .Column(entity => entity.ExerciseVersionId, "exercise_version_id")
            .Column(entity => entity.AttemptNumber, "attempt_number")
            .Column(entity => entity.Status, "status")
            .Column(entity => entity.WorkspaceJson, "workspace_json")
            .Column(entity => entity.WorkspaceHash, "workspace_hash")
            .Column(entity => entity.ProjectRevision, "project_revision")
            .Column(entity => entity.CheckpointJson!, "checkpoint_json")
            .Column(entity => entity.CheckpointEngineVersion!, "checkpoint_engine_version")
            .Column(entity => entity.LastSavedAtUtcMs!, "last_saved_at_utc_ms")
            .Column(entity => entity.CompletedAtUtcMs!, "completed_at_utc_ms")
            .Column(entity => entity.CreatedAtUtcMs, "created_at_utc_ms")
            .Column(entity => entity.UpdatedAtUtcMs, "updated_at_utc_ms")
            .Column(entity => entity.RowVersion, "row_version");
    }
}
