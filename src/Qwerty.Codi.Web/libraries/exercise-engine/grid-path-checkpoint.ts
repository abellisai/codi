import {
  GRID_PATH_ENGINE_VERSION,
  type GridCell,
  type GridDirection,
} from '@exercise-engine/grid-path-scene';

/**
 * Checkpoint del motore `gridpath-1` (§13.7). È il quarto formato della piattaforma, distinto dalla
 * definizione didattica, dal workspace Blockly e dallo stato osservabile: qui vive **solo** la parte
 * che l'interprete deve ricordare per riprendere esattamente da dove si era fermato.
 *
 * Il checkpoint non è autonomo: da solo non contiene il programma. È ripristinabile soltanto con lo
 * stesso progetto, la stessa versione del motore e la stessa generazione di esecuzione (§13.7), e
 * quei riferimenti — tentativo, versione dell'esercizio, generazione, impronta e revisione del
 * progetto — appartengono all'involucro che l'applicazione compone intorno a questo stato. Qui
 * restano le condizioni che l'interprete può verificare da sé: schema, versione del motore e
 * coerenza interna dello stato.
 *
 * Modificare i blocchi invalida il checkpoint: l'impronta del progetto cambia, e chi lo conserva lo
 * rifiuta prima ancora di provare a riprenderlo. Tempo trascorso in pausa o in attesa di rete non
 * consuma il budget di durata attiva (§13.7).
 */

/** Versione dello schema del checkpoint compresa da questa versione del motore. */
export const GRID_PATH_CHECKPOINT_SCHEMA_VERSION = 1;

/** Esito didattico della corsa. `running` significa che la corsa può ancora avanzare. */
export type GridPathOutcome =
  'running' | 'success' | 'collision' | 'leftTheGrid' | 'budget' | 'finished';

/**
 * Un frame dello stack di esecuzione. `path` è il percorso degli indici di istruzione dalla radice
 * fino al blocco `ripeti` che ha aperto il frame: il programma non viene copiato nel checkpoint, così
 * un checkpoint resta piccolo e vale solo per il progetto da cui è nato.
 */
export interface GridPathCheckpointFrame {
  readonly path: readonly number[];
  readonly index: number;
  readonly remaining: number;
}

/**
 * Stato serializzabile di un confine deterministico. Contiene ciò che serve a ricostruire la corsa:
 * posizione e orientamento, esito corrente, stack di frame con contatori, passi consumati, durata
 * attiva consumata e motivo della pausa.
 */
export interface GridPathCheckpointState {
  readonly schemaVersion: number;
  readonly engineVersion: string;
  readonly position: GridCell;
  readonly direction: GridDirection;
  readonly outcome: GridPathOutcome;
  readonly executions: number;
  readonly consumed: number;
  readonly activeBlockId: string | null;
  readonly activeDurationMs: number;
  readonly pauseReason: string;
  readonly frames: readonly GridPathCheckpointFrame[];
}

/** Errore di un checkpoint che non si può usare: il messaggio spiega perché. */
export class GridPathCheckpointError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'GridPathCheckpointError';
  }
}

const OUTCOMES: readonly GridPathOutcome[] = [
  'running',
  'success',
  'collision',
  'leftTheGrid',
  'budget',
  'finished',
];

const DIRECTIONS: readonly GridDirection[] = ['North', 'East', 'South', 'West'];

/**
 * Converte un valore in un checkpoint verificato. Rifiuta ciò che non è un checkpoint, un formato
 * futuro e uno stato internamente incoerente: un checkpoint si riprende solo se è della stessa
 * versione del motore e ha la forma attesa (§13.7). Il chiamante conserva il valore così com'è se
 * lo accetta: la conversione non lo arricchisce.
 */
export function parseGridPathCheckpointState(
  value: unknown,
  engineVersion: string = GRID_PATH_ENGINE_VERSION,
): GridPathCheckpointState {
  if (!isRecord(value)) {
    throw new GridPathCheckpointError('Il checkpoint non è un oggetto.');
  }

  if (value['schemaVersion'] !== GRID_PATH_CHECKPOINT_SCHEMA_VERSION) {
    throw new GridPathCheckpointError(
      `Il checkpoint usa lo schema ${String(value['schemaVersion'])}, che questa versione non conosce.`,
    );
  }

  if (value['engineVersion'] !== engineVersion) {
    throw new GridPathCheckpointError('Il checkpoint appartiene a un’altra versione del motore.');
  }

  const position = parseCell(value['position'], 'partenza');
  const direction = parseDirection(value['direction']);
  const outcome = parseOutcome(value['outcome']);
  const executions = parseCount(value['executions'], 'passi eseguiti');
  const consumed = parseCount(value['consumed'], 'passi consumati');
  const activeDurationMs = parseCount(value['activeDurationMs'], 'durata attiva');
  const pauseReason = typeof value['pauseReason'] === 'string' ? value['pauseReason'] : '';
  const activeBlockId = typeof value['activeBlockId'] === 'string' ? value['activeBlockId'] : null;

  const framesValue = value['frames'];
  if (!Array.isArray(framesValue) || framesValue.length === 0) {
    throw new GridPathCheckpointError('Il checkpoint non contiene lo stack di esecuzione.');
  }

  const frames = framesValue.map((frame, index) => parseFrame(frame, index));

  return {
    schemaVersion: GRID_PATH_CHECKPOINT_SCHEMA_VERSION,
    engineVersion,
    position,
    direction,
    outcome,
    executions,
    consumed,
    activeDurationMs,
    pauseReason,
    activeBlockId,
    frames,
  };
}

/** Vero se il valore è un checkpoint utilizzabile con la versione del motore indicata. */
export function isGridPathCheckpointState(
  value: unknown,
  engineVersion: string = GRID_PATH_ENGINE_VERSION,
): value is GridPathCheckpointState {
  try {
    parseGridPathCheckpointState(value, engineVersion);

    return true;
  } catch {
    return false;
  }
}

function parseFrame(value: unknown, position: number): GridPathCheckpointFrame {
  if (!isRecord(value)) {
    throw new GridPathCheckpointError(`Il frame ${String(position)} non è un oggetto.`);
  }

  const rawPath = value['path'];
  if (!Array.isArray(rawPath)) {
    throw new GridPathCheckpointError(`Il frame ${String(position)} non ha un percorso valido.`);
  }

  const path = rawPath.map((step) => {
    if (typeof step !== 'number' || !Number.isInteger(step) || step < 0) {
      throw new GridPathCheckpointError(
        `Il frame ${String(position)} ha un percorso con un indice non valido.`,
      );
    }

    return step;
  });

  return {
    path,
    index: parseCount(value['index'], 'posizione nel frame'),
    remaining: parseCount(value['remaining'], 'ripetizioni residue'),
  };
}

function parseCell(value: unknown, what: string): GridCell {
  if (!isRecord(value)) {
    throw new GridPathCheckpointError(`Il checkpoint non ha una ${what} valida.`);
  }

  return {
    x: parseInteger(value['x'], `${what} x`),
    y: parseInteger(value['y'], `${what} y`),
  };
}

function parseInteger(value: unknown, what: string): number {
  if (typeof value !== 'number' || !Number.isInteger(value)) {
    throw new GridPathCheckpointError(`Il checkpoint ha un valore non intero per ${what}.`);
  }

  return value;
}

function parseCount(value: unknown, what: string): number {
  const count = parseInteger(value, what);
  if (count < 0) {
    throw new GridPathCheckpointError(`Il checkpoint ha un valore negativo per ${what}.`);
  }

  return count;
}

function parseDirection(value: unknown): GridDirection {
  if (typeof value !== 'string' || !DIRECTIONS.includes(value as GridDirection)) {
    throw new GridPathCheckpointError('Il checkpoint ha un orientamento sconosciuto.');
  }

  return value as GridDirection;
}

function parseOutcome(value: unknown): GridPathOutcome {
  if (typeof value !== 'string' || !OUTCOMES.includes(value as GridPathOutcome)) {
    throw new GridPathCheckpointError('Il checkpoint ha un esito sconosciuto.');
  }

  return value as GridPathOutcome;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
