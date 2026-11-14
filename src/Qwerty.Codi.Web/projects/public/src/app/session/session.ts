import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import {
  ApiService,
  AssignedExercise,
  Attempt,
  DesiredState,
  ParticipantContext,
  ParticipantSnapshot,
  toApiError,
} from '@codi/api';
import { ParticipantRealtime, RealtimeStatus } from '../core/participant-realtime';
import { ParticipantService } from '../core/participant-service';
import { AttemptAutosave } from '../core/attempt-autosave';
import { WorkspacePreviewPublisher } from '../core/workspace-preview';
import { TrialRun } from '../core/trial-run';
import { QuizPanel } from './quiz-panel';
import { knowsEngine, findEngine } from '@exercise-engine/engine-registry';
import { GRID_PATH_EXERCISE_TYPE } from '@exercise-engine/grid-path-scene';
import { VALUE_PROGRAM_EXERCISE_TYPE } from '@exercise-engine/value-blocks';
import { ParticipantWorkspace } from './participant-workspace';
import { ValueProgramPanel } from './value-program-panel';
import { ChatPanel } from './chat-panel';
import { StatusBadge } from './status-badge';
import { sessionStatusBadges, type StatusBadgeSpec } from './status-badges';

/** Simbolo di ogni orientamento: comunica la direzione anche senza distinguere i colori (§20.3). */
const DIRECTION_ARROWS: Record<string, string> = { North: '↑', East: '→', South: '↓', West: '←' };

/** Nome italiano degli orientamenti, per le etichette ai lettori di schermo. */
const DIRECTION_LABELS: Record<string, string> = {
  North: 'Nord',
  East: 'Est',
  South: 'Sud',
  West: 'Ovest',
};

/** Cella della griglia vista dalla pagina; la forma autorevole è quella del motore. */
interface BoardCell {
  readonly x: number;
  readonly y: number;
}

/** Scena disegnata dalla pagina, letta dalla definizione dell'esercizio. */
interface Board {
  readonly width: number;
  readonly height: number;
  readonly start: BoardCell;
  readonly startDirection: string;
  readonly goal: BoardCell;
  readonly obstacles: readonly BoardCell[];
  readonly feedback: { readonly success: string; readonly collision: string };
}

/** Progetto dell'alunno, player Blockly nel worker e salvataggio automatico. */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, ParticipantWorkspace, QuizPanel, ValueProgramPanel, ChatPanel, StatusBadge],
  selector: 'app-participant-session',
  styleUrl: './session.css',
  templateUrl: './session.html',
})
export class ParticipantSession implements OnInit {
  protected readonly autosave = inject(AttemptAutosave);
  private readonly participants = inject(ParticipantService);
  private readonly realtime = inject(ParticipantRealtime);
  private readonly preview = inject(WorkspacePreviewPublisher);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  /** Prova locale del programma: vive nella pagina, non nel worker e non nel protocollo (§13.1). */
  protected readonly trial = inject(TrialRun);

  protected readonly context = signal<ParticipantContext | null>(null);
  protected readonly snapshot = signal<ParticipantSnapshot | null>(null);
  protected readonly exercise = signal<AssignedExercise | null>(null);
  protected readonly attempt = signal<Attempt | null>(null);
  protected readonly instructions = computed(() => {
    try {
      return JSON.parse(this.exercise()?.definitionJson ?? '{}').instructions ?? '';
    } catch {
      return '';
    }
  });
  protected readonly allowedBlocks = computed<readonly string[]>(() => {
    try {
      return JSON.parse(this.exercise()?.definitionJson ?? '{}').toolbox?.allowedBlocks ?? [];
    } catch {
      return [];
    }
  });

  /**
   * Dove si trova l'alunno (0.40): scuola, classe, lezione e — quando il docente ha assegnato
   * l'attività — l'esercizio. I livelli senza nome si saltano: il server li omette quando la riga
   * corrispondente non esiste più, e una voce vuota con il suo separatore confonderebbe soltanto.
   */
  protected readonly whereAmI = computed<readonly string[]>(() => {
    const context = this.context();

    return [
      context?.schoolName,
      context?.classroomName,
      context?.lessonTitle,
      this.exercise()?.exerciseTitle,
    ].filter((level): level is string => typeof level === 'string' && level.trim() !== '');
  });

  /** La scena serve solo a disegnare la griglia: l'esecuzione vive nel worker, non qui. */
  protected readonly board = computed<Board | null>(() => {
    // Un tipo di esercizio che questa build non sa svolgere non viene interpretato come se fosse
    // una griglia: il documento si lascia com'è e la pagina lo dice al bambino (§14.4).
    if (this.unsupportedType() !== '') return null;

    try {
      const scene = JSON.parse(this.exercise()?.definitionJson ?? '{}').scene;
      const width = Number(scene?.width);
      const height = Number(scene?.height);
      if (!Number.isInteger(width) || !Number.isInteger(height) || width < 1 || height < 1)
        return null;
      const feedback = JSON.parse(this.exercise()?.definitionJson ?? '{}').feedback ?? {};

      return {
        width,
        height,
        start: { x: Number(scene.start?.x ?? 0), y: Number(scene.start?.y ?? 0) },
        startDirection: typeof scene.start?.direction === 'string' ? scene.start.direction : 'East',
        goal: { x: Number(scene.goal?.x ?? 0), y: Number(scene.goal?.y ?? 0) },
        obstacles: Array.isArray(scene.obstacles)
          ? scene.obstacles.map((cell: { x?: unknown; y?: unknown }) => ({
              x: Number(cell?.x ?? -1),
              y: Number(cell?.y ?? -1),
            }))
          : [],
        feedback: {
          success: typeof feedback.success === 'string' ? feedback.success : 'Traguardo raggiunto!',
          collision:
            typeof feedback.collision === 'string'
              ? feedback.collision
              : 'C’è un ostacolo: prova a cambiare percorso.',
        },
      };
    } catch {
      return null;
    }
  });

  protected readonly rows = computed<readonly (readonly BoardCell[])[]>(() => {
    const board = this.board();
    if (board === null) return [];

    return Array.from({ length: board.height }, (_, y) =>
      Array.from({ length: board.width }, (_, x) => ({ x, y })),
    );
  });

  /** Messaggio dell'esito didattico dichiarato dal browser (§13.8, `ClientReported`). */
  protected readonly outcomeMessage = computed(() => {
    const outcome = this.runtime.outcome();
    const board = this.board();
    if (outcome === null || board === null) return '';

    switch (outcome.outcome) {
      case 'success':
        return board.feedback.success;
      case 'collision':
        return board.feedback.collision;
      case 'leftTheGrid':
        return 'Il personaggio esce dalla griglia: prova a cambiare percorso.';
      case 'budget':
        return 'Il programma ha esaurito i passi o il tempo a disposizione.';
      case 'finished':
        return 'Il programma è finito, ma il personaggio non ha raggiunto il traguardo.';
      case 'running':
        return '';
    }
  });
  protected readonly editingEnabled = computed(
    () =>
      this.realtime.authorized() &&
      this.permissions()?.interactionEnabled === true &&
      !this.permissions()?.teacherPaused &&
      !this.permissions()?.recoveryBlocked &&
      !this.autosave.conflict(),
  );

  /**
   * Riquadri di stato: collegamento, che cosa può fare adesso e che cosa fa il programma. Sono la
   * prima cosa che l'alunno vede, perché a otto anni «posso lavorare?» deve leggersi a colpo
   * d'occhio e non dentro un pannello con le parole del contratto (§14.4).
   */
  protected readonly badges = computed<readonly StatusBadgeSpec[]>(() => {
    const state = this.permissions();

    return sessionStatusBadges({
      connection: this.connection(),
      view: state?.view ?? null,
      interactionEnabled: state?.interactionEnabled ?? false,
      teacherPaused: state?.teacherPaused ?? false,
      recoveryBlocked: state?.recoveryBlocked ?? false,
      runtimeStatus: this.runtime.status(),
      trialRunning: this.trial.running(),
      showsProgram: this.showsProgram(),
    });
  });

  /** Il progetto su cui lavora la prova: quello che l'alunno ha davanti ora, non quello salvato. */
  private readonly currentWorkspace = computed(
    () => this.autosave.workspace() ?? this.attempt()?.workspaceJson ?? '',
  );

  /** Si può provare solo quando si può lavorare: la prova è parte del lavoro, non un'eccezione. */
  protected readonly canTry = computed(
    () =>
      this.editingEnabled() &&
      this.attempt() !== null &&
      this.board() !== null &&
      this.exercise() !== null &&
      this.unsupportedType() === '',
  );

  /**
   * Frase da mostrare quando l'esercizio assegnato è di un tipo o di una versione del motore che
   * questa build non sa svolgere. Vuota quando l'esercizio è svolgibile.
   *
   * Il caso è reale e va spiegato: il server distribuisce la versione fissata dall'assegnazione, e
   * un client non aggiornato riceverebbe altrimenti una pagina vuota senza dire perché. La frase è
   * per il bambino — breve e senza identificativi interni (§8) — e il docente trova il dettaglio
   * nel pannello di regia.
   */
  protected readonly unsupportedType = computed(() => {
    const exercise = this.exercise();
    if (exercise === null) return '';

    return knowsEngine(exercise.engineVersion)
      ? ''
      : 'Questo esercizio non si apre con questa versione del programma: chiedi al maestro.';
  });

  /**
   * Vero quando l'esercizio si svolge componendo un progetto a blocchi, falso per un esercizio
   * dichiarativo. Decide quali pannelli mostra la pagina: un tipo senza blocchi non ha un progetto
   * da comporre né un programma da provare, e mostrarglieli vuoti sarebbe peggio che non mostrarli.
   *
   * Un motore che questa build non conosce non è «senza blocchi»: è sconosciuto, e la pagina lo dice
   * invece di disegnare il pannello di un tipo che non sa svolgere.
   */
  protected readonly usesBlocks = computed(
    () =>
      this.unsupportedType() === '' &&
      (findEngine(this.exercise()?.engineVersion)?.blockly ?? false),
  );

  /**
   * Vero quando l'esercizio si svolge su una **griglia**: il pannello del programma mostra la scena,
   * la prova e i comandi. È il caso più stretto di `usesBlocks`, perché un esercizio che conserva
   * valori compone un progetto a blocchi ma non ha una griglia da disegnare: mostrargli una tabella
   * vuota sarebbe peggio che non mostrarla.
   */
  protected readonly usesGrid = computed(
    () =>
      this.unsupportedType() === '' && this.exercise()?.exerciseType === GRID_PATH_EXERCISE_TYPE,
  );

  /**
   * Vero quando l'esercizio **conserva valori**: il pannello mostra le variabili con il loro valore,
   * l'uscita e il campo con cui l'alunno fornisce un valore quando il programma lo chiede.
   */
  protected readonly usesValues = computed(
    () =>
      this.unsupportedType() === '' &&
      this.exercise()?.exerciseType === VALUE_PROGRAM_EXERCISE_TYPE,
  );

  /**
   * Vero quando l'esercizio è quello **dichiarativo**: legge una rappresentazione, risponde, riceve
   * un esito. È il ramo «altrimenti» dei pannelli, e per questo si dichiara invece di dedurlo: la
   * catena dei rami di un template non ha un modo per dire «tutto il resto» senza negare i casi
   * precedenti, e negarli a mano è il modo in cui un tipo nuovo finirebbe nel ramo sbagliato.
   */
  protected readonly showsQuiz = computed(
    () =>
      this.unsupportedType() === '' &&
      this.exercise() !== null &&
      !this.usesBlocks() &&
      !this.usesValues(),
  );

  /**
   * Vero quando la pagina mostra il riquadro del **programma del player**. Solo la griglia ha un
   * programma eseguito dal worker: un esercizio dichiarativo risponde e basta, uno che conserva
   * valori ha il proprio pannello e la prova locale, quindi il riquadro del worker non direbbe nulla
   * di vero. Non dipende dal tentativo: in attesa non c'è ancora un esercizio e il riquadro resta il
   * posto in cui l'alunno legge che cosa sta succedendo.
   */
  protected readonly showsProgram = computed(() => this.exercise() === null || this.usesGrid());

  /**
   * Vero quando la pagina mostra il pannello della **griglia** — stato, Prova, scena. Non dipende dal
   * tentativo, perché in attesa non c'è ancora un esercizio e il pannello resta il posto in cui
   * l'alunno legge che cosa sta succedendo.
   *
   * Un esercizio che conserva valori non ha una griglia: ha il pannello delle variabili e dell'uscita
   * e una prova locale che vive nel proprio pannello, quindi i comandi della griglia non compaiono.
   */
  protected readonly showsGridProgram = computed(() => this.exercise() === null || this.usesGrid());

  /** Problema da mostrare sotto la scena: la prova, che è dell'alunno, viene prima del player. */
  protected readonly sceneProblem = computed(() => this.trial.problem() || this.runtime.problem());

  /** Esito da mostrare sotto la scena: quello della prova se è in corso, altrimenti quello comandato. */
  protected readonly sceneMessage = computed(() =>
    this.trial.active() ? this.trial.message() : this.outcomeMessage(),
  );
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  /** Stato e avvisi del collegamento: il trasporto aperto non basta, conta l'operatività. */
  protected readonly connection = this.realtime.status;
  protected readonly notice = this.realtime.notice;
  protected readonly leaseValidUntilUtc = this.realtime.leaseValidUntilUtc;
  protected readonly lastCommand = this.realtime.lastCommand;
  protected readonly runtime = this.realtime.runtime;
  private workLoadGeneration = 0;
  private pendingWork: { attemptId: string; promise: Promise<void> } | null = null;

  /**
   * Permessi correnti: quelli consegnati dal collegamento, o quelli letti dal server se il
   * collegamento non ha ancora ricevuto uno stato. Il realtime è la sorgente più fresca.
   */
  protected readonly permissions = computed<DesiredState | null>(
    () => this.realtime.desiredState() ?? this.snapshot()?.desiredState ?? null,
  );

  /** Nessuna credenziale valida: si torna all'ingresso invece di mostrare una pagina vuota. */
  protected readonly needsJoin = signal(false);

  constructor() {
    effect(() => {
      this.autosave.setOperative(this.realtime.authorized());
    });
    // L'anteprima segue l'operatività del collegamento, non il permesso di modificare: il docente
    // guarda i blocchi soprattutto durante una pausa (§15.5).
    effect(() => {
      this.preview.setOperative(this.realtime.status() === 'connected');
    });
    effect(() => {
      const receipt = this.autosave.saved();
      if (receipt !== null && receipt.attemptId === this.attempt()?.attemptId) {
        this.realtime.setWorkspaceHash(receipt.workspaceHash);
        this.realtime.setPersistedRevision(receipt.projectRevision);
        // Il progetto persistito ha ora la stessa impronta dell'anteprima: si rimanda perché il
        // docente la veda come completa, non come parziale.
        const workspace = this.autosave.workspace();
        if (workspace !== null) untracked(() => this.preview.publishNow(workspace, true));
      }
    });
    effect(() => {
      // Ogni checkpoint prodotto dal player diventa un salvataggio: la pagina compone l'involucro
      // con tentativo, generazione e progetto, che il worker non conosce.
      const notice = this.runtime.checkpoint();
      const attempt = this.attempt();
      if (notice === null || attempt === null) return;
      untracked(() =>
        this.autosave.checkpoint({
          schemaVersion: 1,
          engineVersion: notice.checkpoint.engineVersion,
          attemptId: attempt.attemptId,
          exerciseVersionId: attempt.exerciseVersionId,
          executionGeneration: this.permissions()?.executionGeneration ?? '0',
          runId: notice.runId,
          workspaceHash: attempt.workspaceHash,
          projectRevision: attempt.projectRevision,
          runtimeStatus: notice.runtimeStatus,
          runtimeState: { ...notice.checkpoint },
        }),
      );
    });
    // Un comando consegnato può cambiare il lavoro: quando l'identificativo del tentativo cambia,
    // definizione e progetto si rileggono. Le scritture stanno in `untracked`, altrimenti l'effetto
    // si riattiverebbe da solo leggendo i segnali che scrive.
    effect(() => {
      const desired = this.realtime.desiredState();
      if (desired === null) {
        return;
      }

      untracked(() => {
        void this.syncWork(desired).catch(() => undefined);
      });
    });

    // La prova è dell'alunno e vive finché il maestro lascia lavorare: quando i controlli si
    // chiudono, o quando arriva un'esecuzione comandata, l'animazione locale si ferma e la scena
    // torna a quella del player. La posizione raggiunta resta: si smette di muoverla, non si cancella.
    effect(() => {
      const commanded = this.runtime.status() === 'Running';
      const allowed = this.editingEnabled();
      if (commanded || !allowed) {
        untracked(() => this.trial.stop());
      }
    });

    inject(DestroyRef).onDestroy(() => {
      this.autosave.dispose();
      this.preview.dispose();
      void this.realtime.stop();
      this.runtime.dispose();
      this.trial.reset();
    });
  }

  ngOnInit(): void {
    this.realtime.setStateApplier((state) => this.syncWork(state));
    void this.autosave.resumeFinalizations();
    void this.load();
    void this.realtime.start();
  }

  protected async reload(): Promise<void> {
    await this.load();
  }

  /**
   * Rilegge contesto e stato corrente dal server. È il percorso del primo caricamento e di
   * «Ricarica»; durante la lezione lo stato arriva dal collegamento.
   */
  private async load(): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set('');

    try {
      const context = await this.participants.context();
      this.context.set(context);
      this.needsJoin.set(false);

      const snapshot = await this.api.executeQuery<ParticipantSnapshot>(
        'GetParticipantSnapshot',
        {},
      );
      this.snapshot.set(snapshot);

      // La rilettura non si risparmia: è il punto in cui l'alunno chiede esplicitamente lo stato, e
      // il tentativo può essere cambiato senza che il collegamento sia ancora arrivato.
      await this.syncWork(this.realtime.desiredState() ?? snapshot.desiredState, true);
    } catch (failure) {
      const failure_ = toApiError(failure);
      this.context.set(null);
      this.snapshot.set(null);

      if (failure_.status === 401) {
        this.needsJoin.set(true);
      } else {
        this.error.set(failure_.message);
      }
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * Porta la pagina in linea con lo stato desiderato: se c'è un esercizio carica la consegna e il
   * progetto, altrimenti li toglie. Il lavoro si chiede solo quando ha oggetto — in attesa o in
   * chiusura non c'è nulla da mostrare — e non si richiede se è già quello in memoria.
   */
  private syncWork(desired: DesiredState, force = false): Promise<void> {
    const old = this.attempt();
    if (old !== null && (desired.attemptId !== old.attemptId || desired.view === 'Goodbye')) {
      // Il lavoro precedente si chiude: gli ultimi checkpoint sono già stati emessi dal player.
      void this.autosave.finalize(old.attemptId);
    }
    const handshake = this.realtime.currentSnapshot();
    if (handshake !== null && handshake.connectionEpoch !== this.snapshot()?.connectionEpoch) {
      this.snapshot.set(handshake);
      force = true;
    }
    if (desired.view !== 'Exercise' || desired.attemptId === null) {
      ++this.workLoadGeneration;
      this.pendingWork = null;
      this.exercise.set(null);
      this.attempt.set(null);
      this.busy.set(false);
      this.realtime.setWorkspaceHash(null);
      return Promise.resolve();
    }
    if (this.pendingWork?.attemptId === desired.attemptId) return this.pendingWork.promise;
    if (!force && this.attempt()?.attemptId === desired.attemptId) return Promise.resolve();
    const generation = ++this.workLoadGeneration;
    const promise = this.loadWork(desired, generation);
    const pending = { attemptId: desired.attemptId, promise };
    this.pendingWork = pending;
    void promise
      .finally(() => {
        if (this.pendingWork === pending) this.pendingWork = null;
      })
      .catch(() => undefined);
    return promise;
  }

  private async loadWork(desired: DesiredState, generation: number): Promise<void> {
    this.busy.set(true);

    try {
      const [exercise, attempt] = await Promise.all([
        this.api.executeQuery<AssignedExercise>('GetAssignedExercise', {}),
        this.api.executeQuery<Attempt>('GetAttempt', {}),
      ]);
      if (generation !== this.workLoadGeneration)
        throw new Error('Il caricamento appartiene a un tentativo precedente.');
      if (attempt.attemptId !== desired.attemptId || exercise.attemptId !== desired.attemptId)
        throw new Error('Il lavoro è cambiato durante il caricamento. Attendi il nuovo stato.');
      this.exercise.set(exercise);
      this.attempt.set(attempt);
      const verified = this.realtime.currentSnapshot() ?? this.snapshot();
      if (verified !== null) await this.autosave.attach(verified, attempt);
      const executionGeneration = desired.executionGeneration ?? '0';
      this.runtime.loadWork({
        attemptId: attempt.attemptId,
        executionGeneration,
        definitionJson: exercise.definitionJson,
        workspaceJson: attempt.workspaceJson,
        projectRevision: attempt.projectRevision,
        workspaceHash: attempt.workspaceHash,
      });
      this.runtime.restoreCheckpoint(attempt, executionGeneration);
      this.realtime.setWorkspaceHash(attempt.workspaceHash);
      this.realtime.setPersistedRevision(attempt.projectRevision);
      this.preview.attach(attempt.attemptId, attempt.projectRevision);
      this.preview.publishNow(attempt.workspaceJson);
    } catch (failure) {
      if (generation === this.workLoadGeneration) this.error.set(toApiError(failure).message);
      throw failure;
    } finally {
      if (generation === this.workLoadGeneration) this.busy.set(false);
    }
  }

  protected workspaceChanged(json: string): void {
    if (!this.editingEnabled()) return;
    // Un progetto modificato invalida il checkpoint: il player ricompila e la corsa torna pronta.
    this.runtime.projectChanged(json);
    this.autosave.change(json);
    this.preview.change(json);
    // Anche la prova appartiene al progetto con cui è cominciata: cambiarlo la riporta alla partenza,
    // invece di mostrare la posizione di un programma che non esiste più.
    this.trial.reset();
  }

  /**
   * Risposta confermata su un esercizio dichiarativo. Il salvataggio lo ha già fatto il pannello:
   * qui si rilegge il lavoro, perché la revisione e la versione di riga della pagina devono seguire
   * quella appena scritta, altrimenti la conferma successiva arriverebbe con una base superata.
   */
  protected async answerConfirmed(): Promise<void> {
    const desired = this.realtime.desiredState() ?? this.snapshot()?.desiredState ?? null;

    if (desired === null) {
      return;
    }

    await this.syncWork(desired, true);
  }

  /** Prova il programma: il personaggio si muove sulla scena, senza toccare il lavoro comandato. */
  protected tryProgram(): void {
    const exercise = this.exercise();
    if (exercise === null || !this.canTry()) return;

    this.trial.play(exercise.definitionJson, this.currentWorkspace());
  }

  /** Un passo alla volta: serve a seguire il programma senza fretta. */
  protected stepProgram(): void {
    const exercise = this.exercise();
    if (exercise === null || !this.canTry()) return;

    this.trial.step(exercise.definitionJson, this.currentWorkspace());
  }

  /** Ferma lo scorrimento: la posizione raggiunta resta visibile. */
  protected stopTrial(): void {
    this.trial.stop();
  }

  /** Riporta la scena alla partenza e dimentica la prova. */
  protected resetTrial(): void {
    this.trial.reset();
  }

  /** Classi della cella: stato della scena ed eventuale presenza del personaggio. */
  protected cellClasses(cell: BoardCell): string {
    const classes = ['cell', `cell-${this.cellState(cell)}`];
    if (this.isCharacter(cell)) classes.push('cell-character');

    return classes.join(' ');
  }

  /**
   * Personaggio disegnato: quello della **prova** locale quando è in corso, altrimenti quello del
   * player comandato dal maestro. La scena è una sola: mostrarne due confonderebbe il bambino.
   */
  private readonly character = computed<{ position: BoardCell; direction: string } | null>(() => {
    if (this.trial.active()) {
      const position = this.trial.position();

      return position === null ? null : { position, direction: this.trial.direction() };
    }

    const scene = this.runtime.scene();

    return scene === null ? null : { position: scene.position, direction: scene.direction };
  });

  /** Simbolo visibile: comunica lo stato anche senza distinguere i colori (§20.3). */
  protected cellSymbol(cell: BoardCell): string {
    const character = this.character();
    if (character !== null && character.position.x === cell.x && character.position.y === cell.y) {
      return DIRECTION_ARROWS[character.direction] ?? DIRECTION_ARROWS['East'];
    }

    switch (this.cellState(cell)) {
      case 'start':
        return '•';
      case 'goal':
        return '★';
      case 'obstacle':
        return '■';
      default:
        return '';
    }
  }

  /** Etichetta per i lettori di schermo: posizione, contenuto e personaggio in parole. */
  protected cellLabel(cell: BoardCell): string {
    const where = `Riga ${String(cell.y + 1)}, colonna ${String(cell.x + 1)}`;
    const board = this.board();
    const state = this.cellState(cell);
    const content =
      state === 'start'
        ? `partenza, orientamento ${DIRECTION_LABELS[board?.startDirection ?? 'East'] ?? ''}`
        : state === 'goal'
          ? 'traguardo'
          : state === 'obstacle'
            ? 'ostacolo'
            : 'libera';
    const character = this.character();
    const direction = character?.direction ?? board?.startDirection ?? 'East';
    const isCharacter =
      character !== null && character.position.x === cell.x && character.position.y === cell.y;
    const who = isCharacter ? `, personaggio rivolto a ${DIRECTION_LABELS[direction] ?? ''}` : '';

    return `${where}: ${content}${who}`;
  }

  private isCharacter(cell: BoardCell): boolean {
    const character = this.character();

    return character !== null && character.position.x === cell.x && character.position.y === cell.y;
  }

  private cellState(cell: BoardCell): 'start' | 'goal' | 'obstacle' | 'free' {
    const board = this.board();
    if (board === null) return 'free';
    if (board.start.x === cell.x && board.start.y === cell.y) return 'start';
    if (board.goal.x === cell.x && board.goal.y === cell.y) return 'goal';
    if (board.obstacles.some((other) => other.x === cell.x && other.y === cell.y))
      return 'obstacle';

    return 'free';
  }

  /** Uscita volontaria: chiude il collegamento, revoca la credenziale e riporta all'ingresso. */
  protected async leave(): Promise<void> {
    this.busy.set(true);

    try {
      await this.autosave.flush();
      await this.realtime.stop();
      await this.participants.leave();
    } catch (failure) {
      this.error.set(toApiError(failure).message);
    } finally {
      this.busy.set(false);
      await this.router.navigateByUrl('/join');
    }
  }

  protected runtimeLabel(status: string): string {
    return (
      (
        {
          Ready: 'pronto',
          Running: 'avviato',
          Paused: 'in pausa',
          Completed: 'completato',
          Stopped: 'concluso',
          Error: 'non eseguibile',
        } as Record<string, string>
      )[status] ?? 'pronto'
    );
  }

  /** Etichetta dello stato del collegamento: si legge a schermo, non è un codice. */
  protected connectionLabel(status: RealtimeStatus): string {
    return ConnectionLabels[status];
  }
}

/** Traduzione degli stati del collegamento, in un posto solo. */
const ConnectionLabels: Record<RealtimeStatus, string> = {
  idle: 'non avviato',
  connecting: 'collegamento in corso…',
  connected: 'collegato',
  reconnecting: 'ricollegamento…',
  waiting: 'in attesa di un’altra pagina',
  superseded: 'sostituito da un’altra pagina',
  blocked: 'collegamento interrotto',
  revoked: 'partecipazione revocata',
  ended: 'lezione conclusa',
};
