import {
  compileGridPathProgram,
  GridPathProgramError,
  type GridPathProgram,
} from '@exercise-engine/grid-path-program';
import {
  createGridPathRun,
  resumeGridPathRun,
  type GridPathRun,
  type GridPathRunState,
} from '@exercise-engine/grid-path-runner';
import { GridPathCheckpointError } from '@exercise-engine/grid-path-checkpoint';
import {
  GridPathDefinitionError,
  parseGridPathDefinition,
  type GridPathDefinition,
} from '@exercise-engine/grid-path-definition';
import type { GridCell, GridDirection } from '@exercise-engine/grid-path-scene';
import {
  PlayerErrorCode,
  PLAYER_PROTOCOL_VERSION,
  type LoadProgramRequest,
  type PlayerEvent,
  type PlayerRequest,
  type PlayerRuntimeStatus,
  type PlayerScene,
} from '@exercise-engine/player-protocol';

/**
 * Macchina del player (§13.6): traduce i messaggi della pagina in una corsa e produce gli eventi in
 * uscita. Vive identica nel worker — dove il worker le dà il proprio `postMessage` — e nei test —
 * dove le si dà una funzione che raccoglie gli eventi — così il protocollo si prova senza un browser
 * e il worker resta un adattatore sottile.
 *
 * Regole che valgono qui e non nel trasporto:
 * - il permesso è verificato **prima di ogni passo**, con scadenza monotona calcolata alla ricezione;
 * - un `UpdatePermit` più vecchio di uno già ricevuto non prolunga la validità (§13.6);
 * - l'identità dell'istanza non la sceglie il worker: **la adotta dal primo `LoadProgram`** della
 *   pagina, così i suoi eventi la ripetono e la pagina può ignorare un worker sostituito. Le
 *   richieste di un'altra pagina, o di un'altra generazione, vengono ignorate;
 * - la corsa avanza di **una sola operazione visibile** per `pump()`, che il driver richiama a
 *   intervalli didattici: il tempo di animazione non cambia il risultato logico (§13.6).
 */
export interface GridPathPlayerOptions {
  /**
   * Identificativo dell'istanza del worker. Il worker lo riceve dal primo `LoadProgram` della
   * pagina: se non è dichiarato qui, resta vuoto finché la pagina non si presenta.
   */
  readonly workerInstanceId?: string;
  /** Orologio monotono; iniettabile perché il budget e il permesso non si provano aspettando. */
  readonly clock?: () => number;
  /** Passi visibili fra due checkpoint automatici. */
  readonly checkpointEvery?: number;
}

export class GridPathPlayerRuntime {
  private readonly emitEvent: (event: PlayerEvent) => void;
  private readonly clock: () => number;
  private readonly checkpointEvery: number;
  private workerInstanceId: string;

  private attemptId = '';
  private generation = '0';
  private runId: string | null = null;
  private workspaceHash: string | null = null;
  private projectRevision: string | null = null;

  private definition: GridPathDefinition | null = null;
  private program: GridPathProgram | null = null;
  private run: GridPathRun | null = null;
  private status: PlayerRuntimeStatus = 'Ready';
  private problem: string | null = null;

  private permitId = -1;
  private permitted = false;
  private permitUntil = 0;

  private pauseRequested: string | null = null;
  /**
   * Vero quando la corsa si è sospesa **solo** per un permesso scaduto: è una pausa transitoria, e
   * un permesso nuovo la riprende. Una pausa deliberata (docente, recupero, vista) non si riprende
   * da sé (§11.5, §12.6).
   */
  private transientPause = false;
  private stepsSinceCheckpoint = 0;

  constructor(emit: (event: PlayerEvent) => void, options: GridPathPlayerOptions) {
    this.emitEvent = emit;
    this.workerInstanceId = options.workerInstanceId ?? '';
    this.clock = options.clock ?? (() => Date.now());
    this.checkpointEvery = Math.max(1, options.checkpointEvery ?? 10);
  }

  get runtimeStatus(): PlayerRuntimeStatus {
    return this.status;
  }

  get currentRunId(): string | null {
    return this.runId;
  }

  get currentAttemptId(): string {
    return this.attemptId;
  }

  /** Stato osservabile corrente, per la telemetria e per una lettura immediata. */
  scene(): PlayerScene {
    return this.run === null ? this.initialScene() : toScene(this.run.state());
  }

  handle(request: PlayerRequest): void {
    if (request.type === 'LoadProgram') {
      // La pagina si presenta qui: il worker ne adotta l'identità e da quel momento riconosce i
      // suoi comandi. Senza questo passaggio il worker scarterebbe ogni messaggio della pagina.
      this.workerInstanceId = request.workerInstanceId;
      this.load(request.workspaceJson, request.definitionJson, request);

      return;
    }

    if (this.workerInstanceId === '' || request.workerInstanceId !== this.workerInstanceId) return;
    if (this.attemptId === '' || request.attemptId !== this.attemptId) return;
    if (request.executionGeneration !== this.generation) return;

    switch (request.type) {
      case 'StartRun':
        this.start(request.runId);
        break;
      case 'PauseRun':
        this.pause(request.reason);
        break;
      case 'ResumeRun':
        this.resume(request.runId, request.checkpoint);
        break;
      case 'StopRun':
        this.stop();
        break;
      case 'UpdatePermit':
        this.updatePermit(request.permitId, request.permitted, request.validForMs);
        break;
    }
  }

  /**
   * Avanza la corsa di una sola operazione visibile. Il driver la richiama a intervalli didattici;
   * qui non c'è nessun ciclo infinito, così una corsa lunga resta interrompibile e osservabile.
   */
  pump(): void {
    if (this.status !== 'Running' || this.run === null) return;

    if (this.pauseRequested !== null) {
      const reason = this.pauseRequested;
      this.pauseRequested = null;
      this.pauseNow(reason);

      return;
    }

    if (!this.effectivePermit()) {
      // Scadenza del permesso: la corsa si sospende con un checkpoint, non si perde il punto.
      this.pauseNow(PERMIT_EXPIRED_REASON);

      return;
    }

    const state = this.run.step();
    this.stepsSinceCheckpoint += 1;

    if (state.outcome !== 'running') {
      // La corsa è conclusa: `complete` produce il checkpoint finale e lo stato definitivo, così non
      // si osserva un ultimo «Running» che non è più vero.
      this.complete(state);

      return;
    }

    this.emitState();

    if (this.stepsSinceCheckpoint >= this.checkpointEvery) {
      this.stepsSinceCheckpoint = 0;
      this.emitCheckpoint('Automatic');
    }
  }

  private load(workspaceJson: string, definitionJson: string, request: LoadProgramRequest): void {
    // Un nuovo programma ferma la corsa precedente: la sua generazione non vale più.
    this.run = null;
    this.runId = null;
    this.attemptId = request.attemptId;
    this.generation = request.executionGeneration;
    this.workspaceHash = request.workspaceHash;
    this.projectRevision = request.projectRevision;
    this.definition = null;
    this.program = null;
    this.problem = null;
    this.pauseRequested = null;
    this.stepsSinceCheckpoint = 0;
    this.permitted = false;
    this.permitUntil = 0;
    this.permitId = -1;
    this.transientPause = false;
    this.status = 'Ready';

    try {
      const definition = parseGridPathDefinition(definitionJson);
      this.definition = definition;
      this.program = compileGridPathProgram(JSON.parse(workspaceJson), {
        engineVersion: definition.engineVersion,
        allowedBlocks: definition.allowedBlocks,
        maxBlocks: definition.limits.maxBlocks,
        maxNestingDepth: definition.limits.maxNestingDepth,
      });
    } catch (error) {
      this.problem = describeProblem(error);
      this.fail(
        error instanceof GridPathDefinitionError
          ? PlayerErrorCode.DefinitionInvalid
          : PlayerErrorCode.ProgramInvalid,
        this.problem,
      );

      return;
    }

    this.emitState();
  }

  private start(runId: string | null): void {
    if (this.definition === null || this.program === null) {
      this.fail(
        PlayerErrorCode.ProgramInvalid,
        this.problem ?? 'Il progetto non è ancora stato caricato.',
      );

      return;
    }

    this.runId = runId ?? this.runId;
    this.stepsSinceCheckpoint = 0;
    this.pauseRequested = null;
    this.transientPause = false;
    this.run = createGridPathRun(this.program, this.definition.scene, this.limits());
    this.status = 'Running';
    this.emitState();
  }

  private resume(runId: string | null, checkpoint: unknown): void {
    if (this.definition === null || this.program === null) {
      this.fail(PlayerErrorCode.ProgramInvalid, 'Il progetto non è ancora stato caricato.');

      return;
    }

    try {
      this.run = resumeGridPathRun(
        this.program,
        this.definition.scene,
        this.limits(),
        checkpoint,
        this.clock,
      );
    } catch (error) {
      this.fail(
        error instanceof GridPathCheckpointError
          ? PlayerErrorCode.StaleMessage
          : PlayerErrorCode.UnexpectedCommand,
        error instanceof Error ? error.message : 'Il checkpoint non è utilizzabile.',
      );

      return;
    }

    this.runId = runId ?? this.runId;
    this.pauseRequested = null;
    this.transientPause = false;
    this.stepsSinceCheckpoint = 0;
    this.status = 'Running';
    this.emitState();
  }

  private pause(reason: string): void {
    if (this.run === null) {
      this.fail(PlayerErrorCode.UnexpectedCommand, 'Non c’è una corsa da sospendere.');

      return;
    }

    if (this.status === 'Running') {
      // La pausa si conferma al prossimo confine di passo: la richiesta non interrompe un passo a metà.
      this.pauseRequested = reason;

      return;
    }

    if (this.status === 'Paused') {
      // Una pausa deliberata sostituisce quella transitoria: il permesso non la riattiva.
      this.transientPause = false;
      this.emitCheckpoint(reason);
    }
  }

  private pauseNow(reason: string): void {
    this.status = 'Paused';
    this.transientPause = reason === PERMIT_EXPIRED_REASON;
    this.emitCheckpoint(reason);
    this.emitState();
  }

  private stop(): void {
    this.run = null;
    this.pauseRequested = null;
    this.transientPause = false;
    this.status = 'Stopped';
    this.emitState();
  }

  private updatePermit(permitId: number, permitted: boolean, validForMs: number): void {
    // Un permesso più vecchio di uno già ricevuto non allunga la validità (§13.6).
    if (permitId <= this.permitId) return;
    this.permitId = permitId;
    this.permitted = permitted;
    this.permitUntil = permitted ? this.clock() + Math.max(0, validForMs) : 0;

    // Il permesso è tornato dopo una sospensione transitoria: la corsa riprende dal punto in cui
    // si era fermata, senza un nuovo comando del docente (§11.5).
    if (this.transientPause && this.status === 'Paused' && this.effectivePermit()) {
      this.transientPause = false;
      this.status = 'Running';
      this.emitState();
    }
  }

  private effectivePermit(): boolean {
    return this.permitted && this.clock() < this.permitUntil;
  }

  private limits(): { maxExecutionSteps: number; maxActiveDurationMs: number } {
    const limits = this.definition?.limits;

    return {
      maxExecutionSteps: limits?.maxExecutionSteps ?? 0,
      maxActiveDurationMs: limits?.maxActiveDurationMs ?? 0,
    };
  }

  private complete(state: GridPathRunState): void {
    this.status = 'Completed';
    this.emitCheckpoint(state.outcome === 'success' ? 'Success' : 'Finished');
    this.emitEvent({
      ...this.envelope(),
      type: 'RunFinished',
      outcome: state.outcome,
      executions: state.executions,
      consumed: state.consumed,
    });
    this.emitState();
  }

  private emitCheckpoint(pauseReason: string): void {
    if (this.run === null) return;
    this.emitEvent({
      ...this.envelope(),
      type: 'CheckpointCreated',
      checkpoint: this.run.checkpoint(pauseReason),
      runtimeStatus: this.status,
    });
  }

  private emitState(): void {
    this.emitEvent({
      ...this.envelope(),
      type: 'RuntimeState',
      runtimeStatus: this.status,
      scene: this.scene(),
    });
  }

  private fail(code: string, message: string): void {
    this.status = 'Error';
    this.emitEvent({
      ...this.envelope(),
      type: 'RuntimeError',
      code,
      message,
      sourceBlockId: null,
    });
    this.emitState();
  }

  private initialScene(): PlayerScene {
    const start: GridCell = this.definition?.scene.start ?? { x: 0, y: 0 };
    const direction: GridDirection = this.definition?.scene.start.direction ?? 'East';

    return {
      position: start,
      direction,
      activeBlockId: null,
      executions: 0,
      consumed: 0,
      outcome: 'running',
    };
  }

  /** Campi comuni a ogni evento: l'identità dell'esecuzione per cui è stato prodotto (§13.6). */
  private envelope(): PlayerEnvelope {
    return {
      protocolVersion: PLAYER_PROTOCOL_VERSION,
      workerInstanceId: this.workerInstanceId,
      attemptId: this.attemptId,
      executionGeneration: this.generation,
      runId: this.runId,
    };
  }
}

type PlayerEnvelope = Pick<
  PlayerEvent,
  'protocolVersion' | 'workerInstanceId' | 'attemptId' | 'executionGeneration' | 'runId'
>;

/**
 * Motivo di una sospensione **transitoria**: la corsa si è fermata perché il permesso è scaduto e
 * riprenderà da sé quando ne arriverà uno nuovo (§11.5). È diverso da una pausa deliberata del
 * docente, che resta ferma finché non arriva un comando.
 */
export const PERMIT_EXPIRED_REASON = 'PermitExpired';

function toScene(state: GridPathRunState): PlayerScene {
  return {
    position: state.position,
    direction: state.direction,
    activeBlockId: state.activeBlockId,
    executions: state.executions,
    consumed: state.consumed,
    outcome: state.outcome,
  };
}

function describeProblem(error: unknown): string {
  if (error instanceof GridPathProgramError || error instanceof GridPathDefinitionError) {
    return error.message;
  }

  return 'Il progetto a blocchi non è leggibile: chiedi al docente.';
}
