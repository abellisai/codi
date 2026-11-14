import { DestroyRef, Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { TeacherRealtime } from '@core/teacher-realtime';
import {
  ApiError,
  ApiService,
  CommandAccepted,
  Exercise,
  LessonExercise,
  ParticipantPreviewView,
  Session,
  SessionParticipantDetail,
  SessionParticipantOverview,
  SessionSummary,
  toApiError,
} from '@core/api';

/** Casella della sequenza di una lezione, con il titolo dell'esercizio per riconoscerla. */
export interface LessonBox {
  readonly id: string;
  readonly position: number;
  readonly title: string;
}

/**
 * Stato condiviso della regia di una sessione (§10.5, §12.1, §14.1).
 *
 * La pagina della regia e la pagina dei partecipanti riguardano la stessa sessione: lo stato
 * autoritativo ha quindi un **unico punto di aggiornamento**, il server, invece di due copie che
 * possono divergere mentre il docente passa dall'una all'altra. Il servizio possiede la lettura,
 * la sottoscrizione all'hub e l'esecuzione dei comandi; le pagine decidono solo *dove* mostrare i
 * dati e quali azioni offrire.
 *
 * Ogni comando dichiara la `expectedSessionRevision` letta e, prima di riabilitare i controlli,
 * rilegge la sessione e i partecipanti: un comando parte dallo stato confermato dal server, non da
 * quello ipotizzato. La revisione è il token del contratto — il servizio la invia, non la mostra.
 *
 * La connessione all'hub vive finché una pagina sta usando la sessione: la regia e i partecipanti
 * condividono la stessa sottoscrizione, senza una connessione per ogni route.
 */
@Injectable({ providedIn: 'root' })
export class SessionRegiaService {
  private readonly api = inject(ApiService);
  private readonly realtime = inject(TeacherRealtime);
  private readonly destroyRef = inject(DestroyRef);

  /** Pagine registrate sulla sessione: la sottoscrizione resta attiva finché ne resta una. */
  private registeredPages = 0;
  private observedSessionId = '';

  readonly sessionId = signal('');
  readonly session = signal<SessionSummary | null>(null);

  /** Partecipanti (§10.5): presenza e stato **desiderato** di ogni alunno ammesso. */
  readonly participants = signal<readonly SessionParticipantOverview[]>([]);

  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal<ApiError | null>(null);

  /** Indirizzo senza identificativo: non è un errore del server, ma va detto. */
  readonly missingId = signal(false);

  /** Esito dell'ultimo comando sul contenuto: quante consegne sono state messe in coda (§10.6). */
  readonly notice = signal('');

  readonly online = this.realtime.online;
  readonly commands = computed(() => this.realtime.snapshot()?.commands ?? []);

  /** Dettaglio dell'alunno aperto: uno alla volta, come chiede §14.3. */
  readonly detailFor = signal('');
  readonly detail = signal<SessionParticipantDetail | null>(null);

  /** Anteprima dei blocchi dell'alunno aperto (§15.5): si legge per l'alunno selezionato. */
  readonly preview = signal<ParticipantPreviewView | null>(null);
  readonly previewProblem = signal('');

  /** Caselle della lezione, lette quando il docente apre la scelta dell'esercizio da assegnare. */
  readonly boxes = signal<readonly LessonBox[]>([]);

  constructor() {
    // Lo snapshot dell'hub aggiorna la stessa lettura del server, senza ricostruire le pagine.
    effect(() => {
      const snapshot = this.realtime.snapshot();
      if (snapshot === null) return;
      untracked(() => {
        const current = this.session();
        if (
          current === null ||
          current.id !== snapshot.session.id ||
          BigInt(snapshot.session.sessionRevision) < BigInt(current.sessionRevision)
        )
          return;
        this.session.set({ ...current, ...snapshot.session });
        this.participants.set(snapshot.participants);
      });
    });

    // L'hub annuncia un'anteprima nuova: si rilegge il contenuto solo per l'alunno aperto (§15.5).
    effect(() => {
      const open = this.detailFor();
      const receipt = this.realtime.previews().find((row) => row.participantId === open);
      if (open === '' || receipt === undefined) return;
      untracked(() => {
        void this.loadPreview(open);
      });
    });

    this.destroyRef.onDestroy(() => {
      void this.realtime.stop();
    });
  }

  /**
   * Registrazione della pagina: la sottoscrizione all'hub vive finché una pagina usa la sessione,
   * quindi la regia e i partecipanti condividono la stessa connessione invece di aprirne una per
   * route (§14.2).
   */
  register(): void {
    this.registeredPages += 1;
  }

  /** Disregistrazione: la connessione si chiude solo se nessuna pagina usa più la sessione. */
  unregister(): void {
    this.registeredPages = Math.max(0, this.registeredPages - 1);
    if (this.registeredPages === 0) {
      this.observedSessionId = '';
      void this.realtime.stop();
    }
  }

  /**
   * Sessione mostrata: arriva dall'indirizzo, si legge dal server e si osserva una volta per
   * sessione. Cambiare sessione chiude il dettaglio aperto, che apparteneva alla precedente.
   */
  async show(sessionId: string): Promise<void> {
    if (this.sessionId() !== sessionId) {
      this.detailFor.set('');
      this.detail.set(null);
      this.preview.set(null);
      this.previewProblem.set('');
    }

    this.sessionId.set(sessionId);
    this.loading.set(true);

    try {
      await this.refresh();
    } finally {
      this.loading.set(false);
    }

    if (
      this.registeredPages > 0 &&
      this.sessionId() === sessionId &&
      sessionId !== '' &&
      sessionId !== this.observedSessionId
    ) {
      this.observedSessionId = sessionId;
      void this.realtime.observe(sessionId);
    }
  }

  /**
   * Rilegge la sessione dal server, l'unica fonte autorevole dello stato. Un identificativo
   * mancante o una sessione non leggibile svuotano lo stato e preparano il ritorno all'elenco.
   */
  async refresh(): Promise<void> {
    const id = this.sessionId();
    if (id === '') {
      this.session.set(null);
      this.missingId.set(true);
      return;
    }

    this.missingId.set(false);

    try {
      const session = await this.api.executeQuery<SessionSummary>('GetSession', { sessionId: id });
      if (this.sessionId() !== id) return;
      const snapshot = this.realtime.snapshot();
      this.session.set(
        snapshot !== null &&
          snapshot.session.id === id &&
          BigInt(snapshot.session.sessionRevision) >= BigInt(session.sessionRevision)
          ? { ...session, ...snapshot.session }
          : session,
      );

      // I partecipanti si rileggono insieme alla sessione: dopo un comando le due cose cambiano
      // assieme, e mostrare l'una aggiornata e l'altra no racconterebbe uno stato che non esiste.
      const participants = await this.api.executeQuery<readonly SessionParticipantOverview[]>(
        'ListSessionParticipants',
        { sessionId: id },
      );
      if (this.sessionId() !== id) return;
      const latest = this.realtime.snapshot();
      this.participants.set(
        latest !== null &&
          latest.session.id === id &&
          BigInt(latest.session.sessionRevision) > BigInt(session.sessionRevision)
          ? latest.participants
          : participants,
      );

      // Il dettaglio aperto è dello stesso istante della griglia: un comando può cambiare il
      // tentativo (azzeramento, assegnazione) e mostrarne uno aggiornato e l'altro no
      // racconterebbe uno stato che non esiste (§14.3).
      const open = this.detailFor();
      if (open !== '') await this.readDetail(open);
    } catch (failure) {
      this.session.set(null);
      this.participants.set([]);
      this.error.set(toApiError(failure));
    }
  }

  /**
   * Esegue un comando di regia con la revisione letta e rilegge la sessione prima di riabilitare i
   * controlli: il blocco vale anche in caso di rifiuto, così il comando successivo parte dalla
   * revisione confermata dal server (§10.7).
   */
  private async runCommand(name: string, participantId?: string): Promise<void> {
    const current = this.session();
    if (current === null || current.status === 'Ended' || this.busy() || !this.online()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      await this.api.executeCommand<Session>(name, {
        ...(participantId === undefined ? {} : { participantId }),
        sessionId: current.id,
        expectedSessionRevision: current.sessionRevision,
      });
    } catch (failure) {
      this.error.set(toApiError(failure));
    } finally {
      await this.refresh();
      this.busy.set(false);
    }
  }

  /** Avvia la sessione in play: ammesso solo dalla sala d'attesa. */
  async start(): Promise<void> {
    await this.runCommand('StartSession');
  }

  /** Mette in pausa la classe: ammesso solo mentre è in corso. */
  async pause(): Promise<void> {
    await this.runCommand('Pause');
  }

  /** Riprende la classe in pausa: torna in corso. */
  async resume(): Promise<void> {
    await this.runCommand('Resume');
  }

  /** Conclude la sessione: è un'azione definitiva, chiesta con conferma dalla pagina. */
  async end(): Promise<void> {
    await this.runCommand('EndSession');
  }

  async changeAdmissions(): Promise<void> {
    if (!['Active', 'Paused'].includes(this.session()?.status ?? '')) return;
    await this.runCommand(this.session()?.admissionsOpen ? 'CloseAdmissions' : 'OpenAdmissions');
  }

  canManageParticipant(participant: SessionParticipantOverview): boolean {
    return (
      this.canEnd() &&
      participant.admissionStatus === 'Admitted' &&
      participant.rowVersion !== undefined
    );
  }

  async updateParticipant(
    participant: SessionParticipantOverview,
    displayName?: string,
  ): Promise<void> {
    if (!this.canManageParticipant(participant) || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.executeCommand(
        displayName === undefined ? 'ExcludeParticipant' : 'UpdateParticipant',
        {
          sessionId: this.sessionId(),
          participantId: participant.participantId,
          baseRowVersion: participant.rowVersion,
          ...(displayName === undefined ? {} : { displayName, stationId: participant.stationId }),
        },
      );
    } catch (failure) {
      this.error.set(toApiError(failure));
    } finally {
      await this.refresh();
      this.busy.set(false);
    }
  }

  /** Pausa di un solo alunno: l'eccezione individuale resta distinta da quella della classe. */
  async pauseParticipant(participant: SessionParticipantOverview): Promise<void> {
    await this.runCommand('Pause', participant.participantId);
  }

  /** Ripresa di un solo alunno. */
  async resumeParticipant(participant: SessionParticipantOverview): Promise<void> {
    await this.runCommand('Resume', participant.participantId);
  }

  /**
   * Esegue un comando sul contenuto della sessione: revisione letta, rifiuto mostrato con il suo
   * messaggio, rilettura dello stato e dei partecipanti prima di riabilitare i controlli (§10.7,
   * §12.1). L'esito dice quante consegne sono in coda: «accettato» non è «applicato».
   */
  private async runContentCommand(
    name: string,
    extra: Record<string, unknown> = {},
  ): Promise<void> {
    const current = this.session();
    if (current === null || current.status === 'Ended' || this.busy() || !this.online()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.notice.set('');

    try {
      const accepted = await this.api.executeCommand<CommandAccepted>(name, {
        sessionId: current.id,
        expectedSessionRevision: current.sessionRevision,
        ...extra,
      });

      this.notice.set(`${name}: ${accepted.deliverySummary.pending} consegne registrate.`);
    } catch (failure) {
      this.error.set(toApiError(failure));
    } finally {
      await this.refresh();
      this.busy.set(false);
    }
  }

  /** Abilita l'attività di tutta la classe: i controlli si accendono senza avviare il programma. */
  async enableClass(): Promise<void> {
    if (!this.canEnableClass()) return;
    await this.runContentCommand('EnableActivity');
  }

  /** Abilita l'attività di un solo alunno. */
  async enableParticipant(participant: SessionParticipantOverview): Promise<void> {
    await this.runContentCommand('EnableActivity', { participantId: participant.participantId });
  }

  /** Avvia il progetto corrente della classe: non azzera la scena. */
  async runClass(): Promise<void> {
    if (!this.canRunClass()) return;
    await this.runContentCommand('RunProgram');
  }

  /** Avvia il progetto di un solo alunno. */
  async runParticipant(participant: SessionParticipantOverview): Promise<void> {
    await this.runContentCommand('RunProgram', { participantId: participant.participantId });
  }

  /** Riavvia la classe: la generazione avanza e il progetto resta. */
  async restartClass(): Promise<void> {
    if (!this.canRunClass()) return;
    await this.runContentCommand('RestartExecution');
  }

  /** Riavvia il lavoro di un solo alunno. */
  async restartParticipant(participant: SessionParticipantOverview): Promise<void> {
    await this.runContentCommand('RestartExecution', { participantId: participant.participantId });
  }

  /** Assegna una casella della lezione alla classe: la versione la risolve il server (§6.4). */
  async assign(lessonExerciseId: string): Promise<void> {
    await this.runContentCommand('AssignExercise', { lessonExerciseId });
  }

  /**
   * Azzera il lavoro di un alunno: dichiara il tentativo mostrato nella conferma (§10.6), così il
   * server verifica che sia ancora quello corrente prima di archiviarlo.
   */
  async resetParticipant(participant: SessionParticipantOverview): Promise<void> {
    await this.runContentCommand('ResetWork', {
      participantId: participant.participantId,
      attemptId: participant.attemptId,
      attemptRowVersion: participant.attemptRowVersion,
    });
  }

  /**
   * Carica le caselle della lezione e i titoli degli esercizi all'apertura della scelta: il docente
   * sceglie da un elenco noto invece di digitare un identificativo.
   */
  async prepareAssignment(): Promise<void> {
    const current = this.session();
    if (current === null || current.status === 'Ended' || this.busy() || !this.online()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      const [boxes, exercises] = await Promise.all([
        this.api.executeQuery<readonly LessonExercise[]>('ListLessonExercises', {
          lessonId: current.lessonId,
        }),
        this.api.executeQuery<readonly Exercise[]>('ListExercises', { includeArchived: false }),
      ]);
      const titles = new Map(exercises.map((exercise) => [exercise.id, exercise.title]));

      this.boxes.set(
        boxes.map((box) => ({
          id: box.id,
          position: box.position,
          title: titles.get(box.exerciseId) ?? 'Esercizio senza titolo',
        })),
      );
    } catch (failure) {
      this.error.set(toApiError(failure));
    } finally {
      this.busy.set(false);
    }
  }

  /** Apre il dettaglio di un alunno, o lo chiude se è già quello aperto. */
  async toggleDetail(participant: SessionParticipantOverview): Promise<void> {
    if (this.detailFor() === participant.participantId) {
      this.detail.set(null);
      this.detailFor.set('');
      this.preview.set(null);
      this.previewProblem.set('');
      return;
    }

    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.detailFor.set(participant.participantId);
    this.preview.set(null);
    this.previewProblem.set('');

    try {
      await this.readDetail(participant.participantId);
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * Legge il dettaglio di un alunno e la sua anteprima, ma solo se è ancora quello aperto: la
   * lettura è asincrona e il docente può aver cambiato card nel frattempo. Un errore vero si
   * dichiara, senza svuotare il resto dello stato.
   */
  private async readDetail(participantId: string): Promise<void> {
    try {
      const detail = await this.api.executeQuery<SessionParticipantDetail>('GetParticipantDetail', {
        sessionId: this.sessionId(),
        participantId,
      });
      if (this.detailFor() !== participantId) return;
      this.detail.set(detail);
      await this.loadPreview(participantId);
    } catch (failure) {
      if (this.detailFor() !== participantId) return;
      this.error.set(toApiError(failure));
    }
  }

  /**
   * Legge l'ultima anteprima dell'alunno (§15.5). L'assenza non è un guasto: significa che il
   * browser non ha ancora pubblicato nulla — o che l'anteprima è scaduta — e il docente aspetta.
   * Un errore vero si dichiara a parte, senza nascondere il resto del dettaglio.
   */
  private async loadPreview(participantId: string): Promise<void> {
    try {
      const preview = await this.api.executeQuery<ParticipantPreviewView | null>(
        'GetParticipantPreview',
        { sessionId: this.sessionId(), participantId },
        { notFoundIsExpected: true },
      );
      if (this.detailFor() !== participantId) return;
      this.preview.set(preview);
      this.previewProblem.set('');
    } catch (failure) {
      if (this.detailFor() !== participantId) return;
      const problem = toApiError(failure);
      // Nessuna anteprima disponibile: il server risponde così, non è un errore da mostrare.
      this.preview.set(null);
      this.previewProblem.set(problem.status === 404 ? '' : problem.message);
    }
  }

  /**
   * Abilitazione dei comandi per stato: derivate dallo stato letto dal server, non da un contatore
   * locale. Una sessione riletta come conclusa non riabilita Avvia.
   */
  readonly canStart = computed(() => this.online() && this.session()?.status === 'Lobby');
  readonly canPause = computed(() => this.online() && this.session()?.status === 'Active');
  readonly canResume = computed(() => this.online() && this.session()?.status === 'Paused');
  readonly canEnd = computed(
    () => this.online() && this.session() !== null && this.session()?.status !== 'Ended',
  );

  /** Comandi sul contenuto: si offrono quando la classe è in corso e c'è qualcuno a cui applicarli. */
  readonly canCommandActivity = computed(
    () => this.online() && this.session()?.status === 'Active' && this.participants().length > 0,
  );

  /** I comandi collettivi riguardano gli alunni ammessi, come sul server. */
  private readonly classParticipants = computed(() =>
    this.participants().filter((participant) => participant.admissionStatus === 'Admitted'),
  );

  /** Tutti i destinatari devono avere un esercizio assegnato e poter lavorare. */
  readonly canEnableClass = computed(
    () =>
      this.canCommandActivity() &&
      this.classParticipants().length > 0 &&
      this.classParticipants().every((participant) => this.canEnable(participant)),
  );

  /** L'esecuzione collettiva richiede anche il permesso di lavorare per tutti. */
  readonly canRunClass = computed(
    () =>
      this.canEnableClass() &&
      this.classParticipants().every((participant) => this.canRun(participant)),
  );

  /** Abilitabile: ha un esercizio caricato, non è in pausa docente né di recupero, guarda l'esercizio. */
  canEnable(participant: SessionParticipantOverview): boolean {
    return (
      this.canEnd() &&
      participant.admissionStatus === 'Admitted' &&
      participant.attemptId !== null &&
      !participant.teacherPaused &&
      // Sulla pausa di recupero l'unica via è la ripresa: "Abilita i controlli" riaprirebbe
      // l'interazione lasciando il blocco aperto, uno sblocco a metà che la regia non deve offrire.
      !participant.recoveryBlocked &&
      participant.view === 'Exercise'
    );
  }

  /** Eseguibile: come sopra, con l'attività abilitata e nessuna pausa di recupero. */
  canRun(participant: SessionParticipantOverview): boolean {
    return (
      this.canEnable(participant) && participant.interactionEnabled && !participant.recoveryBlocked
    );
  }

  /** Azzerabile: c'è un tentativo e la pagina ne dichiara la versione da confermare. */
  canReset(participant: SessionParticipantOverview): boolean {
    return (
      this.canEnd() &&
      participant.admissionStatus === 'Admitted' &&
      participant.attemptId !== null &&
      participant.attemptRowVersion !== null
    );
  }

  /** Pausabile: un alunno già in pausa docente non viene messo in pausa una seconda volta. */
  canPauseParticipant(participant: SessionParticipantOverview): boolean {
    return (
      this.canEnd() && participant.admissionStatus === 'Admitted' && !participant.teacherPaused
    );
  }

  /** Riprendibile: l'alunno è in pausa docente, di recupero, o in attesa. */
  canResumeParticipant(participant: SessionParticipantOverview): boolean {
    return (
      this.canEnd() &&
      participant.admissionStatus === 'Admitted' &&
      (participant.teacherPaused || participant.recoveryBlocked || participant.view === 'Waiting')
    );
  }
}
