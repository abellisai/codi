import {
  GRID_PATH_ENGINE_VERSION,
  GRID_PATH_EXERCISE_TYPE,
  GRID_PATH_SCHEMA_VERSION,
  type GridCell,
  type GridDirection,
  type GridPathLimits,
  type GridPathScene,
} from '@exercise-engine/grid-path-scene';
import { blocksForEngine } from '@exercise-engine/grid-path-blocks';

/**
 * Lettura della definizione didattica `GridPath` (§13.2). È il contratto che il motore condivide con
 * la pagina dell'alunno: la definizione arriva come JSON dal server e va verificata **prima** di
 * compilare il progetto, perché un motore che parte con una scena incoerente produrrebbe una
 * diagnosi peggiore di un rifiuto spiegato.
 *
 * Il server ha già validato la definizione quando l'ha pubblicata; questa lettura non la sostituisce,
 * difende il player da un contenuto che non corrisponde al formato atteso — per esempio una versione
 * futura dello schema — senza convertirla in silenzio (§13.3).
 */

/** Messaggi mostrati all'alunno per i due esiti didattici più frequenti. */
export interface GridPathFeedback {
  readonly success: string;
  readonly collision: string;
}

/** Definizione didattica utilizzabile dal motore. Non contiene la soluzione di riferimento. */
export interface GridPathDefinition {
  readonly engineVersion: string;
  readonly schemaVersion: number;
  readonly title: string;
  readonly instructions: string;
  readonly scene: GridPathScene;
  readonly limits: GridPathLimits;
  readonly allowedBlocks: readonly string[];
  readonly feedback: GridPathFeedback;
}

/** Errore di una definizione che non si può usare: il messaggio spiega perché. */
export class GridPathDefinitionError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'GridPathDefinitionError';
  }
}

const DIRECTIONS: readonly GridDirection[] = ['North', 'East', 'South', 'West'];
const DEFAULT_SUCCESS = 'Hai raggiunto il traguardo!';
const DEFAULT_COLLISION = 'C’è un ostacolo: prova a cambiare percorso.';

/**
 * Converte il testo della definizione in una definizione verificata. Accetta soltanto la versione
 * dello schema e il tipo di esercizio di questa versione del motore: uno schema futuro non viene
 * interpretato come se fosse quello corrente.
 */
export function parseGridPathDefinition(json: string): GridPathDefinition {
  const value = parseJson(json);
  if (value['schemaVersion'] !== GRID_PATH_SCHEMA_VERSION) {
    throw new GridPathDefinitionError(
      `La definizione usa lo schema ${String(value['schemaVersion'])}, che questa versione non conosce.`,
    );
  }

  if (value['exerciseType'] !== GRID_PATH_EXERCISE_TYPE) {
    throw new GridPathDefinitionError('La definizione non è un esercizio di percorso a griglia.');
  }

  const engineVersion =
    typeof value['engineVersion'] === 'string' ? value['engineVersion'] : GRID_PATH_ENGINE_VERSION;
  const allowed = blocksForEngine(engineVersion);
  if (allowed.length === 0) {
    throw new GridPathDefinitionError(
      `La definizione chiede il motore «${engineVersion}», che questa versione non conosce.`,
    );
  }

  const toolbox = asRecord(value['toolbox']);
  const allowedValue = toolbox === null ? null : toolbox['allowedBlocks'];
  const allowedBlocks = Array.isArray(allowedValue)
    ? allowed.filter((type) => (allowedValue as readonly unknown[]).includes(type))
    : allowed;
  if (allowedBlocks.length === 0) {
    throw new GridPathDefinitionError('La definizione non mette a disposizione nessun blocco.');
  }

  const feedback = asRecord(value['feedback']);
  const limits = parseLimits(value['limits']);

  return {
    engineVersion,
    schemaVersion: GRID_PATH_SCHEMA_VERSION,
    title: typeof value['title'] === 'string' ? value['title'] : '',
    instructions: typeof value['instructions'] === 'string' ? value['instructions'] : '',
    scene: parseScene(value['scene']),
    limits,
    allowedBlocks,
    feedback: {
      success: text(feedback?.['success'], DEFAULT_SUCCESS),
      collision: text(feedback?.['collision'], DEFAULT_COLLISION),
    },
  };
}

function parseScene(value: unknown): GridPathScene {
  const scene = asRecord(value);
  if (scene === null) {
    throw new GridPathDefinitionError('La definizione non contiene la scena.');
  }

  const width = integer(scene['width'], 'larghezza della griglia');
  const height = integer(scene['height'], 'altezza della griglia');
  if (width < 2 || height < 2) {
    throw new GridPathDefinitionError('La griglia deve essere almeno di 2×2 celle.');
  }

  const start = asRecord(scene['start']);
  if (start === null) {
    throw new GridPathDefinitionError('La definizione non contiene il punto di partenza.');
  }

  const direction = start['direction'];
  if (typeof direction !== 'string' || !DIRECTIONS.includes(direction as GridDirection)) {
    throw new GridPathDefinitionError('Il punto di partenza ha un orientamento sconosciuto.');
  }

  const startCell: GridCell = {
    x: integer(start['x'], 'partenza x'),
    y: integer(start['y'], 'partenza y'),
  };
  const goal = asRecord(scene['goal']);
  if (goal === null) {
    throw new GridPathDefinitionError('La definizione non contiene il traguardo.');
  }

  const goalCell: GridCell = {
    x: integer(goal['x'], 'traguardo x'),
    y: integer(goal['y'], 'traguardo y'),
  };

  const obstaclesValue = scene['obstacles'];
  const obstacles = Array.isArray(obstaclesValue)
    ? obstaclesValue.map((cell) => {
        const record = asRecord(cell);
        if (record === null) {
          throw new GridPathDefinitionError('La definizione ha un ostacolo non valido.');
        }

        return { x: integer(record['x'], 'ostacolo x'), y: integer(record['y'], 'ostacolo y') };
      })
    : [];

  // Le stesse regole della versione del motore (§13.2): partenza e traguardo dentro la griglia e non
  // su un ostacolo. Un motore che partisse fuori griglia terminerebbe subito senza spiegare nulla.
  for (const [name, cell] of [
    ['partenza', startCell],
    ['traguardo', goalCell],
  ] as const) {
    if (cell.x < 0 || cell.y < 0 || cell.x >= width || cell.y >= height) {
      throw new GridPathDefinitionError(`Il punto di ${name} è fuori dalla griglia.`);
    }
  }

  if (obstacles.some((cell) => cell.x === startCell.x && cell.y === startCell.y)) {
    throw new GridPathDefinitionError('Il punto di partenza non può stare su un ostacolo.');
  }

  return {
    width,
    height,
    start: { ...startCell, direction: direction as GridDirection },
    obstacles,
    goal: goalCell,
  };
}

function parseLimits(value: unknown): GridPathLimits {
  const limits = asRecord(value);
  if (limits === null) {
    throw new GridPathDefinitionError('La definizione non contiene i limiti dell’esercizio.');
  }

  return {
    maxBlocks: integer(limits['maxBlocks'], 'numero massimo di blocchi'),
    maxNestingDepth: integer(limits['maxNestingDepth'], 'profondità massima'),
    maxExecutionSteps: integer(limits['maxExecutionSteps'], 'passi massimi'),
    maxActiveDurationMs: integer(limits['maxActiveDurationMs'], 'durata massima attiva'),
  };
}

function parseJson(json: string): Record<string, unknown> {
  let parsed: unknown;
  try {
    parsed = JSON.parse(json);
  } catch {
    throw new GridPathDefinitionError('La definizione dell’esercizio non è leggibile.');
  }

  const record = asRecord(parsed);
  if (record === null) {
    throw new GridPathDefinitionError('La definizione dell’esercizio non è un oggetto.');
  }

  return record;
}

function integer(value: unknown, what: string): number {
  if (typeof value !== 'number' || !Number.isInteger(value)) {
    throw new GridPathDefinitionError(`La definizione ha un valore non intero per ${what}.`);
  }

  return value;
}

function text(value: unknown, fallback: string): string {
  return typeof value === 'string' && value.length > 0 ? value : fallback;
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}
