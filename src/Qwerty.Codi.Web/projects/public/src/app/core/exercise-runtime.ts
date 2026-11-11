import { Injectable, InjectionToken, inject, signal } from '@angular/core';
import type { Attempt, AttemptCheckpoint, DesiredState } from '@codi/api';
import {
  isGridPathCheckpointState,
  type GridPathCheckpointState,
  type GridPathOutcome,
} from '@exercise-engine/grid-path-checkpoint';
import { GRID_PATH_ENGINE_VERSION } from '@exercise-engine/grid-path-scene';
import type {
  PlayerEvent,
  PlayerRequest,
  PlayerRuntimeStatus,
  PlayerScene,
} from '@exercise-engine/player-protocol';

/**
 * Canale verso il worker del player. Il servizio non conosce il browser: la fabbrica predefinita
 * costruisce un worker vero, i test ne forniscono una che restituisce un doppio. È l'unico punto in
 * cui `new Worker` compare, così la logica del player resta provabile senza un browser.
 */
export interface PlayerWorker {
  postMessage(message: PlayerRequest): void;
  set onmessage(handler: ((event: MessageEvent<PlayerEvent>) => void) | null);
  terminate(): void;
}

export type PlayerWorkerFactory = () => PlayerWorker;

export const PLAYER_WORKER_FACTORY = new InjectionToken<PlayerWorkerFactory>(
  'PLAYER_WORKER_FACTORY',
  {
    providedIn: 'root',
    factory: () => () =>
      new Worker(new URL('./player.worker', import.meta.url), {
        type: 'module',
      }) as unknown as PlayerWorker,
  },
);

/** Lavoro caricato nel player: la definizione dell'esercizio e il progetto dell'alunno. */
export interface LoadedWork {
  readonly attemptId: string;
  readonly executionGeneration: string;
  readonly definitionJson: string;
  readonly workspaceJson: string;
  readonly projectRevision: string;
  readonly workspaceHash: string;
}

/** Checkpoint prodotto dal player: l'involucro è composto dalla pagina che lo persiste. */
export interface RuntimeCheckpoint {
  readonly checkpoint: GridPathCheckpointState;
  readonly runtimeStatus: PlayerRuntimeStatus;
  readonly runId: string;
}

/** Esito dichiarato dal browser: `ClientReported` finché non esiste una verifica sul server (§13.8). */
export interface RuntimeOutcome {
  readonly outcome: GridPathOutcome;
  readonly executions: number;
  readonly consumed: number;
  readonly clientReported: true;
}

/**
 * Esito dell'applicazione di un comando alle risorse del player. `recoveryRequired` corrisponde
 * all'ACK `AppliedWithRecoveryRequired` di §12.2: l'attività è ferma, ma la ripresa dall'esatta
 * istruzione non è garantita.
 */
export type ApplicationResult = 'applied' | 'recoveryRequired';

/** Oltre questa soglia il worker non è più affidabile e il player si blocca (§13.7). */
const PAUSE_CONFIRMATION_TIMEOUT_MS = 500;

/**
 * Player dell'alunno (§13.6, §14.4): governa il worker, traduce lo stato desiderato in messaggi e
 * pubblica ciò che il worker osserva. La pagina disegna la scena e mostra l'esito; il motore esegue
 * nel worker e produce i checkpoint.
 *
 * Il servizio resta la sola autorità su **che cosa** inviare al worker: la pagina non conosce il
 * protocollo. Le decisioni che dipendono dal tempo — permesso, budget, passo — sono della macchina
 * nel worker; qui si decide soltanto quando avviare, sospendere e riprendere, seguendo lo stato
 * desiderato del server.
 */
@Injectable({ providedIn: 'root' })
export class ExerciseRuntime {
  private readonly createWorker = inject(PLAYER_WORKER_FACTORY);
  private readonly workerInstanceId = newId();
  private worker: PlayerWorker | null = null;

  private attemptId: string | null = null;
  private generation = '0';
  private loaded: LoadedWork | null = null;
  private pendingResume: GridPathCheckpointState | null = null;
  private resumeRunning = false;
  private permitId = 0;
  private reloadTimer: ReturnType<typeof setTimeout> | null = null;
  private pauseConfirmation: {
    readonly resolve: (confirmed: boolean) => void;
    readonly timer: ReturnType<typeof setTimeout>;
  } | null = null;
  private readonly consumed = new Set<string>();

  readonly status = signal<PlayerRuntimeStatus>('Ready');
  readonly scene = signal<PlayerScene | null>(null);
  readonly runId = signal<string | null>(null);
  readonly problem = signal('');
  readonly checkpoint = signal<RuntimeCheckpoint | null>(null);
  readonly outcome = signal<RuntimeOutcome | null>(null);

  /**
   * Carica definizione e progetto nel worker. Si ripete quando cambia il lavoro o il progetto: il
   * worker ricompila e la corsa torna `Ready`, perché un progetto diverso non è più quello del
   * checkpoint precedente (§13.7).
   */
  loadWork(work: LoadedWork): void {
    this.loaded = work;
    this.attemptId = work.attemptId;
    this.generation = work.executionGeneration;
    this.pendingResume = null;
    this.resumeRunning = false;
    this.problem.set('');
    this.checkpoint.set(null);
    this.outcome.set(null);
    this.sendLoad();
  }

  /**
   * Progetto modificato: il motore ricompila il prima possibile — con un breve ritardo, perché
   * durante la composizione i cambiamenti si susseguono — e la corsa invalidata riparte da `Ready`.
   */
  projectChanged(workspaceJson: string): void {
    if (this.loaded === null) return;
    this.loaded = { ...this.loaded, workspaceJson };
    this.pendingResume = null;
    this.resumeRunning = false;
    if (this.reloadTimer !== null) clearTimeout(this.reloadTimer);
    this.reloadTimer = setTimeout(() => {
      this.reloadTimer = null;
      this.sendLoad();
    }, 400);
  }

  /**
   * Riprende il checkpoint persistito se appartiene a questo tentativo, a questa generazione e allo
   * stesso progetto. Non lo applica da sé: segnala che una ripresa è disponibile e che al prossimo
   * avvio esplicito si partirà da lì, non dall'inizio (§6.2, D-06).
   */
  restoreCheckpoint(attempt: Attempt, generation: string): boolean {
    const raw = attempt.checkpointJson;
    if (raw === null || attempt.checkpointEngineVersion !== GRID_PATH_ENGINE_VERSION) return false;

    let envelope: AttemptCheckpoint;
    try {
      envelope = JSON.parse(raw) as AttemptCheckpoint;
    } catch {
      return false;
    }

    if (
      envelope.schemaVersion !== 1 ||
      envelope.engineVersion !== GRID_PATH_ENGINE_VERSION ||
      envelope.attemptId !== attempt.attemptId ||
      envelope.exerciseVersionId !== attempt.exerciseVersionId ||
      envelope.executionGeneration !== generation ||
      envelope.workspaceHash !== attempt.workspaceHash ||
      envelope.projectRevision !== attempt.projectRevision ||
      !['Running', 'Paused', 'Completed'].includes(envelope.runtimeStatus)
    )
      return false;

    const checkpoint = envelope.runtimeState;
    if (!isGridPathCheckpointState(checkpoint)) return false;

    this.pendingResume = checkpoint;
    this.resumeRunning = true;

    return true;
  }

  /**
   * Applica lo stato ricevuto all'handshake. Restituisce vero quando l'esito non è certo — una
   * direttiva non ancora consumata o una corsa ripresa dopo un refresh — così la pagina materializza
   * la pausa di recupero invece di rieseguire da sé un vecchio avvio (§12.6, §16).
   */
  async initialize(state: DesiredState): Promise<boolean> {
    const uncertain =
      this.resumeRunning ||
      (state.executionDirective !== null &&
        !this.consumed.has(state.executionDirective.directiveId));
    await this.apply(state, uncertain);

    return uncertain;
  }

  /**
   * Applica lo stato desiderato. Una sospensione di una corsa in esecuzione attende la conferma del
   * worker: se non arriva entro la soglia, il player si blocca e il risultato diventa
   * `recoveryRequired`, che il trasporto traduce in `AppliedWithRecoveryRequired` (§13.7).
   */
  async apply(state: DesiredState, uncertain = false): Promise<ApplicationResult> {
    if (state.attemptId !== this.attemptId) {
      this.attemptId = state.attemptId;
      this.reset();
    }

    this.generation = state.executionGeneration ?? this.generation;

    if (state.view === 'Goodbye') {
      this.stop();

      return 'applied';
    }

    if (
      uncertain ||
      state.teacherPaused ||
      state.recoveryBlocked ||
      state.view !== 'Exercise' ||
      !state.interactionEnabled
    ) {
      return this.suspendAndConfirm(uncertain ? 'Recovery' : 'Suspended');
    }

    const directive = state.executionDirective;
    if (directive !== null && !this.consumed.has(directive.directiveId)) {
      this.remember(directive.directiveId);
      if (directive.kind === 'Restart') this.restart();
      else this.run();

      return 'applied';
    }

    if (this.resumeRunning) this.run();

    return 'applied';
  }

  /** Rinnova il permesso operativo verso il worker. Un permesso scaduto è un permesso assente. */
  tick(permitted: boolean, validForMs: number): void {
    this.post({
      type: 'UpdatePermit',
      permitId: ++this.permitId,
      permitted,
      validForMs: permitted ? Math.max(0, validForMs) : 0,
    });
  }

  /**
   * Revoca il permesso operativo. La corsa si sospende da sé al prossimo passo e riprenderà quando
   * arriverà un permesso nuovo: è una sospensione **transitoria**, non una pausa del docente
   * (§11.5). Per fermare la corsa in modo deliberato si applica uno stato che sospende (§12.6).
   */
  revokePermit(): void {
    this.post({ type: 'UpdatePermit', permitId: ++this.permitId, permitted: false, validForMs: 0 });
  }

  /**
   * Sospende una corsa in esecuzione e attende la conferma del worker (§13.7). La pausa non
   * interrompe un passo a metà: il worker risponde al prossimo confine, con un checkpoint. Se non
   * risponde entro la soglia, il player si blocca e la ripresa esatta non è più garantita.
   */
  private async suspendAndConfirm(reason: string): Promise<ApplicationResult> {
    if (this.status() !== 'Running') return 'applied';
    this.resumeRunning = true;
    const confirmation = this.awaitPauseConfirmation();
    this.post({ type: 'PauseRun', reason });
    if (await confirmation) return 'applied';

    return this.abandonPause();
  }

  private awaitPauseConfirmation(): Promise<boolean> {
    // Un'attesa precedente, se ancora aperta, si considera conclusa: ne resta una sola per volta.
    this.settlePauseConfirmation(true);

    return new Promise<boolean>((resolve) => {
      const timer = setTimeout(
        () => this.settlePauseConfirmation(false),
        PAUSE_CONFIRMATION_TIMEOUT_MS,
      );
      this.pauseConfirmation = { resolve, timer };
    });
  }

  private settlePauseConfirmation(confirmed: boolean): void {
    const pending = this.pauseConfirmation;
    if (pending === null) return;
    this.pauseConfirmation = null;
    clearTimeout(pending.timer);
    pending.resolve(confirmed);
  }

  /**
   * Oltre la soglia il worker non è più affidabile: lo si termina, il player si blocca e si tiene
   * l'ultimo checkpoint noto, se c'è, per una ripresa approssimativa che il docente sceglie. Non si
   * promette la ripresa dall'esatta istruzione perché nessuno l'ha confermata (§13.7).
   */
  private abandonPause(): ApplicationResult {
    this.worker?.terminate();
    this.worker = null;
    this.status.set('Paused');
    this.problem.set(
      'La pausa non è stata confermata dal motore: l’attività è ferma. Il docente può riprendere o riavviare.',
    );
    this.pendingResume = this.checkpoint()?.checkpoint ?? null;
    this.resumeRunning = true;

    return 'recoveryRequired';
  }

  /** Conclude la corsa senza cancellare il progetto. */
  stop(): void {
    this.pendingResume = null;
    this.resumeRunning = false;
    // Lo stato terminale si dichiara subito: la vista «Goodbye» non torna indietro.
    this.status.set('Stopped');
    this.post({ type: 'StopRun' });
  }

  /** Smaltisce il worker alla distruzione della vista (§14.2). */
  dispose(): void {
    if (this.reloadTimer !== null) clearTimeout(this.reloadTimer);
    this.reloadTimer = null;
    this.settlePauseConfirmation(false);
    this.worker?.terminate();
    this.worker = null;
  }

  private run(): void {
    if (this.status() === 'Running') return;
    // Un checkpoint in sospeso è la posizione da cui riprendere: si riparte da lì, non da capo.
    const checkpoint = this.pendingResume;
    this.pendingResume = null;
    this.resumeRunning = false;
    this.status.set('Running');
    const runId = this.nextRunId();
    if (checkpoint !== null) {
      this.post({ type: 'ResumeRun', runId, checkpoint });
    } else {
      this.post({ type: 'StartRun', runId });
    }
  }

  private restart(): void {
    this.pendingResume = null;
    this.resumeRunning = false;
    this.status.set('Running');
    this.post({ type: 'StartRun', runId: this.nextRunId() });
  }

  private nextRunId(): string {
    const runId = newId();
    this.runId.set(runId);

    return runId;
  }

  private reset(): void {
    this.pendingResume = null;
    this.resumeRunning = false;
    // Il lavoro precedente non appartiene più a questo tentativo: la pagina lo ricarica.
    this.loaded = null;
    this.status.set('Ready');
    this.scene.set(null);
    this.runId.set(null);
    this.problem.set('');
    this.checkpoint.set(null);
    this.outcome.set(null);
  }

  private sendLoad(): void {
    const work = this.loaded;
    if (work === null) return;
    if (this.worker === null) {
      // Un worker appena creato riceve il programma qui, così non gli si inviano comandi per un
      // progetto che non conosce.
      this.workerFor();

      return;
    }

    this.worker.postMessage({
      ...this.envelope(),
      type: 'LoadProgram',
      workspaceJson: work.workspaceJson,
      definitionJson: work.definitionJson,
      projectRevision: work.projectRevision,
      workspaceHash: work.workspaceHash,
    });
  }

  private post(
    message:
      | { type: 'StartRun'; runId: string }
      | { type: 'PauseRun'; reason: string }
      | { type: 'ResumeRun'; runId: string; checkpoint: GridPathCheckpointState }
      | { type: 'StopRun' }
      | { type: 'UpdatePermit'; permitId: number; permitted: boolean; validForMs: number },
  ): void {
    if (this.worker === null && this.loaded === null) return;
    this.workerFor().postMessage({ ...this.envelope(), ...message });
  }

  private envelope(): Pick<
    PlayerRequest,
    'protocolVersion' | 'workerInstanceId' | 'attemptId' | 'executionGeneration' | 'runId'
  > {
    return {
      protocolVersion: 1,
      workerInstanceId: this.workerInstanceId,
      attemptId: this.attemptId ?? '',
      executionGeneration: this.generation,
      runId: this.runId(),
    };
  }

  private workerFor(): PlayerWorker {
    if (this.worker === null) {
      const worker = this.createWorker();
      worker.onmessage = (event) => this.receive(event.data);
      this.worker = worker;
      // Un worker nuovo non conosce il programma: glielo si manda subito, così un player ripreso
      // dopo una terminazione non resta senza progetto (§13.7).
      const work = this.loaded;
      if (work !== null) this.sendLoad();
    }

    return this.worker;
  }

  private receive(event: PlayerEvent): void {
    // Un messaggio di un'altra istanza o di un altro tentativo non descrive questa corsa.
    if (event.workerInstanceId !== this.workerInstanceId) return;
    if (event.attemptId !== this.attemptId) return;
    if (event.executionGeneration !== this.generation) return;

    switch (event.type) {
      case 'RuntimeState':
        this.status.set(event.runtimeStatus);
        this.scene.set(event.scene);
        this.problem.set('');
        if (event.runtimeStatus === 'Paused') this.settlePauseConfirmation(true);
        break;
      case 'CheckpointCreated':
        if (event.runId !== null) {
          this.checkpoint.set({
            checkpoint: event.checkpoint,
            runtimeStatus: event.runtimeStatus,
            runId: event.runId,
          });
          if (event.runtimeStatus === 'Paused') {
            this.pendingResume = event.checkpoint;
            this.settlePauseConfirmation(true);
          }
        }
        break;
      case 'RunFinished':
        this.outcome.set({
          outcome: event.outcome,
          executions: event.executions,
          consumed: event.consumed,
          clientReported: true,
        });
        break;
      case 'RuntimeError':
        this.problem.set(event.message);
        this.status.set('Error');
        break;
    }
  }

  private remember(directiveId: string): void {
    this.consumed.add(directiveId);
    if (this.consumed.size > 128) this.consumed.delete(this.consumed.values().next().value!);
  }
}

/** Identificativo nuovo, con una via di riserva per gli ambienti che non hanno `randomUUID`. */
function newId(): string {
  return typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : `player-${String(Date.now())}-${String(Math.random()).slice(2)}`;
}
