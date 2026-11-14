using Qwerty.Codi.Application.Dto;
using System.Text.Json;
using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Exercises;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Credentials;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Ingresso idempotente di un alunno nella sessione (§9.4): verifica la catena scelta, crea
/// partecipazione, stato iniziale e credenziale in **una sola transazione**, registra l'esito e
/// restituisce ciò che serve al trasporto per emettere il cookie (§9.2).
///
/// L'ordine dei controlli è deliberato: prima il registro di idempotenza, poi lo stato della
/// sessione. Una risposta persa e ripetuta deve recuperare la partecipazione **anche se** nel
/// frattempo gli ingressi si sono chiusi: la partecipazione esiste già, e negarla direbbe all'alunno
/// che non è entrato mentre è dentro. La riapertura del registro vale finché la credenziale emessa
/// è utilizzabile; se non lo è più, il recupero non è possibile e la richiesta lo dichiara.
///
/// Un ingresso non è una risorsa indirizzabile: il client non ha un URL del partecipante, che resta
/// leggibile dal suo contesto con <c>GetParticipantContext</c>. Per questo la risposta è <c>200</c>
/// e non <c>201</c> con <c>Location</c> (§10.2).
/// </summary>
public sealed class JoinSessionHandler(
    IUnitOfWorkContext unitOfWorkContext,
    TimeProvider timeProvider,
    ISessionCoordinator coordinator,
    IJoinCodeAttemptRegistry? attempts = null) : IRequestHandler<JoinSessionCommand, JoinSessionOutcome>
{
    /// <summary>Lunghezza ammessa della chiave della postazione: identificativa, non un nome.</summary>
    private const int MinBrowserKeyLength = 8;

    private const int MaxBrowserKeyLength = 200;

    public async ValueTask<JoinSessionOutcome> Handle(
        JoinSessionCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var admissionScope = request.Context.AdmissionScope;
        if (string.IsNullOrWhiteSpace(admissionScope))
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                "Manca il cookie di ammissione: ricaricare la pagina di ingresso prima di entrare.");
        }

        var idempotencyKey = request.Context.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                "Manca la chiave di idempotenza della richiesta.");
        }

        if (!ParticipantName.IsUsable(request.DisplayName))
        {
            throw new CodiRequestException(
                CodiErrorCodes.InvalidName,
                $"Il nome deve avere da 1 a {ParticipantName.MaxGraphemes} caratteri e nessun carattere di controllo.");
        }

        // Le due forme dell'ingresso (§9.4): il codice detto a voce oppure la catena scelta. Ne va
        // dichiarata esattamente una: accettarne due lascerebbe al server la scelta di quale credere,
        // e nessuna delle due non è un ingresso.
        var joinCode = request.JoinCode?.Trim();
        var hasCode = !string.IsNullOrEmpty(joinCode);
        var declaredChain = request.SchoolId is not null || request.ClassroomId is not null || request.SessionId is not null;

        if (hasCode == declaredChain)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                "L'ingresso dichiara il codice di ingresso oppure scuola, classe e sessione: esattamente una delle due forme.");
        }

        if (declaredChain && (request.SchoolId is null || request.ClassroomId is null || request.SessionId is null))
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                "La catena dell'ingresso dichiara scuola, classe e sessione: una sola non basta.");
        }

        // Un codice malformato non è un codice: non si cerca nel database e non rivela nulla.
        if (hasCode && !SessionJoinCode.IsWellFormed(joinCode))
        {
            throw CodiRequestException.InvalidJoinCode();
        }

        var browserKey = NormalizeBrowserKey(request.BrowserKey);
        var displayName = ParticipantName.Trim(request.DisplayName);
        var now = timeProvider.GetUtcNow();
        var nowMs = now.ToUnixTimeMilliseconds();
        var key = idempotencyKey.Trim();

        // Il conteggio dei tentativi vale per la forma con codice: la catena non è un segreto da
        // indovinare. Il blocco si controlla prima di cercare, così un browser fermo non consuma lavoro.
        var attemptScope = new JoinCodeAttemptScope(
            JoinIdempotency.AttemptKey(admissionScope),
            Fingerprint(request.Context.RemoteAddress));
        if (hasCode && attempts is not null && attempts.IsBlocked(attemptScope, now))
        {
            throw CodiRequestException.TooManyJoinAttempts();
        }

        var scopeKey = JoinIdempotency.ScopeKey(admissionScope);
        var requestHash = hasCode
            ? JoinIdempotency.RequestHashByJoinCode(joinCode!, displayName, browserKey)
            : JoinIdempotency.RequestHash(
                request.SchoolId!.Value, request.ClassroomId!.Value, request.SessionId!.Value, displayName, browserKey);

        // Il codice identifica la sessione, e il coordinatore si acquisisce per sessione: la
        // risoluzione viene prima. La ricerca **non** filtra lo stato della sessione, perché un
        // ingresso già registrato si recupera dal registro di idempotenza anche se la lezione si è
        // chiusa nel frattempo: è la stessa garanzia della forma a catena. Un codice che non esiste
        // è il tentativo andato a vuoto per eccellenza, e alimenta il conteggio.
        Guid sessionId;
        if (hasCode)
        {
            try
            {
                sessionId = await ResolveSessionIdByJoinCodeAsync(joinCode!, cancellationToken).ConfigureAwait(false);
            }
            catch (CodiRequestException)
            {
                attempts?.RegisterFailure(attemptScope, now);

                throw;
            }
        }
        else
        {
            sessionId = request.SessionId!.Value;
        }

        await using var coordination = await coordinator.AcquireAsync(sessionId, cancellationToken).ConfigureAwait(false);
        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var idempotency = work.Repo<IIdempotencyRecordRepository>();
        var stored = await idempotency
            .FindAsync(scopeKey, key, cancellationToken)
            .ConfigureAwait(false);

        if (stored is not null && stored.ExpiresAtUtcMs > nowMs)
        {
            var replay = await ReplayAsync(
                work.Repo<ICredentialSessionRepository>(),
                stored,
                requestHash,
                now,
                cancellationToken).ConfigureAwait(false);

            attempts?.RegisterSuccess(attemptScope, now);

            return replay;
        }

        var sessions = work.Repo<ILessonSessionRepository>();
        var sessionRow = await sessions
            .GetAsync(sessionId.ToString("D"), cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var session = sessionRow.ToDomain();

        try
        {
            EnsureSessionAcceptsEntries(session);
        }
        catch (CodiRequestException) when (hasCode)
        {
            // Un codice valido su una lezione non più in play è un tentativo andato a vuoto come un
            // codice inventato: anche questo alimenta il conteggio.
            attempts?.RegisterFailure(attemptScope, now);

            throw;
        }

        await EnsureChainAsync(
            work, session, request.SchoolId, request.ClassroomId, cancellationToken).ConfigureAwait(false);

        var participants = work.Repo<ISessionParticipantRepository>();
        var participant = SessionParticipant.Create(
            session.Id,
            displayName,
            await ResolveStationAsync(work, browserKey, session, now, cancellationToken).ConfigureAwait(false),
            now);

        await participants
            .InsertAsync(SessionParticipantEntity.FromDomain(participant), cancellationToken)
            .ConfigureAwait(false);

        // Lo stato iniziale è la copia del default di classe alla revisione corrente (§6.2, §6.5):
        // da qui in avanti lo modificano i comandi di regia, non l'ingresso.
        var initial = ClassActivityState.Read(session.ClassDefaultJson);
        var state = new ParticipantStateEntity
        {
            Id = participant.Id.ToString("D"),
            SessionId = session.Id.ToString("D"),
            EffectiveRevision = session.SessionRevision,
            View = initial.View,
            InteractionEnabledFlag = initial.InteractionEnabled ? 1 : 0,
            TeacherPausedFlag = initial.TeacherPaused || session.Status == SessionStatus.Paused ? 1 : 0,
            RecoveryBlockedFlag = 0,
            ExecutionGeneration = 0,
            CreatedAtUtcMs = nowMs,
            UpdatedAtUtcMs = nowMs,
            RowVersion = 1,
        };
        if (initial.AssignmentId is { } assignmentId)
        {
            var assignment = await work.Repo<IActivityAssignmentRepository>().GetAsync(assignmentId.ToString("D"), cancellationToken).ConfigureAwait(false)
                ?? throw CodiRequestException.ResourceNotFound();
            var version = await work.Repo<IExerciseVersionRepository>().GetAsync(assignment.ExerciseVersionId, cancellationToken).ConfigureAwait(false)
                ?? throw CodiRequestException.ResourceNotFound();
            var attempt = ExerciseAttempt.Create(session.Id, participant.Id, assignmentId, Guid.Parse(version.Id), 1,
                version.InitialWorkspaceJson, ExerciseContentHash.ComputeWorkspace(version.InitialWorkspaceJson), now);
            await work.Repo<IExerciseAttemptRepository>().InsertAsync(ExerciseAttemptEntity.FromDomain(attempt), cancellationToken).ConfigureAwait(false);
            state.AssignmentId = assignment.Id; state.AttemptId = attempt.Id.ToString("D");
        }
        if (ClassActivityState.Suspended(session.ClassDefaultJson) is { } suspended)
            state.ResumeContextJson = JsonSerializer.Serialize(suspended with
            {
                AssignmentId = initial.AssignmentId,
                AttemptId = Guid.TryParse(state.AttemptId, out var attemptId) ? attemptId : null,
                ExecutionDirective = null,
                ExecutionGeneration = 0
            }, CodiJson.Payload);
        await work.Repo<IParticipantStateRepository>().InsertAsync(state, cancellationToken).ConfigureAwait(false);

        var credential = CredentialSession.IssueForParticipant(
            participant.Id, now, ParticipantCredentialPolicy.SessionLifetime);

        await work.Repo<ICredentialSessionRepository>()
            .InsertAsync(CredentialSessionEntity.FromDomain(credential), cancellationToken)
            .ConfigureAwait(false);

        var joined = new ParticipantJoinedDto(
            participant.Id, session.Id, participant.DisplayName, initial.View, credential.Id);

        await idempotency
            .ReplaceAsync(
                new IdempotencyRecordEntity
                {
                    ScopeKey = scopeKey,
                    RequestKey = key,
                    RequestHash = requestHash,
                    ResponseCode = 200,
                    ResponseJson = JsonSerializer.Serialize(joined, CodiJson.Payload),
                    // La chiave resta utilizzabile finché la credenziale emessa lo è: dopo, un
                    // ingresso ripetuto è un ingresso nuovo.
                    ExpiresAtUtcMs = credential.ExpiresAtUtc.ToUnixTimeMilliseconds(),
                },
                cancellationToken)
            .ConfigureAwait(false);

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);

        attempts?.RegisterSuccess(attemptScope, now);

        return new JoinSessionOutcome(joined, credential.SecurityStamp);
    }

    /// <summary>
    /// Sessione identificata dal codice di ingresso (§9.4). La ricerca **non** filtra lo stato: chi
    /// trova un codice trova la sessione, e il rifiuto di entrarci — lezione non iniziata, terminata
    /// o ingressi chiusi — arriva dopo, dove arriva anche per la forma a catena, senza togliere la
    /// possibilità di recuperare un ingresso già registrato. Solo un codice che non esiste non trova
    /// nulla, ed è l'unico caso in cui il codice è dichiarato non valido.
    ///
    /// La lettura è fuori dalla transazione del comando perché il coordinatore si acquisisce per
    /// sessione e qui la sessione non è ancora nota: non muta nulla e non sostituisce le verifiche
    /// che il comando ripete dentro la propria transazione.
    /// </summary>
    private async Task<Guid> ResolveSessionIdByJoinCodeAsync(string joinCode, CancellationToken cancellationToken)
    {
        var value = joinCode.Trim();
        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var row = (await work.Repo<ILessonSessionRepository>()
                .QueryAsync(session => session.JoinCode == value, cancellationToken)
                .ConfigureAwait(false))
            .SingleOrDefault();

        return row is null ? throw CodiRequestException.InvalidJoinCode() : Guid.Parse(row.Id);
    }

    /// <summary>Impronta di un dato del trasporto, oppure nulla se il trasporto non lo dichiara.</summary>
    private static string? Fingerprint(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : JoinIdempotency.AttemptKey(value);

    /// <summary>
    /// Recupera l'esito registrato: la stessa chiave con lo stesso contenuto restituisce la stessa
    /// partecipazione, quella con un contenuto diverso è un conflitto (§9.4).
    /// </summary>
    private static async Task<JoinSessionOutcome> ReplayAsync(
        ICredentialSessionRepository credentials,
        IdempotencyRecordEntity stored,
        string requestHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(stored.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw CodiRequestException.IdempotencyKeyConflict();
        }

        var joined = JsonSerializer.Deserialize<ParticipantJoinedDto>(stored.ResponseJson, CodiJson.Payload)
            ?? throw new InvalidOperationException(
                "Il registro di idempotenza contiene un esito che non è un ingresso registrato.");

        var credentialRow = await credentials
            .GetAsync(joined.CredentialSessionId.ToString("D"), cancellationToken)
            .ConfigureAwait(false);
        var credential = credentialRow?.ToDomain();

        if (credential is null || !credential.IsUsableAt(now, credential.SecurityStamp))
        {
            // La partecipazione esiste ma la sua credenziale non è più utilizzabile — revocata o
            // scaduta: ripetere l'ingresso non la riapre, perché la revoca è una decisione e non una
            // risposta persa.
            throw new CodiRequestException(
                CodiErrorCodes.SessionStateConflict,
                "L'ingresso è già stato registrato ma la sua credenziale non è più utilizzabile: chiedi all’insegnante.");
        }

        return new JoinSessionOutcome(joined, credential.SecurityStamp);
    }

    /// <summary>
    /// La sessione deve accettare ingressi: avviata — in corso o in pausa, perché la pausa sospende
    /// l'attività e non l'ingresso (§6.1) — e con gli ingressi aperti. Una sessione non ancora
    /// avviata è un errore di stato, una con gli ingressi chiusi un rifiuto dichiarato dalla sessione.
    /// </summary>
    private static void EnsureSessionAcceptsEntries(LessonSession session)
    {
        if (session.Status is SessionStatus.Lobby or SessionStatus.Ended)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionStateConflict,
                session.Status == SessionStatus.Lobby
                    ? "La lezione non è ancora iniziata."
                    : "La lezione è terminata.");
        }

        if (!session.AdmissionsOpen)
        {
            throw CodiRequestException.AdmissionsClosed();
        }
    }

    /// <summary>
    /// Verifica la catena scelta dal client (§9.4): la classe e la scuola richieste sono quelle
    /// della sessione, e nulla della catena è archiviato. Un disallineamento è indistinguibile da
    /// una risorsa inesistente: la selezione del client non deve rivelare quali identificativi sono
    /// validi.
    ///
    /// L'ingresso con codice non dichiara la catena, perché non la conosce: la classe è quella della
    /// sessione e la scuola è quella della classe. Le due forme passano di qui e ricevono le stesse
    /// verifiche — classe e scuola archiviate, lezione e corso presenti — cambia solo da dove
    /// arrivano gli identificativi.
    /// </summary>
    private static async Task EnsureChainAsync(
        IUnitOfWork work,
        LessonSession session,
        Guid? declaredSchoolId,
        Guid? declaredClassroomId,
        CancellationToken cancellationToken)
    {
        if (declaredClassroomId is { } classroomId && session.ClassroomId != classroomId)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var classroomRow = await work.Repo<IClassroomRepository>()
            .GetAsync(session.ClassroomId.ToString("D"), cancellationToken)
            .ConfigureAwait(false);
        if (classroomRow is null ||
            (declaredSchoolId is { } schoolId && classroomRow.SchoolId != schoolId.ToString("D")))
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var classroom = classroomRow.ToDomain();
        if (classroom.IsArchived)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var school = (await work.Repo<ISchoolRepository>()
                .GetAsync(classroomRow.SchoolId, cancellationToken)
                .ConfigureAwait(false))?.ToDomain();
        if (school is null || school.IsArchived)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var lesson = (await work.Repo<ILessonRepository>()
                .GetAsync(session.LessonId.ToString("D"), cancellationToken)
                .ConfigureAwait(false))?.ToDomain();
        if (lesson is null || lesson.IsArchived)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var course = (await work.Repo<ICourseRepository>()
                .GetAsync(lesson.CourseId.ToString("D"), cancellationToken)
                .ConfigureAwait(false))?.ToDomain();
        if (course is null || course.IsArchived)
        {
            throw CodiRequestException.ResourceNotFound();
        }
    }

    /// <summary>
    /// Risolve la postazione da cui entra l'alunno (§7.2): la chiave casuale del browser diventa
    /// l'impronta che identifica quella postazione, e la prima volta che si vede il PC se ne crea la
    /// riga, con la classe in cui è stata vista. L'ultimo contatto è diagnostico: se l'aggiornamento
    /// incontra una modifica concorrente non fa fallire l'ingresso.
    /// </summary>
    private static async Task<Guid?> ResolveStationAsync(
        IUnitOfWork work,
        string? browserKey,
        LessonSession session,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (browserKey is null)
        {
            return null;
        }

        var hash = JoinIdempotency.StationKey(browserKey);
        var stations = work.Repo<IStationRepository>();
        var existing = (await stations
                .QueryAsync(row => row.BrowserKeyHash == hash, cancellationToken)
                .ConfigureAwait(false))
            .SingleOrDefault();

        var nowMs = now.ToUnixTimeMilliseconds();

        if (existing is not null)
        {
            existing.LastSeenAtUtcMs = nowMs;
            existing.UpdatedAtUtcMs = nowMs;
            await stations.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);

            return Guid.Parse(existing.Id);
        }

        var station = new StationEntity
        {
            Id = Guid.NewGuid().ToString("D"),
            ClassroomId = session.ClassroomId.ToString("D"),
            BrowserKeyHash = hash,
            LastSeenAtUtcMs = nowMs,
            CreatedAtUtcMs = nowMs,
            UpdatedAtUtcMs = nowMs,
            RowVersion = 1,
        };

        await stations.InsertAsync(station, cancellationToken).ConfigureAwait(false);

        return Guid.Parse(station.Id);
    }

    /// <summary>
    /// La chiave della postazione è facoltativa: assente significa «postazione non nota». Se però
    /// c'è, deve essere una chiave casuale e non un testo qualunque, perché è ciò che identifica il
    /// PC nella classe.
    /// </summary>
    private static string? NormalizeBrowserKey(string? browserKey)
    {
        if (string.IsNullOrWhiteSpace(browserKey))
        {
            return null;
        }

        var trimmed = browserKey.Trim();
        if (trimmed.Length is < MinBrowserKeyLength or > MaxBrowserKeyLength || trimmed.Any(char.IsControl))
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                $"La chiave della postazione deve avere da {MinBrowserKeyLength} a {MaxBrowserKeyLength} caratteri senza caratteri di controllo.");
        }

        return trimmed;
    }
}

/// <summary>
/// Uscita volontaria dell'alunno dalla sessione (§9.2, §10.3): registra l'uscita e revoca le
/// credenziali senza cancellare i lavori. L'identità arriva dal contesto verificato dal
/// trasporto, quindi il comando non può far uscire un partecipante diverso da chi lo invoca.
///
/// Lettura e scrittura stanno nella stessa transazione, così la revoca non ha finestre fra il
/// controllo della credenziale e l'aggiornamento. Un secondo invio con la stessa credenziale non
/// arriva qui: la credenziale revocata non concede più <c>AdmittedParticipant</c> (§9.2), quindi il
/// trasporto risponde <c>401</c> prima di eseguire il caso d'uso.
/// </summary>
public sealed class LeaveSessionHandler(
    IUnitOfWorkContext unitOfWorkContext,
    TimeProvider timeProvider,
    IParticipantMessageStore messages,
    ISessionCoordinator sessionCoordinator,
    IParticipantConnectionRegistry connections) : IRequestHandler<LeaveSessionCommand, bool>
{
    public async ValueTask<bool> Handle(LeaveSessionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.CredentialSessionId is not { } credentialSessionId ||
            request.Context.ParticipantId is not { } participantId)
        {
            throw CodiRequestException.AuthenticationRequired(LeaveSessionCommand.RequestName);
        }

        Guid sessionId;
        await using (var lookup = await unitOfWorkContext.CreateAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            var participantRow = await lookup.Repo<ISessionParticipantRepository>()
                .GetAsync(participantId.ToString("D"), cancellationToken).ConfigureAwait(false)
                ?? throw CodiRequestException.AuthenticationRequired(LeaveSessionCommand.RequestName);
            sessionId = Guid.Parse(participantRow.SessionId);
        }
        await using var coordination = await sessionCoordinator.AcquireAsync(sessionId, cancellationToken).ConfigureAwait(false);
        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var credentials = work.Repo<ICredentialSessionRepository>();
        var stored = await credentials
            .GetAsync(credentialSessionId.ToString("D"), cancellationToken)
            .ConfigureAwait(false);
        var credential = stored?.ToDomain();

        // La credenziale è già stata verificata dal trasporto: una riga mancante, di tipo diverso o
        // di un altro partecipante è un contesto incoerente, non una richiesta legittima.
        if (credential is null ||
            credential.PrincipalKind != CredentialPrincipalKind.Participant ||
            credential.ParticipantId != participantId)
        {
            throw CodiRequestException.AuthenticationRequired(LeaveSessionCommand.RequestName);
        }

        // Ripetere l'uscita non cambia nulla: la revoca è già registrata e non c'è lavoro da toccare.
        if (!credential.Revoke(timeProvider.GetUtcNow()))
        {
            return false;
        }

        if (await credentials.TryRevokeAsync(credential, cancellationToken).ConfigureAwait(false) is null)
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        var participantKey = participantId.ToString("D");
        var participants = work.Repo<ISessionParticipantRepository>();
        var storedParticipant = await participants.GetAsync(participantKey, cancellationToken).ConfigureAwait(false)
            ?? throw CodiRequestException.AuthenticationRequired(LeaveSessionCommand.RequestName);
        var participant = storedParticipant.ToDomain();
        participant.Leave(timeProvider.GetUtcNow());
        if (!await participants.UpdateAsync(SessionParticipantEntity.FromDomain(participant), cancellationToken).ConfigureAwait(false))
            throw CodiRequestException.RowVersionMismatch();

        var otherCredentials = await credentials.QueryAsync(row => row.ParticipantId == participantKey, cancellationToken).ConfigureAwait(false);
        foreach (var row in otherCredentials.Where(row => row.Id != credentialSessionId.ToString("D")))
        {
            var other = row.ToDomain();
            if (other.Revoke(timeProvider.GetUtcNow()) &&
                await credentials.TryRevokeAsync(other, cancellationToken).ConfigureAwait(false) is null)
                throw CodiRequestException.RowVersionMismatch();
        }

        var state = await work.Repo<IParticipantStateRepository>().GetAsync(participantKey, cancellationToken).ConfigureAwait(false);
        if (state is not null)
        {
            state.InteractionEnabledFlag = 0;
            state.UpdatedAtUtcMs = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            await SessionCommandPreamble.UpdateStateAsync(work, state, cancellationToken).ConfigureAwait(false);
        }

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);

        // §15.1: chi esce non è più un destinatario della chat, quindi il suo thread non resta nel
        // processo. Una ripetizione dell'uscita non arriva fin qui, perché la revoca è già registrata.
        messages.Remove(participantId);
        if (connections.Get(participantId) is { } connection)
            connections.Release(participantId, connection.ConnectionId, connection.Epoch);

        return true;
    }
}

/// <summary>
/// Corregge nome e postazione di un partecipante (§10.4, §9.5): verifica la proprietà della
/// sessione, l'appartenenza del partecipante e la versione attesa della riga, poi applica la
/// correzione. Lettura e scrittura stanno nella stessa transazione, così il controllo di versione
/// non ha finestre.
///
/// La versione attesa è quella della riga del partecipante, non della sessione: questa è una
/// correzione di una riga esistente (§10.2) e non cambia lo stato della sessione. Una postazione
/// dichiarata deve esistere nella classe della sessione: associare un PC di un'altra classe sarebbe
/// un riferimento incoerente, e il rifiuto è un 404 come per una risorsa inesistente.
/// </summary>
public sealed class UpdateParticipantHandler(
    IUnitOfWorkContext unitOfWorkContext,
    ISessionCoordinator sessionCoordinator,
    TimeProvider timeProvider) : IRequestHandler<UpdateParticipantCommand, SessionParticipantDto>
{
    public async ValueTask<SessionParticipantDto> Handle(
        UpdateParticipantCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } controllerAdminId)
        {
            throw CodiRequestException.AuthenticationRequired(UpdateParticipantCommand.RequestName);
        }

        if (request.BaseRowVersion is not { } baseRowVersion)
        {
            throw CodiRequestException.VersionRequired(UpdateParticipantCommand.RequestName);
        }

        if (!ParticipantName.IsUsable(request.DisplayName))
        {
            throw new CodiRequestException(
                CodiErrorCodes.InvalidName,
                $"Il nome deve avere da 1 a {ParticipantName.MaxGraphemes} caratteri e nessun carattere di controllo.");
        }

        await using var work = await SessionCommandOperation.OpenAsync(
            unitOfWorkContext, sessionCoordinator, timeProvider, request.SessionId,
            request.Context, UpdateParticipantCommand.RequestName, request, cancellationToken).ConfigureAwait(false);
        if (work.TryReplay<SessionParticipantDto>(out var replay)) return replay;

        var session = await SessionParticipantRegiaRules
            .LoadControlledSessionAsync(work, request.SessionId, controllerAdminId, cancellationToken)
            .ConfigureAwait(false);

        var participant = await SessionParticipantRegiaRules
            .LoadParticipantAsync(work, request.SessionId, request.ParticipantId, cancellationToken)
            .ConfigureAwait(false);

        if (participant.RowVersion != baseRowVersion)
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        if (request.StationId is { } stationId)
        {
            await SessionParticipantRegiaRules
                .EnsureStationAsync(work, stationId, session, cancellationToken)
                .ConfigureAwait(false);
        }

        participant.Correct(request.DisplayName, request.StationId, timeProvider.GetUtcNow());

        var row = SessionParticipantEntity.FromDomain(participant);
        if (!await work.Repo<ISessionParticipantRepository>()
                .UpdateAsync(row, cancellationToken)
                .ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        return await SessionCommandOperation.CompleteAsync(work,
            SessionParticipantDto.FromDomain(participant.WithRowVersion(row.RowVersion)), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Esclude un partecipante dalla sessione (§10.4): verifica proprietà, appartenenza e versione,
/// poi porta l'ammissione a <c>Excluded</c>. La riga non si cancella, così il lavoro già svolto
/// resta, e il contesto del partecipante smette di concedere <c>AdmittedParticipant</c> alla
/// richiesta successiva (§9.2), anche senza chiudere il trasporto.
///
/// Escludere chi è già escluso restituisce lo stato corrente senza scrivere: come l'archiviazione
/// ripetuta, non è un errore e non alza la versione.
/// </summary>
public sealed class ExcludeParticipantHandler(
    IUnitOfWorkContext unitOfWorkContext,
    ISessionCoordinator sessionCoordinator,
    TimeProvider timeProvider,
    IParticipantMessageStore messages) : IRequestHandler<ExcludeParticipantCommand, SessionParticipantDto>
{
    public async ValueTask<SessionParticipantDto> Handle(
        ExcludeParticipantCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } controllerAdminId)
        {
            throw CodiRequestException.AuthenticationRequired(ExcludeParticipantCommand.RequestName);
        }

        if (request.BaseRowVersion is not { } baseRowVersion)
        {
            throw CodiRequestException.VersionRequired(ExcludeParticipantCommand.RequestName);
        }

        await using var work = await SessionCommandOperation.OpenAsync(
            unitOfWorkContext, sessionCoordinator, timeProvider, request.SessionId,
            request.Context, ExcludeParticipantCommand.RequestName, request, cancellationToken).ConfigureAwait(false);
        if (work.TryReplay<SessionParticipantDto>(out var replay)) return replay;

        await SessionParticipantRegiaRules
            .LoadControlledSessionAsync(work, request.SessionId, controllerAdminId, cancellationToken)
            .ConfigureAwait(false);

        var participant = await SessionParticipantRegiaRules
            .LoadParticipantAsync(work, request.SessionId, request.ParticipantId, cancellationToken)
            .ConfigureAwait(false);

        if (participant.RowVersion != baseRowVersion)
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        if (!participant.IsAdmitted)
        {
            // Già escluso: nessuna scrittura, versione invariata.
            return await SessionCommandOperation.CompleteAsync(work, SessionParticipantDto.FromDomain(participant), cancellationToken).ConfigureAwait(false);
        }

        participant.Exclude(timeProvider.GetUtcNow());

        var row = SessionParticipantEntity.FromDomain(participant);
        if (!await work.Repo<ISessionParticipantRepository>()
                .UpdateAsync(row, cancellationToken)
                .ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        // §15.1: il thread della chat non appartiene più a un destinatario ammesso, quindi viene
        // eliminato. Il lavoro già svolto sul tentativo resta nel database.
        messages.Remove(request.ParticipantId);

        return await SessionCommandOperation.CompleteAsync(work,
            SessionParticipantDto.FromDomain(participant.WithRowVersion(row.RowVersion)), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Regole condivise dai comandi di regia dei partecipanti: caricare la sessione verificandone la
/// proprietà, caricare il partecipante verificandone l'appartenenza e confermare una postazione
/// della classe. Sono le stesse condizioni per correggere e per escludere, e appartengono a un solo
/// punto per non duplicarle.
/// </summary>
internal static class SessionParticipantRegiaRules
{
    /// <summary>
    /// Sessione della credenziale di regia. La sessione di un altro amministratore è indistinguibile
    /// da una inesistente (404, decisione D3, installazione a docente singolo).
    /// </summary>
    public static async Task<LessonSession> LoadControlledSessionAsync(
        IUnitOfWork work,
        Guid sessionId,
        Guid controllerAdminId,
        CancellationToken cancellationToken)
    {
        var stored = await work.Repo<ILessonSessionRepository>()
            .GetAsync(sessionId.ToString("D"), cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var session = stored.ToDomain();

        if (session.ControllerAdminId != controllerAdminId)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        return session;
    }

    /// <summary>
    /// Partecipante della sessione indicata. Una partecipazione di un'altra sessione è una risorsa
    /// che il comando non può raggiungere, quindi un 404.
    /// </summary>
    public static async Task<SessionParticipant> LoadParticipantAsync(
        IUnitOfWork work,
        Guid sessionId,
        Guid participantId,
        CancellationToken cancellationToken)
    {
        var stored = await work.Repo<ISessionParticipantRepository>()
            .GetAsync(participantId.ToString("D"), cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var participant = stored.ToDomain();

        if (participant.SessionId != sessionId)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        return participant;
    }

    /// <summary>
    /// La postazione dichiarata deve esistere nella classe della sessione: un PC di un'altra classe
    /// non è una postazione di questa lezione.
    /// </summary>
    public static async Task EnsureStationAsync(
        IUnitOfWork work,
        Guid stationId,
        LessonSession session,
        CancellationToken cancellationToken)
    {
        var station = await work.Repo<IStationRepository>()
            .GetAsync(stationId.ToString("D"), cancellationToken)
            .ConfigureAwait(false);

        if (station is null ||
            !string.Equals(station.ClassroomId, session.ClassroomId.ToString("D"), StringComparison.Ordinal))
        {
            throw CodiRequestException.ResourceNotFound();
        }
    }
}
