using Qwerty.Codi.Application.Dto;
using System.Text;
using System.Text.Json;
using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Exercises;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Exercises;

namespace Qwerty.Codi.Application.Sessions;

public sealed class SaveAttemptWorkspaceHandler(AttemptSaveService saves)
    : IRequestHandler<SaveAttemptWorkspaceCommand, AttemptSaveDto>
{
    public async ValueTask<AttemptSaveDto> Handle(SaveAttemptWorkspaceCommand request, CancellationToken cancellationToken) =>
        await saves.SaveAsync(request.Context, SaveAttemptWorkspaceCommand.RequestName, request.AttemptId,
            request.SaveId, request.ConnectionEpoch, request.BaseRowVersion, request, request.ClientProjectRevision,
            request.WorkspaceJson, request.WorkspaceHash, null, null, cancellationToken).ConfigureAwait(false);
}

public sealed class SaveAttemptCheckpointHandler(AttemptSaveService saves)
    : IRequestHandler<SaveAttemptCheckpointCommand, AttemptSaveDto>
{
    public async ValueTask<AttemptSaveDto> Handle(SaveAttemptCheckpointCommand request, CancellationToken cancellationToken) =>
        await saves.SaveAsync(request.Context, SaveAttemptCheckpointCommand.RequestName, request.AttemptId,
            request.SaveId, request.ConnectionEpoch, request.BaseRowVersion, request, null,
            null, null, request.Checkpoint, null, cancellationToken).ConfigureAwait(false);
}

public sealed class FinalizeAttemptHandler(AttemptSaveService saves)
    : IRequestHandler<FinalizeAttemptCommand, AttemptSaveDto>
{
    public async ValueTask<AttemptSaveDto> Handle(FinalizeAttemptCommand request, CancellationToken cancellationToken) =>
        await saves.SaveAsync(request.Context, FinalizeAttemptCommand.RequestName, request.AttemptId,
            request.SaveId, request.ConnectionEpoch, request.BaseRowVersion, request, request.ClientProjectRevision,
            request.WorkspaceJson, request.WorkspaceHash, request.Checkpoint, request.GrantId, cancellationToken).ConfigureAwait(false);
}

/// <summary>Scrittura atomica di progetto, checkpoint, ricevuta e consumo del permesso.</summary>
public sealed class AttemptSaveService(IUnitOfWorkContext factory, ISessionCoordinator coordinator,
    IParticipantConnectionRegistry connections, TimeProvider time)
{
    public async Task<AttemptSaveDto> SaveAsync(CodiRequestContext context, string name, Guid attemptId,
        Guid saveId, long epoch, long? baseVersion, object request, long? revision,
        string? workspace, string? workspaceHash, AttemptCheckpoint? checkpoint, Guid? grantId,
        CancellationToken cancellationToken)
    {
        var participantId = context.ParticipantId ?? throw CodiRequestException.AuthenticationRequired(name);
        var credentialId = context.CredentialSessionId ?? throw CodiRequestException.AuthenticationRequired(name);
        if (attemptId == Guid.Empty || saveId == Guid.Empty || epoch < 1)
            throw CodiRequestException.InvalidPayload(name);
        if (baseVersion is null) throw CodiRequestException.VersionRequired(name);
        var participantKey = participantId.ToString("D");
        var attemptKey = attemptId.ToString("D");
        Guid sessionId;
        await using (var read = await factory.CreateAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            var own = await read.Repo<IExerciseAttemptRepository>().GetAsync(attemptKey, cancellationToken).ConfigureAwait(false);
            if (own is null || own.ParticipantId != participantKey) throw CodiRequestException.ResourceNotFound();
            sessionId = Guid.Parse(own.SessionId);
        }
        await using var coordination = await coordinator.AcquireAsync(sessionId, cancellationToken).ConfigureAwait(false);
        await using var work = await factory.CreateAsync(useTransaction: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        var nowMs = now.ToUnixTimeMilliseconds();
        var participant = await work.Repo<ISessionParticipantRepository>().GetAsync(participantKey, cancellationToken).ConfigureAwait(false);
        var credential = await work.Repo<ICredentialSessionRepository>().GetAsync(credentialId.ToString("D"), cancellationToken).ConfigureAwait(false);
        var attempt = await work.Repo<IExerciseAttemptRepository>().GetAsync(attemptKey, cancellationToken).ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();
        if (participant is null || participant.LeftAtUtcMs is not null || participant.AdmissionStatusText != "Admitted" || credential is null ||
            credential.ParticipantId != participantKey || credential.RevokedAtUtcMs is not null || credential.ExpiresAtUtcMs <= nowMs)
            throw CodiRequestException.AuthenticationRequired(name);
        if (participant.ConnectionEpoch != epoch) throw Conflict("La connessione del salvataggio è superata.");
        AttemptFinalizationGrantEntity? grant = null;
        if (grantId is { } id)
        {
            grant = await work.Repo<IAttemptFinalizationGrantRepository>().GetAsync(id.ToString("D"), cancellationToken).ConfigureAwait(false);
            if (grant is null || grant.AttemptId != attemptKey || grant.CredentialSessionId != credential.Id ||
                grant.ConnectionEpoch != epoch || grant.ExpiresAtUtcMs <= nowMs)
                throw CodiRequestException.PolicyDenied(name, CodiPolicy.FinalizingParticipant);
        }
        else
        {
            var connection = connections.Get(participantId);
            if (connection is null || connection.Epoch != epoch || !connection.SnapshotApplied ||
                connection.Presence == ParticipantPresence.NotOperational || connection.AppliedRevision != connection.RequiredRevision)
                throw Conflict("Il salvataggio richiede una connessione sincronizzata.");
        }
        var scope = $"save:{credentialId:D}:{attemptId:D}";
        var requestHash = ExerciseContentHash.ComputeWorkspace(name + ":" + JsonSerializer.Serialize(request, request.GetType(), CodiJson.Payload));
        var records = work.Repo<IIdempotencyRecordRepository>();
        var previous = await records.FindAsync(scope, saveId.ToString("D"), cancellationToken).ConfigureAwait(false);
        if (previous is not null && previous.ExpiresAtUtcMs > nowMs)
        {
            if (previous.RequestHash != requestHash) throw CodiRequestException.IdempotencyKeyConflict();
            return JsonSerializer.Deserialize<AttemptSaveDto>(previous.ResponseJson, CodiJson.Payload)!;
        }
        if (grant?.UsedAtUtcMs is not null) throw Conflict("Il permesso conclusivo ha già salvato un lavoro.");
        var state = await work.Repo<IParticipantStateRepository>().GetAsync(participantKey, cancellationToken).ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();
        var session = await work.Repo<ILessonSessionRepository>().GetAsync(sessionId.ToString("D"), cancellationToken).ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();
        if (grant is null && (session.Status == "Ended" || state.AttemptId != attemptKey || attempt.Status == "Archived"))
            throw Conflict("Il tentativo non è più quello operativo.");
        var expectedVersion = grant is not null && attempt.Status == "Archived" ? baseVersion + 1 : baseVersion;
        if (attempt.RowVersion != expectedVersion || (grant is not null && grant.BaseRowVersion != baseVersion))
            throw CodiRequestException.RowVersionMismatch();
        var version = await work.Repo<IExerciseVersionRepository>().GetAsync(attempt.ExerciseVersionId, cancellationToken).ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();
        if (workspace is not null)
        {
            if (Encoding.UTF8.GetByteCount(workspace) > ExerciseWorkspaceJson.MaxLength)
                throw new CodiRequestException(CodiErrorCodes.PayloadTooLarge, "Il progetto supera il limite ammesso.");
            // Il lavoro dell'alunno si verifica secondo la versione del motore che lo governa: il
            // progetto a blocchi di un percorso, la risposta di un esercizio dichiarativo. Per il
            // tipo dichiarativo serve la soluzione privata, che non esce dal server (§13.1).
            var privateRow = await work.Repo<IExerciseVersionPrivateRepository>()
                .GetAsync(version.Id, cancellationToken)
                .ConfigureAwait(false);
            ExerciseWorkspaceValidator.Validate(
                workspace,
                ExerciseDefinitionJson.Parse(version.DefinitionJson, name),
                name,
                "il progetto",
                privateRow?.ReferenceSolutionJson);
            var hash = ExerciseContentHash.ComputeWorkspace(workspace);
            if (hash != workspaceHash) throw Invalid("L'impronta non corrisponde al progetto.");
            if (revision is null || revision < attempt.ProjectRevision || revision < 0)
                throw Conflict("La revisione del progetto è superata.");
            if (revision == attempt.ProjectRevision)
            {
                if (grant is null || hash != attempt.WorkspaceHash)
                    throw Conflict("La revisione del progetto deve avanzare.");
            }
            else
            {
                var domain = attempt.ToDomain();
                domain.SaveWorkspace(workspace, hash, revision.Value, now, finalizing: grant is not null);
                attempt.WorkspaceJson = domain.WorkspaceJson;
                attempt.WorkspaceHash = domain.WorkspaceHash;
                attempt.ProjectRevision = domain.ProjectRevision;
                attempt.CheckpointJson = null;
                attempt.CheckpointEngineVersion = null;
            }
        }
        if (checkpoint is not null)
        {
            ValidateCheckpoint(checkpoint, attempt, version, grant?.ExecutionGeneration ?? state.ExecutionGeneration, name);
            attempt.CheckpointJson = JsonSerializer.Serialize(checkpoint, CodiJson.Payload);
            attempt.CheckpointEngineVersion = checkpoint.EngineVersion;
        }
        else if (workspace is null) throw CodiRequestException.InvalidPayload(name);
        attempt.LastSavedAtUtcMs = nowMs;
        attempt.UpdatedAtUtcMs = nowMs;
        if (!await work.Repo<IExerciseAttemptRepository>().UpdateAsync(attempt, cancellationToken).ConfigureAwait(false))
            throw CodiRequestException.RowVersionMismatch();
        if (grant is not null)
        {
            grant.UsedAtUtcMs = nowMs;
            grant.SaveId = saveId.ToString("D");
            if (!await work.Repo<IAttemptFinalizationGrantRepository>().UpdateAsync(grant, cancellationToken).ConfigureAwait(false))
                throw CodiRequestException.RowVersionMismatch();
        }
        var response = new AttemptSaveDto(attemptId, saveId, attempt.RowVersion, attempt.ProjectRevision,
            attempt.WorkspaceHash, now, attempt.CheckpointJson is not null);
        await records.ReplaceAsync(new IdempotencyRecordEntity
        {
            ScopeKey = scope,
            RequestKey = saveId.ToString("D"),
            RequestHash = requestHash,
            ResponseJson = JsonSerializer.Serialize(response, CodiJson.Payload),
            ResponseCode = 200,
            ExpiresAtUtcMs = now.AddHours(24).ToUnixTimeMilliseconds(),
        }, cancellationToken).ConfigureAwait(false);
        await work.CommitAsync(cancellationToken).ConfigureAwait(false);
        return response;
    }

    private static void ValidateCheckpoint(AttemptCheckpoint checkpoint, ExerciseAttemptEntity attempt,
        ExerciseVersionEntity version, long generation, string name)
    {
        // La versione dello schema del checkpoint è quella dichiarata dal motore dell'esercizio:
        // il server non fissa un numero valido per tutti i motori, altrimenti il secondo motore
        // dovrebbe collidere con il primo o rinumerarsi per forza.
        var checkpointSchema = ExerciseEngineRegistry
            .Find(version.EngineVersion)?.CheckpointSchemaVersion;

        if (checkpointSchema is null || checkpoint.SchemaVersion != checkpointSchema.Value
            || checkpoint.AttemptId.ToString("D") != attempt.Id ||
            checkpoint.ExerciseVersionId.ToString("D") != attempt.ExerciseVersionId || checkpoint.ExecutionGeneration != generation ||
            checkpoint.WorkspaceHash != attempt.WorkspaceHash || checkpoint.ProjectRevision != attempt.ProjectRevision ||
            checkpoint.RunId == Guid.Empty || checkpoint.RuntimeStatus is not ("Running" or "Paused" or "Completed") ||
            (checkpoint.EngineVersion != version.EngineVersion && checkpoint.EngineVersion != "regia-demo-1") ||
            checkpoint.RuntimeState.ValueKind != JsonValueKind.Object)
            throw Invalid("Il checkpoint non corrisponde al progetto, al motore o alla generazione.");
        if (Encoding.UTF8.GetByteCount(checkpoint.RuntimeState.GetRawText()) > 65536)
            throw new CodiRequestException(CodiErrorCodes.PayloadTooLarge, "Il checkpoint supera il limite ammesso.");
        if (checkpoint.EngineVersion == "regia-demo-1" &&
            (!checkpoint.RuntimeState.TryGetProperty("progress", out var progress) || progress.ValueKind != JsonValueKind.Number || !progress.TryGetInt32(out var value) || value < 0 || value > 40))
            throw CodiRequestException.InvalidPayload(name);

        // Il checkpoint di un esercizio che conserva valori porta l'ambiente delle variabili, l'uscita
        // e il percorso di esecuzione. Il numero di schema è lo stesso del percorso, quindi non basta a
        // dire che il formato è quello atteso: senza questa verifica uno stato incoerente verrebbe
        // accettato e la ripresa riprenderebbe da un punto che non esiste.
        if (checkpoint.EngineVersion == ValueProgramEngine.Version)
            ValidateValueProgramState(checkpoint, name);
    }

    /// <summary>
    /// Verifica la coerenza dello stato di una corsa che conserva valori: uno stato di quelli previsti,
    /// i fotogrammi con la loro posizione, le variabili intere e i valori da consumare interi.
    ///
    /// Un progetto ripreso da uno stato incoerente riprende **altrove**: è il motivo per cui questa
    /// verifica sta qui e non si limita a controllare che il campo esista.
    /// </summary>
    private static void ValidateValueProgramState(AttemptCheckpoint checkpoint, string name)
    {
        var state = checkpoint.RuntimeState;

        if (!state.TryGetProperty("status", out var status)
            || status.ValueKind != JsonValueKind.String
            || status.GetString() is not ("Ready" or "WaitingForValue" or "Finished" or "Refused" or "BudgetExhausted"))
            throw CodiRequestException.InvalidPayload(name);

        if (!state.TryGetProperty("frames", out var frames) || frames.ValueKind != JsonValueKind.Array
            || frames.GetArrayLength() == 0)
            throw CodiRequestException.InvalidPayload(name);

        foreach (var frame in frames.EnumerateArray())
        {
            if (frame.ValueKind != JsonValueKind.Object
                || !frame.TryGetProperty("blockId", out var blockId) || blockId.ValueKind != JsonValueKind.String
                || !frame.TryGetProperty("index", out var index) || index.ValueKind != JsonValueKind.Number
                || !index.TryGetInt32(out var position) || position < 0)
                throw CodiRequestException.InvalidPayload(name);
        }

        if (!state.TryGetProperty("variables", out var variables) || variables.ValueKind != JsonValueKind.Object)
            throw CodiRequestException.InvalidPayload(name);

        foreach (var variable in variables.EnumerateObject())
        {
            if (variable.Value.ValueKind != JsonValueKind.Number || !variable.Value.TryGetInt32(out _))
                throw CodiRequestException.InvalidPayload(name);
        }

        if (!state.TryGetProperty("pendingValues", out var pending) || pending.ValueKind != JsonValueKind.Array)
            throw CodiRequestException.InvalidPayload(name);

        foreach (var value in pending.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out _))
                throw CodiRequestException.InvalidPayload(name);
        }
    }

    private static CodiRequestException Conflict(string message) => new(CodiErrorCodes.SessionStateConflict, message);
    private static CodiRequestException Invalid(string message) => new(CodiErrorCodes.ValidationFailed, message);
}
