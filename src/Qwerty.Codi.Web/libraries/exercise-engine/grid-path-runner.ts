import type { GridPathInstruction, GridPathProgram } from '@exercise-engine/grid-path-program';
import {
  GRID_PATH_CHECKPOINT_SCHEMA_VERSION,
  GridPathCheckpointError,
  parseGridPathCheckpointState,
  type GridPathCheckpointFrame,
  type GridPathCheckpointState,
  type GridPathOutcome,
} from '@exercise-engine/grid-path-checkpoint';
import {
  GRID_PATH_ENGINE_VERSION,
  contains,
  isInside,
  step,
  turnDirection,
  type GridCell,
  type GridDirection,
  type GridPathScene,
} from '@exercise-engine/grid-path-scene';

export type { GridPathOutcome } from '@exercise-engine/grid-path-checkpoint';

/**
 * Interprete a passi della versione `gridpath-1` (§13.2, §13.5, §13.7). È lo stesso motore che
 * l'anteprima dell'autore usa in stato isolato e che il worker del player esegue: l'anteprima non
 * produce comandi di classe né risultati di alunni (§10.4), quindi la corsa vive qui e non nel
 * protocollo della sessione.
 *
 * I cicli non vengono espansi: la ripetizione è un frame con il proprio indice e il proprio
 * contatore, e anche le operazioni di controllo consumano budget, così un ciclo vuoto non aggira il
 * limite (§13.5). Ogni passo è osservabile e porta con sé il blocco che lo ha prodotto, per
 * evidenziarlo mentre scorre.
 *
 * La corsa sa produrre un checkpoint serializzabile a ogni confine di passo e sa riprendere da uno
 * compatibile: `checkpoint()` e `resumeGridPathRun()` sono la stessa macchina, così una corsa
 * ripresa dopo una pausa dà lo stesso esito di una corsa continua (§19.4).
 */

/** Limiti che riguardano l'esecuzione; gli altri sono verificati alla compilazione. */
export interface GridPathRunLimits {
  readonly maxExecutionSteps: number;
  readonly maxActiveDurationMs: number;
}

/** Stato osservabile della corsa: è ciò che l'anteprima disegna. */
export interface GridPathRunState {
  readonly position: GridCell;
  readonly direction: GridDirection;
  readonly outcome: GridPathOutcome;
  /** Passi e rotazioni eseguiti: le operazioni che si vedono. */
  readonly executions: number;
  /** Operazioni contate rispetto al budget, comprese quelle di controllo dei cicli. */
  readonly consumed: number;
  readonly activeBlockId: string | null;
}

/** Corsa in corso, avanzabile un passo alla volta. */
export interface GridPathRun {
  state(): GridPathRunState;
  done(): boolean;
  step(): GridPathRunState;
  runAll(): GridPathRunState;
  /** Stato serializzabile del confine corrente, con il motivo per cui ci si è fermati. */
  checkpoint(pauseReason: string): GridPathCheckpointState;
}

interface Frame {
  readonly instructions: readonly GridPathInstruction[];
  /** Percorso degli indici fino al blocco «ripeti» che ha aperto il frame; vuoto per la radice. */
  readonly path: readonly number[];
  index: number;
  remaining: number;
}

/** Ciò che una ripresa ripristina: la corsa riparte dallo stesso confine del checkpoint. */
interface RestoredRun {
  readonly frames: readonly GridPathCheckpointFrame[];
  readonly position: GridCell;
  readonly direction: GridDirection;
  readonly outcome: GridPathOutcome;
  readonly executions: number;
  readonly consumed: number;
  readonly activeBlockId: string | null;
  readonly activeDurationMs: number;
}

/**
 * Crea una corsa isolata: stato proprio, nessuna condivisione con altre corse e nessuna scrittura.
 * L'orologio è iniettabile perché il budget di durata attiva non si prova aspettando davvero.
 */
export function createGridPathRun(
  program: GridPathProgram,
  scene: GridPathScene,
  limits: GridPathRunLimits,
  clock: () => number = () => Date.now(),
): GridPathRun {
  return buildRun(program, scene, limits, clock, null);
}

/**
 * Riprende una corsa da un checkpoint verificato. Il checkpoint deve appartenere alla stessa
 * versione del motore e allo stesso progetto: la coerenza con tentativo, generazione e impronta è
 * dell'involucro che lo conserva, mentre qui si verifica che lo stack di esecuzione esista ancora
 * nelle istruzioni del programma. Un checkpoint di un altro progetto viene rifiutato invece di
 * essere interpretato a caso.
 */
export function resumeGridPathRun(
  program: GridPathProgram,
  scene: GridPathScene,
  limits: GridPathRunLimits,
  checkpoint: unknown,
  clock: () => number = () => Date.now(),
): GridPathRun {
  const state = parseGridPathCheckpointState(checkpoint);

  return buildRun(program, scene, limits, clock, {
    frames: state.frames,
    position: state.position,
    direction: state.direction,
    outcome: state.outcome,
    executions: state.executions,
    consumed: state.consumed,
    activeBlockId: state.activeBlockId,
    activeDurationMs: state.activeDurationMs,
  });
}

function buildRun(
  program: GridPathProgram,
  scene: GridPathScene,
  limits: GridPathRunLimits,
  clock: () => number,
  restored: RestoredRun | null,
): GridPathRun {
  const frames: Frame[] =
    restored === null
      ? [{ instructions: program.instructions, path: [], index: 0, remaining: 0 }]
      : restoreFrames(program, restored.frames);

  // Il tempo trascorso in pausa non consuma la durata attiva: la ripresa riparte dall'orologio
  // spostato indietro della durata già consumata, non dal momento del ripristino (§13.7).
  const startedAt = clock() - (restored?.activeDurationMs ?? 0);

  let position: GridCell = restored?.position ?? { x: scene.start.x, y: scene.start.y };
  let direction: GridDirection = restored?.direction ?? scene.start.direction;
  let outcome: GridPathOutcome = restored?.outcome ?? 'running';
  let executions = restored?.executions ?? 0;
  let consumed = restored?.consumed ?? 0;
  let activeBlockId: string | null = restored?.activeBlockId ?? null;

  const state = (): GridPathRunState => ({
    position,
    direction,
    outcome,
    executions,
    consumed,
    activeBlockId,
  });

  /** Consuma un'operazione rispetto al budget; falso quando il budget è finito. */
  const charge = (): boolean => {
    if (consumed >= limits.maxExecutionSteps || clock() - startedAt > limits.maxActiveDurationMs) {
      outcome = 'budget';

      return false;
    }

    consumed += 1;

    return true;
  };

  const apply = (instruction: GridPathInstruction): void => {
    if (instruction.op === 'turn') {
      direction = turnDirection(direction, instruction.turn ?? 'Left');
      executions += 1;
      activeBlockId = instruction.sourceBlockId;

      return;
    }

    const next = step(position, direction);
    if (!isInside(scene, next)) {
      // Un passo fuori dalla griglia termina la corsa e mantiene la posizione precedente (§13.2).
      outcome = 'leftTheGrid';

      return;
    }

    if (contains(scene.obstacles, next)) {
      outcome = 'collision';

      return;
    }

    position = next;
    executions += 1;
    activeBlockId = instruction.sourceBlockId;

    if (next.x === scene.goal.x && next.y === scene.goal.y) {
      // Il traguardo conclude la corsa con successo: il resto del programma non viene eseguito.
      outcome = 'success';
    }
  };

  /**
   * Avanza fino a compiere una sola operazione visibile — un passo o una rotazione — dopo aver
   * contato ciò che serve a raggiungerla: l'ingresso in un ciclo e ogni suo giro sono operazioni di
   * controllo e consumano budget (§13.5).
   */
  const advance = (): void => {
    while (outcome === 'running' && frames.length > 0) {
      const frame = frames[frames.length - 1];
      if (frame.index >= frame.instructions.length) {
        if (frame.remaining > 0) {
          // Un giro in più della ripetizione è un'operazione di controllo e consuma budget.
          if (!charge()) {
            return;
          }

          frame.remaining -= 1;
          frame.index = 0;
          continue;
        }

        frames.pop();
        continue;
      }

      const instructionIndex = frame.index;
      const instruction = frame.instructions[instructionIndex];
      frame.index += 1;

      if (instruction.op === 'repeat') {
        if (!charge()) {
          return;
        }

        const count = instruction.count ?? 0;
        if (count > 0) {
          frames.push({
            instructions: instruction.body ?? [],
            path: [...frame.path, instructionIndex],
            index: 0,
            remaining: count - 1,
          });
        }

        continue;
      }

      if (!charge()) {
        return;
      }

      apply(instruction);

      return;
    }

    if (outcome === 'running') {
      outcome = 'finished';
    }
  };

  return {
    state,
    done: () => outcome !== 'running',
    step: () => {
      if (outcome === 'running') {
        advance();
      }

      return state();
    },
    runAll: () => {
      // La corsa termina da sola: il ciclo è solo la ripetizione del passo, con la stessa garanzia
      // di terminazione del budget.
      let guard = 0;
      while (outcome === 'running' && guard < limits.maxExecutionSteps + 1) {
        advance();
        guard += 1;
      }

      if (outcome === 'running') {
        outcome = 'budget';
      }

      return state();
    },
    checkpoint: (pauseReason: string): GridPathCheckpointState => ({
      schemaVersion: GRID_PATH_CHECKPOINT_SCHEMA_VERSION,
      engineVersion: GRID_PATH_ENGINE_VERSION,
      position,
      direction,
      outcome,
      executions,
      consumed,
      activeBlockId,
      activeDurationMs: Math.max(0, Math.round(clock() - startedAt)),
      pauseReason,
      frames: frames.map((frame) => ({
        path: [...frame.path],
        index: frame.index,
        remaining: frame.remaining,
      })),
    }),
  };
}

/** Ricostruisce lo stack di frame risolvendo i percorsi nelle istruzioni del programma. */
function restoreFrames(
  program: GridPathProgram,
  frames: readonly GridPathCheckpointFrame[],
): Frame[] {
  const rebuilt: Frame[] = [];

  for (let index = 0; index < frames.length; index += 1) {
    const frame = frames[index];
    let instructions: readonly GridPathInstruction[];

    if (index === 0) {
      if (frame.path.length !== 0) {
        throw new GridPathCheckpointError(
          'Il primo frame del checkpoint non può avere un percorso.',
        );
      }

      instructions = program.instructions;
    } else {
      const parent = frames[index - 1];
      if (frame.path.length !== parent.path.length + 1) {
        throw new GridPathCheckpointError(
          'I frame del checkpoint non sono annidati in modo coerente.',
        );
      }

      for (let step = 0; step < parent.path.length; step += 1) {
        if (frame.path[step] !== parent.path[step]) {
          throw new GridPathCheckpointError(
            'I frame del checkpoint non condividono lo stesso percorso.',
          );
        }
      }

      const repeatIndex = frame.path[frame.path.length - 1];
      const repeat = rebuilt[index - 1].instructions[repeatIndex];
      if (repeat === undefined || repeat.op !== 'repeat') {
        throw new GridPathCheckpointError(
          'Il checkpoint rimanda a una ripetizione che il progetto non contiene più.',
        );
      }

      instructions = repeat.body ?? [];
    }

    if (frame.index > instructions.length) {
      throw new GridPathCheckpointError('Il checkpoint punta oltre le istruzioni del progetto.');
    }

    rebuilt.push({
      instructions,
      path: frame.path,
      index: frame.index,
      remaining: frame.remaining,
    });
  }

  return rebuilt;
}

/** Messaggio dell'esito, nella forma che il docente e l'alunno leggono. */
export function describeOutcome(outcome: GridPathOutcome): string {
  switch (outcome) {
    case 'success':
      return 'Traguardo raggiunto.';
    case 'collision':
      return 'Il personaggio ha incontrato un ostacolo.';
    case 'leftTheGrid':
      return 'Il personaggio è uscito dalla griglia.';
    case 'budget':
      return 'Il programma ha esaurito il tempo o i passi a disposizione.';
    case 'finished':
      return 'Il programma è finito senza raggiungere il traguardo.';
    case 'running':
      return 'Programma in esecuzione.';
  }
}
