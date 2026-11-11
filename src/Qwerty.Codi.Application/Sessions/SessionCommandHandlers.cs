using Qwerty.Codi.Application.Dto;
using System.Text.Json;
using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Istanzia la lezione in una classe (§6.1, §10.4): verifica che lezione e classe esistano e che
/// la classe non abbia già una sessione non terminata, poi inserisce la sessione in <c>Lobby</c>.
///
/// L'inserimento è una scrittura singola, quindi senza transazione, come <c>CreateCourseHandler</c>:
/// l'unicità della sessione non terminata per classe resta garantita dall'indice parziale di §7.6,
/// che è la vera barriera contro due creazioni concorrenti.
/// </summary>
public sealed class CreateSessionHandler(
    IUnitOfWorkContext unitOfWorkContext,
    TimeProvider timeProvider) : IRequestHandler<CreateSessionCommand, SessionDto>
{
    public async ValueTask<SessionDto> Handle(CreateSessionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } controllerAdminId)
        {
            throw CodiRequestException.AuthenticationRequired(CreateSessionCommand.RequestName);
        }

        var lessonId = request.LessonId.ToString("D");
        var classroomId = request.ClassroomId.ToString("D");

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (await work.Repo<ILessonRepository>()
                .GetAsync(lessonId, cancellationToken)
                .ConfigureAwait(false) is null)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        if (await work.Repo<IClassroomRepository>()
                .GetAsync(classroomId, cancellationToken)
                .ConfigureAwait(false) is null)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var sessions = work.Repo<ILessonSessionRepository>();
        var forClassroom = await sessions
            .QueryAsync(row => row.ClassroomId == classroomId, cancellationToken)
            .ConfigureAwait(false);

        // Una sessione non terminata è quella in qualunque stato diverso da Ended (§6.1, §7.6).
        if (forClassroom.Any(row => !string.Equals(row.Status, LessonSessionEntity.EndedStatusText, StringComparison.Ordinal)))
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionAlreadyOpen,
                "La classe ha già una sessione non terminata: occorre terminarla prima di istanziarne una nuova.");
        }

        var session = LessonSession.Create(
            request.LessonId, request.ClassroomId, controllerAdminId, timeProvider.GetUtcNow());

        await sessions
            .InsertAsync(LessonSessionEntity.FromDomain(session), cancellationToken)
            .ConfigureAwait(false);

        return await SessionCommandOperation.CompleteAsync(work, SessionDto.FromDomain(session), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Avvia una sessione (§6.1, §6.4): carica la sessione, ne verifica proprietà, revisione attesa e
/// stato, poi la porta in play. La lettura e la scrittura stanno nella stessa transazione, così il
/// controllo di revisione non ha finestre fra la decisione e l'aggiornamento.
///
/// Il rifiuto previsto (revisione superata, stato incompatibile) è prodotto qui con i codici di
/// §10.7 **prima** di invocare l'invariante di dominio; <see cref="LessonSession.Start"/> la
/// riafferma e un suo rifiuto resta un errore di programmazione, non una risposta di successo.
/// La proprietà è verificata contro il contesto: la sessione di un altro amministratore è
/// indistinguibile da una inesistente (404, non 403 — decisione D3, installazione a docente singolo).
/// </summary>
public sealed class StartSessionHandler(
    IUnitOfWorkContext unitOfWorkContext,
    ISessionCoordinator sessionCoordinator,
    TimeProvider timeProvider) : IRequestHandler<StartSessionCommand, SessionDto>
{
    public async ValueTask<SessionDto> Handle(StartSessionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } controllerAdminId)
        {
            throw CodiRequestException.AuthenticationRequired(StartSessionCommand.RequestName);
        }

        if (request.ExpectedSessionRevision is not { } expectedSessionRevision)
        {
            throw CodiRequestException.VersionRequired(StartSessionCommand.RequestName);
        }

        // §8.2: il coordinamento della sessione si acquisisce **prima** della transazione, e si
        // rilascia dopo il commit. Aspettare qui non tiene aperta alcuna connessione.
        await using var work = await SessionCommandOperation.OpenAsync(
            unitOfWorkContext, sessionCoordinator, timeProvider, request.SessionId,
            request.Context, StartSessionCommand.RequestName, request, cancellationToken).ConfigureAwait(false);
        if (work.TryReplay<SessionDto>(out var replay)) return replay;

        var sessions = work.Repo<ILessonSessionRepository>();
        var stored = await sessions
            .GetAsync(request.SessionId.ToString("D"), cancellationToken)
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

        if (session.SessionRevision != expectedSessionRevision)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionRevisionConflict,
                "La sessione è cambiata dopo la lettura: ricaricare lo stato e ripetere l'avvio con la revisione corrente.");
        }

        if (session.Status != SessionStatus.Lobby)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionStateConflict,
                "La sessione non è in attesa: solo una sessione in Lobby può essere avviata.");
        }

        session.Start(
            expectedSessionRevision,
            timeProvider.GetUtcNow(),
            await GenerateJoinCodeAsync(sessions, cancellationToken).ConfigureAwait(false));

        var row = LessonSessionEntity.FromDomain(session);
        if (!await sessions.UpdateAsync(row, cancellationToken).ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        return await SessionCommandOperation.CompleteAsync(work, SessionDto.FromDomain(session.WithRowVersion(row.RowVersion)), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Tentativi di generazione di un codice libero prima di dichiarare l'impossibilità.</summary>
    private const int JoinCodeGenerationAttempts = 8;

    /// <summary>
    /// Genera il codice di ingresso della sessione (§9.4) verificando che non sia già assegnato: la
    /// garanzia finale è l'indice unico <c>ux_lesson_session_join_code</c>, ma un codice ripetuto
    /// farebbe entrare l'alunno nella lezione sbagliata, quindi si ritenta invece di usarlo. La
    /// probabilità di collisione è trascurabile; l'esaurimento dei tentativi è un guasto del server,
    /// non un errore del docente, e resta un errore inatteso.
    /// </summary>
    private static async Task<SessionJoinCode> GenerateJoinCodeAsync(
        ILessonSessionRepository sessions,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < JoinCodeGenerationAttempts; attempt++)
        {
            var candidate = SessionJoinCode.Create();
            var value = candidate.Value;
            if (!await sessions
                    .ExistsAsync(row => row.JoinCode == value, cancellationToken)
                    .ConfigureAwait(false))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "Non è stato possibile generare un codice di ingresso libero: nessun codice è stato assegnato.");
    }
}

/// <summary>
/// Sospende la sessione (§6.1, §6.4): carica la sessione, ne verifica proprietà, revisione attesa
/// e stato, poi la porta in pausa di classe. La lettura e la scrittura stanno nella stessa
/// transazione, così il controllo di revisione non ha finestre fra la decisione e l'aggiornamento.
///
/// Il rifiuto previsto (revisione superata, stato incompatibile) è prodotto qui con i codici di
/// §10.7 **prima** di invocare l'invariante di dominio; <see cref="LessonSession.Pause"/> la
/// riafferma e un suo rifiuto resta un errore di programmazione, non una risposta di successo.
/// La proprietà è verificata contro il contesto: la sessione di un altro amministratore è
/// indistinguibile da una inesistente (404, non 403 — decisione D3, installazione a docente singolo).
/// </summary>
public sealed class PauseHandler(
    IUnitOfWorkContext unitOfWorkContext,
    ISessionCoordinator sessionCoordinator,
    TimeProvider timeProvider,
    IOutboxSignal outboxSignal) : IRequestHandler<PauseCommand, SessionDto>
{
    public async ValueTask<SessionDto> Handle(PauseCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } controllerAdminId)
        {
            throw CodiRequestException.AuthenticationRequired(PauseCommand.RequestName);
        }

        if (request.ExpectedSessionRevision is not { } expectedSessionRevision)
        {
            throw CodiRequestException.VersionRequired(PauseCommand.RequestName);
        }

        // §8.2: coordinamento della sessione, poi transazione.
        await using var work = await SessionCommandOperation.OpenAsync(
            unitOfWorkContext, sessionCoordinator, timeProvider, request.SessionId,
            request.Context, PauseCommand.RequestName, request, cancellationToken).ConfigureAwait(false);
        if (work.TryReplay<SessionControlAcceptedDto>(out var replay)) return replay;

        var sessions = work.Repo<ILessonSessionRepository>();
        var stored = await sessions
            .GetAsync(request.SessionId.ToString("D"), cancellationToken)
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

        if (session.SessionRevision != expectedSessionRevision)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionRevisionConflict,
                "La sessione è cambiata dopo la lettura: ricaricare lo stato e ripetere la pausa con la revisione corrente.");
        }

        if (request.ParticipantId is null && session.Status != SessionStatus.Active)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionStateConflict,
                "La sessione non è in play: solo una sessione in Active può essere messa in pausa.");
        }

        var now = timeProvider.GetUtcNow();
        if (request.ParticipantId is null) session.Pause(expectedSessionRevision, now);
        else session.RegisterDesiredStateChange(expectedSessionRevision, now);

        var row = LessonSessionEntity.FromDomain(session);
        if (!await sessions.UpdateAsync(row, cancellationToken).ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        var accepted = await SessionControlState.ApplyAsync(work, session, controllerAdminId, PauseCommand.RequestName,
            request.ParticipantId, now, outboxSignal, cancellationToken).ConfigureAwait(false);
        var response = await SessionCommandOperation.CompleteAsync(work,
            SessionControlAcceptedDto.Create(SessionDto.FromDomain(session.WithRowVersion(row.RowVersion)), accepted), cancellationToken).ConfigureAwait(false);
        outboxSignal.Notify();
        return response;
    }
}

/// <summary>
/// Riprende la sessione (§6.1, §6.4): carica la sessione, ne verifica proprietà, revisione attesa
/// e stato, poi la riporta in play. La lettura e la scrittura stanno nella stessa transazione, così
/// il controllo di revisione non ha finestre fra la decisione e l'aggiornamento.
///
/// Il rifiuto previsto è prodotto qui con i codici di §10.7 **prima** di invocare l'invariante di
/// dominio; <see cref="LessonSession.Resume"/> la riafferma. La proprietà è verificata contro il
/// contesto: la sessione di un altro amministratore è indistinguibile da una inesistente (404).
/// </summary>
public sealed class ResumeHandler(
    IUnitOfWorkContext unitOfWorkContext,
    ISessionCoordinator sessionCoordinator,
    TimeProvider timeProvider,
    IOutboxSignal outboxSignal) : IRequestHandler<ResumeCommand, SessionDto>
{
    public async ValueTask<SessionDto> Handle(ResumeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } controllerAdminId)
        {
            throw CodiRequestException.AuthenticationRequired(ResumeCommand.RequestName);
        }

        if (request.ExpectedSessionRevision is not { } expectedSessionRevision)
        {
            throw CodiRequestException.VersionRequired(ResumeCommand.RequestName);
        }

        // §8.2: coordinamento della sessione, poi transazione.
        await using var work = await SessionCommandOperation.OpenAsync(
            unitOfWorkContext, sessionCoordinator, timeProvider, request.SessionId,
            request.Context, ResumeCommand.RequestName, request, cancellationToken).ConfigureAwait(false);
        if (work.TryReplay<SessionControlAcceptedDto>(out var replay)) return replay;

        var sessions = work.Repo<ILessonSessionRepository>();
        var stored = await sessions
            .GetAsync(request.SessionId.ToString("D"), cancellationToken)
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

        if (session.SessionRevision != expectedSessionRevision)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionRevisionConflict,
                "La sessione è cambiata dopo la lettura: ricaricare lo stato e ripetere la ripresa con la revisione corrente.");
        }

        if (request.ParticipantId is null && session.Status != SessionStatus.Paused)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionStateConflict,
                "La sessione non è in pausa: solo una sessione in Paused può essere ripresa.");
        }

        var now = timeProvider.GetUtcNow();
        if (request.ParticipantId is null) session.Resume(expectedSessionRevision, now);
        else session.RegisterDesiredStateChange(expectedSessionRevision, now);

        var row = LessonSessionEntity.FromDomain(session);
        if (!await sessions.UpdateAsync(row, cancellationToken).ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        var accepted = await SessionControlState.ApplyAsync(work, session, controllerAdminId, ResumeCommand.RequestName,
            request.ParticipantId, now, outboxSignal, cancellationToken).ConfigureAwait(false);
        var response = await SessionCommandOperation.CompleteAsync(work,
            SessionControlAcceptedDto.Create(SessionDto.FromDomain(session.WithRowVersion(row.RowVersion)), accepted), cancellationToken).ConfigureAwait(false);
        outboxSignal.Notify();
        return response;
    }
}

/// <summary>
/// Conclude la sessione (§6.1, §6.4): carica la sessione, ne verifica proprietà, revisione attesa
/// e stato, poi la porta in <c>Ended</c> da qualunque stato non terminale. La lettura e la
/// scrittura stanno nella stessa transazione, così il controllo di revisione non ha finestre fra
/// la decisione e l'aggiornamento.
///
/// Il rifiuto previsto è prodotto qui con i codici di §10.7 **prima** di invocare l'invariante di
/// dominio; <see cref="LessonSession.End"/> la riafferma. La proprietà è verificata contro il
/// contesto: la sessione di un altro amministratore è indistinguibile da una inesistente (404).
/// La distribuzione ai client e la finestra di salvataggio finale non esistono ancora: qui si
/// persiste soltanto lo stato terminale della sessione.
/// </summary>
public sealed class EndSessionHandler(
    IUnitOfWorkContext unitOfWorkContext,
    ISessionCoordinator sessionCoordinator,
    TimeProvider timeProvider,
    IOutboxSignal outboxSignal,
    IParticipantMessageStore messages) : IRequestHandler<EndSessionCommand, SessionDto>
{
    public async ValueTask<SessionDto> Handle(EndSessionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } controllerAdminId)
        {
            throw CodiRequestException.AuthenticationRequired(EndSessionCommand.RequestName);
        }

        if (request.ExpectedSessionRevision is not { } expectedSessionRevision)
        {
            throw CodiRequestException.VersionRequired(EndSessionCommand.RequestName);
        }

        // §8.2: coordinamento della sessione, poi transazione.
        await using var work = await SessionCommandOperation.OpenAsync(
            unitOfWorkContext, sessionCoordinator, timeProvider, request.SessionId,
            request.Context, EndSessionCommand.RequestName, request, cancellationToken).ConfigureAwait(false);
        if (work.TryReplay<SessionControlAcceptedDto>(out var replay)) return replay;

        var sessions = work.Repo<ILessonSessionRepository>();
        var stored = await sessions
            .GetAsync(request.SessionId.ToString("D"), cancellationToken)
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

        if (session.SessionRevision != expectedSessionRevision)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionRevisionConflict,
                "La sessione è cambiata dopo la lettura: ricaricare lo stato e ripetere la conclusione con la revisione corrente.");
        }

        if (session.Status == SessionStatus.Ended)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionStateConflict,
                "La sessione è già terminata: una sessione conclusa non si termina di nuovo.");
        }

        var now = timeProvider.GetUtcNow();
        session.End(expectedSessionRevision, now);

        var row = LessonSessionEntity.FromDomain(session);
        if (!await sessions.UpdateAsync(row, cancellationToken).ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        var accepted = await SessionControlState.ApplyAsync(work, session, controllerAdminId, EndSessionCommand.RequestName,
            null, now, outboxSignal, cancellationToken).ConfigureAwait(false);
        var response = await SessionCommandOperation.CompleteAsync(work,
            SessionControlAcceptedDto.Create(SessionDto.FromDomain(session.WithRowVersion(row.RowVersion)), accepted), cancellationToken).ConfigureAwait(false);

        // §15.1: alla fine della sessione i thread della chat vengono eliminati. Non è una pulizia
        // opzionale — il testo scritto dai bambini non deve restare nel processo dopo la lezione.
        messages.ClearSession(session.Id);

        outboxSignal.Notify();
        return response;
    }
}

/// <summary>
/// Mostra l'attesa alla classe (§6.4, §6.5): carica la sessione, ne verifica proprietà, revisione
/// attesa e stato non terminale, riporta il default di classe alla vista di attesa e aggiorna lo
/// stato desiderato **di ogni partecipante** della sessione. Lettura e scrittura stanno nella stessa
/// transazione, così nessun alunno resta con la vista precedente e il controllo di revisione non ha
/// finestre.
///
/// Gli stati si leggono dal repository per sessione, non dai partecipanti uno per uno: è la tabella
/// dello stato desiderato a dire chi c'è, e un partecipante senza riga di stato non esiste (l'ingresso
/// la crea nella stessa transazione, §9.4). La distribuzione non avviene qui: l'handler registra le
/// consegne e il messaggio, e il dispatcher outbox li consegna dopo il commit (§12.2); la vista cambia
/// intanto per il server e per chi la legge con `GetParticipantContext`.
/// </summary>
public sealed class ShowWaitingHandler(
    IUnitOfWorkContext unitOfWorkContext,
    ISessionCoordinator sessionCoordinator,
    IOutboxSignal outboxSignal,
    TimeProvider timeProvider) : IRequestHandler<ShowWaitingCommand, CommandAcceptedDto>
{
    public async ValueTask<CommandAcceptedDto> Handle(ShowWaitingCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } controllerAdminId)
        {
            throw CodiRequestException.AuthenticationRequired(ShowWaitingCommand.RequestName);
        }

        if (request.ExpectedSessionRevision is not { } expectedSessionRevision)
        {
            throw CodiRequestException.VersionRequired(ShowWaitingCommand.RequestName);
        }

        // §8.2: coordinamento della sessione, poi transazione. La revisione si verifica dentro la
        // transazione, quindi due comandi non possono applicarsi alla stessa revisione di partenza.
        await using var work = await SessionCommandOperation.OpenAsync(
            unitOfWorkContext, sessionCoordinator, timeProvider, request.SessionId,
            request.Context, ShowWaitingCommand.RequestName, request, cancellationToken).ConfigureAwait(false);
        if (work.TryReplay<CommandAcceptedDto>(out var replay)) return replay;

        var sessions = work.Repo<ILessonSessionRepository>();
        var stored = await sessions
            .GetAsync(request.SessionId.ToString("D"), cancellationToken)
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

        if (session.SessionRevision != expectedSessionRevision)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionRevisionConflict,
                "La sessione è cambiata dopo la lettura: ricaricare lo stato e ripetere il comando con la revisione corrente.");
        }

        if (session.Status == SessionStatus.Ended)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionStateConflict,
                "La sessione è terminata: una sessione conclusa non torna in attesa.");
        }

        var now = timeProvider.GetUtcNow();
        var previousDefault = session.ClassDefaultJson;
        if (request.ParticipantId is null) session.ShowWaiting(expectedSessionRevision, now);
        else session.RegisterDesiredStateChange(expectedSessionRevision, now);

        var sessionRow = LessonSessionEntity.FromDomain(session);
        if (!await sessions.UpdateAsync(sessionRow, cancellationToken).ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        // La vista desiderata è quella del default appena scritto: si rilegge dal documento invece
        // di ripetere la costante, così i due valori non possono divergere.
        var view = nameof(ParticipantView.Waiting);
        var states = work.Repo<IParticipantStateRepository>();
        var participantStates = await SessionCommandTargets
            .ResolveAsync(work, request.SessionId, request.ParticipantId, cancellationToken)
            .ConfigureAwait(false);

        foreach (var state in participantStates)
        {
            if (state.View == nameof(ParticipantView.Exercise))
                state.ResumeContextJson = JsonSerializer.Serialize(DesiredStateProjection.Create(state), CodiJson.Payload);
            state.View = view;

            // Sospende il motore: in attesa non c'è attività con cui interagire (§6.2).
            state.InteractionEnabledFlag = 0;
            state.EffectiveRevision = session.SessionRevision;
            state.UpdatedAtUtcMs = now.ToUnixTimeMilliseconds();

            if (!await states.UpdateAsync(state, cancellationToken).ConfigureAwait(false))
            {
                throw CodiRequestException.RowVersionMismatch();
            }
        }

        return await DesiredStateCommandRegistration.RegisterAsync(work, outboxSignal, session,
            new DesiredStateCommandEffect(controllerAdminId, ShowWaitingCommand.RequestName, request.ParticipantId,
                new DesiredStateDto(view, false), participantStates, now, previousDefault), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Apre gli ingressi di una sessione (§6.1, §6.4): carica la sessione, ne verifica proprietà,
/// revisione attesa e stato non terminale, poi apre gli ingressi senza toccare lo stato. La lettura
/// e la scrittura stanno nella stessa transazione, così il controllo di revisione non ha finestre.
///
/// Ripetere l'apertura su ingressi già aperti restituisce lo stato corrente senza scrivere: come
/// l'archiviazione ripetuta, non è un errore e non incrementa la revisione. La proprietà è
/// verificata contro il contesto: la sessione di un altro amministratore è indistinguibile da una
/// inesistente (404, decisione D3).
/// </summary>
public sealed class OpenAdmissionsHandler(
    IUnitOfWorkContext unitOfWorkContext,
    ISessionCoordinator sessionCoordinator,
    TimeProvider timeProvider) : IRequestHandler<OpenAdmissionsCommand, SessionDto>
{
    public async ValueTask<SessionDto> Handle(OpenAdmissionsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } controllerAdminId)
        {
            throw CodiRequestException.AuthenticationRequired(OpenAdmissionsCommand.RequestName);
        }

        if (request.ExpectedSessionRevision is not { } expectedSessionRevision)
        {
            throw CodiRequestException.VersionRequired(OpenAdmissionsCommand.RequestName);
        }

        // §8.2: coordinamento della sessione, poi transazione.
        await using var work = await SessionCommandOperation.OpenAsync(
            unitOfWorkContext, sessionCoordinator, timeProvider, request.SessionId,
            request.Context, OpenAdmissionsCommand.RequestName, request, cancellationToken).ConfigureAwait(false);
        if (work.TryReplay<SessionDto>(out var replay)) return replay;

        var sessions = work.Repo<ILessonSessionRepository>();
        var stored = await sessions
            .GetAsync(request.SessionId.ToString("D"), cancellationToken)
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

        if (session.SessionRevision != expectedSessionRevision)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionRevisionConflict,
                "La sessione è cambiata dopo la lettura: ricaricare lo stato e ripetere l'apertura degli ingressi con la revisione corrente.");
        }

        if (session.Status == SessionStatus.Ended)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionStateConflict,
                "La sessione è terminata: gli ingressi di una sessione conclusa non si riaprono.");
        }

        if (!session.OpenAdmissions(expectedSessionRevision, timeProvider.GetUtcNow()))
        {
            // Ingressi già aperti: nessuna scrittura, revisione invariata.
            return await SessionCommandOperation.CompleteAsync(work, SessionDto.FromDomain(session), cancellationToken).ConfigureAwait(false);
        }

        var row = LessonSessionEntity.FromDomain(session);
        if (!await sessions.UpdateAsync(row, cancellationToken).ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        return await SessionCommandOperation.CompleteAsync(work, SessionDto.FromDomain(session.WithRowVersion(row.RowVersion)), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Chiude gli ingressi di una sessione (§6.1, §6.4): come <see cref="OpenAdmissionsHandler"/>,
/// verifica proprietà, revisione attesa e stato non terminale, poi chiude gli ingressi. Chiudere
/// ingressi già chiusi restituisce lo stato corrente senza scrivere.
/// </summary>
public sealed class CloseAdmissionsHandler(
    IUnitOfWorkContext unitOfWorkContext,
    ISessionCoordinator sessionCoordinator,
    TimeProvider timeProvider) : IRequestHandler<CloseAdmissionsCommand, SessionDto>
{
    public async ValueTask<SessionDto> Handle(CloseAdmissionsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } controllerAdminId)
        {
            throw CodiRequestException.AuthenticationRequired(CloseAdmissionsCommand.RequestName);
        }

        if (request.ExpectedSessionRevision is not { } expectedSessionRevision)
        {
            throw CodiRequestException.VersionRequired(CloseAdmissionsCommand.RequestName);
        }

        // §8.2: coordinamento della sessione, poi transazione.
        await using var work = await SessionCommandOperation.OpenAsync(
            unitOfWorkContext, sessionCoordinator, timeProvider, request.SessionId,
            request.Context, CloseAdmissionsCommand.RequestName, request, cancellationToken).ConfigureAwait(false);
        if (work.TryReplay<SessionDto>(out var replay)) return replay;

        var sessions = work.Repo<ILessonSessionRepository>();
        var stored = await sessions
            .GetAsync(request.SessionId.ToString("D"), cancellationToken)
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

        if (session.SessionRevision != expectedSessionRevision)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionRevisionConflict,
                "La sessione è cambiata dopo la lettura: ricaricare lo stato e ripetere la chiusura degli ingressi con la revisione corrente.");
        }

        if (session.Status == SessionStatus.Ended)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionStateConflict,
                "La sessione è terminata: gli ingressi di una sessione conclusa restano chiusi.");
        }

        if (!session.CloseAdmissions(expectedSessionRevision, timeProvider.GetUtcNow()))
        {
            // Ingressi già chiusi: nessuna scrittura, revisione invariata.
            return await SessionCommandOperation.CompleteAsync(work, SessionDto.FromDomain(session), cancellationToken).ConfigureAwait(false);
        }

        var row = LessonSessionEntity.FromDomain(session);
        if (!await sessions.UpdateAsync(row, cancellationToken).ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        return await SessionCommandOperation.CompleteAsync(work, SessionDto.FromDomain(session.WithRowVersion(row.RowVersion)), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Elimina definitivamente una sessione conclusa o mai avviata (§6.1, §10.4). Il caso d'uso è
/// separato dalla chiusura perché la chiusura è irreversibile ma conserva lo storico, mentre la
/// cancellazione lo rimuove: §6.1 chiede che le funzioni di cancellazione siano riconoscibili.
///
/// La lettura e le cancellazioni stanno nella stessa transazione del coordinatore di sessione, così
/// nessun comando di regia può inserirsi fra il controllo dello stato e la rimozione. Il controllo
/// di revisione è quello degli altri comandi di regia; la riga della sessione si cancella con la
/// versione letta, che è l'ultima barriera contro una scrittura concorrente.
///
/// I thread della chat e le copie in memoria dell'anteprima e della telemetria non sono dati
/// persistiti: si eliminano **dopo** il commit, perché una transazione annullata non deve aver
/// toccato la memoria del processo (§15.1, §15.5).
/// </summary>
public sealed class DeleteSessionHandler(
    IUnitOfWorkContext unitOfWorkContext,
    ISessionCoordinator sessionCoordinator,
    IParticipantMessageStore messages,
    IParticipantPreviewCache previews,
    IParticipantObservationCache observations) : IRequestHandler<DeleteSessionCommand, SessionDeletedDto>
{
    public async ValueTask<SessionDeletedDto> Handle(DeleteSessionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } controllerAdminId)
        {
            throw CodiRequestException.AuthenticationRequired(DeleteSessionCommand.RequestName);
        }

        if (request.ExpectedSessionRevision is not { } expectedSessionRevision)
        {
            throw CodiRequestException.VersionRequired(DeleteSessionCommand.RequestName);
        }

        // §8.2: coordinamento della sessione prima della transazione, come per i comandi di regia.
        await using var coordination = await sessionCoordinator
            .AcquireAsync(request.SessionId, cancellationToken)
            .ConfigureAwait(false);

        Guid[] participantIds;
        await using (var work = await unitOfWorkContext
                         .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
                         .ConfigureAwait(false))
        {
            var sessions = work.Repo<ILessonSessionRepository>();
            var stored = await sessions
                .GetAsync(request.SessionId.ToString("D"), cancellationToken)
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

            if (session.SessionRevision != expectedSessionRevision)
            {
                throw new CodiRequestException(
                    CodiErrorCodes.SessionRevisionConflict,
                    "La sessione è cambiata dopo la lettura: ricaricare l'elenco e ripetere l'eliminazione con la revisione corrente.");
            }

            if (session.Status is not (SessionStatus.Ended or SessionStatus.Lobby))
            {
                throw new CodiRequestException(
                    CodiErrorCodes.SessionStateConflict,
                    "La sessione è in corso: concludila con Stop e solo dopo eliminala.");
            }

            participantIds = [.. (await work.Repo<ISessionParticipantRepository>()
                .QueryAsync(row => row.SessionId == stored.Id, cancellationToken)
                .ConfigureAwait(false))
                .Select(row => Guid.Parse(row.Id))];

            await work.Repo<ISessionRemovalStore>()
                .RemoveChildrenAsync(stored.Id, cancellationToken)
                .ConfigureAwait(false);

            if (!await sessions.DeleteAsync(stored, cancellationToken).ConfigureAwait(false))
            {
                throw CodiRequestException.RowVersionMismatch();
            }

            await work.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        // Solo dopo il commit: la chat di una lezione eliminata non deve restare nel processo.
        messages.ClearSession(request.SessionId);
        foreach (var participantId in participantIds)
        {
            previews.Remove(participantId);
            observations.Remove(participantId);
        }

        return new SessionDeletedDto(request.SessionId);
    }
}
