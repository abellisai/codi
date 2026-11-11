/**
 * Definizione didattica `GridPath` (§13.2) e sua composizione dai campi dell'editor.
 *
 * Queste funzioni sono pure: non conoscono Angular, il dispatcher o il DOM. La pagina vi
 * appoggia la scena e il salvataggio, quindi la stessa regola — per esempio che il
 * completamento segue sempre il traguardo — vale per la griglia e per il JSON inviato,
 * senza due copie che possono divergere.
 */

import {
  GRID_PATH_ENGINE_VERSION,
  GRID_PATH_EXERCISE_TYPE,
  GRID_PATH_SCHEMA_VERSION,
  type GridCell,
  type GridDirection,
  type GridPathLimits,
  type GridPathScene,
  type GridStart,
} from '@exercise-engine/grid-path-scene';
import { GRID_PATH_BLOCK_TYPES, type GridPathBlockType } from '@exercise-engine/grid-path-blocks';
import { findEngine } from '@exercise-engine/engine-registry';

/**
 * La scena, i limiti e i nomi del motore vivono nella libreria, che li condivide con l'anteprima e
 * con i test; qui si ripubblicano perché la pagina li importa da questo modulo, che è anche quello
 * che compone la definizione. Una sola definizione per ciascuno: due copie divergenti sarebbero un
 * motore diverso con lo stesso nome.
 */
export {
  GRID_PATH_ENGINE_VERSION,
  GRID_PATH_EXERCISE_TYPE,
  GRID_PATH_SCHEMA_VERSION,
  type GridCell,
  type GridDirection,
  type GridPathLimits,
  type GridPathScene,
  type GridStart,
};

/** Orientamenti nell'ordine con cui si offrono nel selettore. */
export const GRID_DIRECTIONS: readonly GridDirection[] = ['North', 'East', 'South', 'West'];

/** Nomi italiani degli orientamenti: la definizione conserva quelli del motore. */
export const DIRECTION_LABELS: Readonly<Record<GridDirection, string>> = {
  North: 'Nord',
  East: 'Est',
  South: 'Sud',
  West: 'Ovest',
};

/** Freccia visibile per ogni orientamento: comunica la direzione anche senza colore. */
export const DIRECTION_ARROWS: Readonly<Record<GridDirection, string>> = {
  North: '↑',
  East: '→',
  South: '↓',
  West: '←',
};

/** Blocchi del catalogo `gridpath-1` (§13.4): l'ordine è quello mostrato nel cassetto Blockly. */
export type ExerciseBlockId = GridPathBlockType;

export const EXERCISE_BLOCKS: readonly ExerciseBlockId[] = GRID_PATH_BLOCK_TYPES;

/** Nomi comprensibili dei blocchi, mostrati nel selettore dell'editor. */
export const BLOCK_LABELS: Readonly<Record<ExerciseBlockId, string>> = {
  aula_start: 'Inizio del programma',
  aula_move: 'Fai un passo',
  aula_turn: 'Gira',
  aula_repeat: 'Ripeti',
};

/**
 * Etichetta dell'unico limite che il docente configura: la durata massima della corsa, in minuti.
 * Gli altri limiti del documento (`maxBlocks`, `maxNestingDepth`, `maxExecutionSteps`) sono budgets
 * tecnici del motore: si conservano dalla bozza e si riscrivono, senza un campo in pagina.
 */
export const DURATION_LIMIT_LABEL = 'Durata massima (minuti)';

/**
 * Intervallo ammesso per la durata, in minuti: gli stessi confini del dominio
 * (`ExerciseLimits.MaxMaxActiveDurationMs`, un'ora), espressi nell'unità che la pagina mostra.
 */
export const DURATION_LIMIT_RANGE = { min: 1, max: 60 } as const;

/** Griglia ammessa: la specifica propone 2×2–20×20 (§13.2). */
export const MIN_GRID_SIZE = 2;
export const MAX_GRID_SIZE = 20;

export const MAX_INSTRUCTIONS_LENGTH = 2000;
export const MAX_MESSAGE_LENGTH = 500;

/**
 * Progetto a blocchi di una bozza che non ne ha ancora uno. In questa porzione l'editor configura
 * la definizione didattica e non compone il progetto iniziale: il campo va comunque inviato, perché
 * la bozza ha un progetto — quello vuoto — e non un progetto mancante.
 *
 * Quando l'editor composerà i blocchi, al suo posto andrà la serializzazione di Blockly.
 */
export const EMPTY_INITIAL_WORKSPACE = '{}';

/** Limiti di partenza della definizione suggerita: sono quelli dell'esempio di §13.2. */
export const DEFAULT_LIMITS: GridPathLimits = {
  maxBlocks: 30,
  maxNestingDepth: 8,
  maxExecutionSteps: 1000,
  maxActiveDurationMs: 120000,
};

/** Definizione come viaggia in `definitionJson` (§13.2). */
export interface GridPathDefinition {
  readonly schemaVersion: number;
  readonly exerciseType: string;
  readonly engineVersion: string;
  readonly title: string;
  readonly instructions: string;
  readonly scene: GridPathScene;
  readonly toolbox: { readonly allowedBlocks: readonly string[] };
  readonly limits: GridPathLimits;
  readonly completion: { readonly type: 'ReachCell'; readonly x: number; readonly y: number };
  readonly feedback: { readonly success: string; readonly collision: string };
}

/**
 * Stato modificabile dell'editor. Larghezze, altezze e durata accettano `null` perché un campo
 * numerico svuotato vale `null` in Signal Forms: la conversione avviene solo nella composizione
 * della definizione, e la validazione impedisce che ci si arrivi con un valore mancante.
 *
 * I limiti tecnici non sono modificabili in pagina — non servono al docente per progettare
 * l'esercizio — ma restano nel modello perché il documento li conserva: azzerarli al salvataggio
 * cambierebbe il budget del motore di una bozza già scritta.
 */
export interface ExerciseEditorModel {
  readonly instructions: string;
  readonly width: number | null;
  readonly height: number | null;
  readonly startX: number;
  readonly startY: number;
  readonly direction: GridDirection;
  readonly goalX: number;
  readonly goalY: number;
  readonly obstacles: readonly GridCell[];
  readonly blocks: Readonly<Record<ExerciseBlockId, boolean>>;
  readonly limits: Readonly<
    Pick<GridPathLimits, 'maxBlocks' | 'maxNestingDepth' | 'maxExecutionSteps'>
  >;
  /** Durata massima attiva in minuti: la pagina parla minuti, il documento millisecondi. */
  readonly maxActiveDurationMinutes: number | null;
  readonly successMessage: string;
  readonly collisionMessage: string;
}

/** Scena suggerita quando la bozza non esiste: griglia 5×5, partenza in alto a sinistra. */
export function suggestedScene(): GridPathScene {
  return {
    width: 5,
    height: 5,
    start: { x: 0, y: 0, direction: 'East' },
    obstacles: [],
    goal: { x: 4, y: 4 },
  };
}

/** Modello suggerito per una bozza nuova: tutti i blocchi del catalogo e i limiti dell'esempio. */
export function suggestedEditorModel(): ExerciseEditorModel {
  const scene = suggestedScene();

  return {
    instructions: 'Porta il personaggio sulla stella usando i blocchi.',
    width: scene.width,
    height: scene.height,
    startX: scene.start.x,
    startY: scene.start.y,
    direction: scene.start.direction,
    goalX: scene.goal.x,
    goalY: scene.goal.y,
    obstacles: [],
    blocks: { aula_start: true, aula_move: true, aula_turn: true, aula_repeat: true },
    limits: technicalLimits(DEFAULT_LIMITS),
    maxActiveDurationMinutes: durationMinutes(DEFAULT_LIMITS.maxActiveDurationMs),
    successMessage: 'Hai raggiunto la stella!',
    collisionMessage: "C'è un ostacolo: prova a cambiare percorso.",
  };
}

/**
 * Converte una definizione letta dal server nel modello dell'editor. `aula_start` è sempre
 * selezionato: senza di lui il catalogo non è eseguibile (§13.4), quindi non è deselezionabile.
 */
export function toEditorModel(definition: GridPathDefinition): ExerciseEditorModel {
  const allowed = new Set(definition.toolbox.allowedBlocks);

  return {
    instructions: definition.instructions,
    width: definition.scene.width,
    height: definition.scene.height,
    startX: definition.scene.start.x,
    startY: definition.scene.start.y,
    direction: definition.scene.start.direction,
    goalX: definition.scene.goal.x,
    goalY: definition.scene.goal.y,
    obstacles: definition.scene.obstacles.map((cell) => ({ x: cell.x, y: cell.y })),
    blocks: {
      aula_start: true,
      aula_move: allowed.has('aula_move'),
      aula_turn: allowed.has('aula_turn'),
      aula_repeat: allowed.has('aula_repeat'),
    },
    limits: {
      maxBlocks: definition.limits.maxBlocks,
      maxNestingDepth: definition.limits.maxNestingDepth,
      maxExecutionSteps: definition.limits.maxExecutionSteps,
    },
    maxActiveDurationMinutes: durationMinutes(definition.limits.maxActiveDurationMs),
    successMessage: definition.feedback.success,
    collisionMessage: definition.feedback.collision,
  };
}

/** Vero se le due celle coincidono. */
export function sameCell(left: GridCell, right: GridCell): boolean {
  return left.x === right.x && left.y === right.y;
}

/**
 * Riporta la scena nei limiti del disegno: dimensione fra 2 e 20, coordinate intere dentro la
 * griglia, ostacoli deduplicati e mai sovrapposti a partenza o traguardo, partenza e traguardo
 * distinti. È l'unico punto che decide come si risolve una scena incoerente, quindi la griglia
 * mostrata e il JSON inviato non possono raccontare due storie diverse.
 */
export function normalizeScene(model: ExerciseEditorModel): GridPathScene {
  const width = clampInteger(model.width, MIN_GRID_SIZE, MAX_GRID_SIZE, MIN_GRID_SIZE);
  const height = clampInteger(model.height, MIN_GRID_SIZE, MAX_GRID_SIZE, MIN_GRID_SIZE);

  const start: GridStart = {
    x: clampCoordinate(model.startX, width),
    y: clampCoordinate(model.startY, height),
    direction: model.direction,
  };

  let goal: GridCell = {
    x: clampCoordinate(model.goalX, width),
    y: clampCoordinate(model.goalY, height),
  };

  const obstacles = dedupeObstacles(model.obstacles, width, height);

  // Un esercizio già risolto non è un esercizio: se il traguardo finisce sulla partenza — per
  // esempio dopo un ridimensionamento — si sposta sulla prima cella libera.
  if (sameCell(goal, start)) {
    goal = firstFreeCell(width, height, start, obstacles);
  }

  const freeObstacles = obstacles.filter((cell) => !sameCell(cell, start) && !sameCell(cell, goal));

  return { width, height, start, obstacles: freeObstacles, goal };
}

/** Compone la definizione da inviare dai campi dell'editor e dal titolo dell'esercizio. */
export function buildDefinition(title: string, model: ExerciseEditorModel): GridPathDefinition {
  const scene = normalizeScene(model);
  const allowedBlocks = EXERCISE_BLOCKS.filter(
    (block) => block === 'aula_start' || model.blocks[block],
  );

  return {
    schemaVersion: GRID_PATH_SCHEMA_VERSION,
    exerciseType: GRID_PATH_EXERCISE_TYPE,
    engineVersion: GRID_PATH_ENGINE_VERSION,
    title,
    instructions: model.instructions,
    scene,
    toolbox: { allowedBlocks },
    limits: {
      maxBlocks: positiveInteger(model.limits.maxBlocks, DEFAULT_LIMITS.maxBlocks),
      maxNestingDepth: positiveInteger(
        model.limits.maxNestingDepth,
        DEFAULT_LIMITS.maxNestingDepth,
      ),
      maxExecutionSteps: positiveInteger(
        model.limits.maxExecutionSteps,
        DEFAULT_LIMITS.maxExecutionSteps,
      ),
      maxActiveDurationMs: durationMilliseconds(model.maxActiveDurationMinutes),
    },
    completion: { type: 'ReachCell', x: scene.goal.x, y: scene.goal.y },
    feedback: { success: model.successMessage, collision: model.collisionMessage },
  };
}

/**
 * Legge la `definitionJson` del server. Restituisce `null` per un testo che non è una definizione
 * `GridPath` utilizzabile: la pagina lo dice e non lo sovrascrive con valori suggeriti.
 *
 * Il tipo e la versione dichiarati devono essere quelli che questa build sa comporre e mostrare:
 * un documento di un'altra tipologia ha per forza una forma diversa, e leggerlo come se fosse una
 * griglia mostrerebbe all'editore campi che non esistono. Il registro del motore è la sorgente di
 * quella verifica, non una costante scritta qui.
 */
export function parseDefinition(json: string): GridPathDefinition | null {
  let value: unknown;
  try {
    value = JSON.parse(json);
  } catch {
    return null;
  }

  if (!isRecord(value) || !isRecord(value['scene']) || !isRecord(value['toolbox'])) {
    return null;
  }
  if (!Array.isArray(value['toolbox']['allowedBlocks'])) {
    return null;
  }
  if (!isRecord(value['limits']) || !isRecord(value['feedback'])) {
    return null;
  }
  if (
    typeof value['schemaVersion'] !== 'number' ||
    typeof value['exerciseType'] !== 'string' ||
    typeof value['engineVersion'] !== 'string' ||
    typeof value['title'] !== 'string' ||
    typeof value['instructions'] !== 'string'
  ) {
    return null;
  }

  const engine = findEngine(value['engineVersion']);
  if (engine === null || engine.exerciseType !== value['exerciseType']) {
    return null;
  }

  return value as unknown as GridPathDefinition;
}

function clampInteger(value: number | null, min: number, max: number, fallback: number): number {
  if (value === null || !Number.isFinite(value)) {
    return fallback;
  }

  return Math.min(Math.max(Math.trunc(value), min), max);
}

function clampCoordinate(value: number, size: number): number {
  if (!Number.isFinite(value)) {
    return 0;
  }

  return Math.min(Math.max(Math.trunc(value), 0), size - 1);
}

function positiveInteger(value: number | null, fallback: number): number {
  return value === null || !Number.isFinite(value) || value <= 0 ? fallback : Math.trunc(value);
}

/** I soli limiti tecnici del modello: la durata ha un campo suo, in secondi. */
function technicalLimits(
  limits: GridPathLimits,
): Readonly<Pick<GridPathLimits, 'maxBlocks' | 'maxNestingDepth' | 'maxExecutionSteps'>> {
  return {
    maxBlocks: limits.maxBlocks,
    maxNestingDepth: limits.maxNestingDepth,
    maxExecutionSteps: limits.maxExecutionSteps,
  };
}

/**
 * Durata del documento → minuti della pagina. Il minimo è un minuto: una bozza scritta con una
 * durata più corta si arrotonda, e il valore più piccolo resta configurabile.
 */
function durationMinutes(milliseconds: number): number {
  return Math.max(1, Math.round(milliseconds / 60_000));
}

/** Minuti della pagina → durata del documento; un valore assente torna al default di §13.2. */
function durationMilliseconds(minutes: number | null): number {
  return positiveInteger(minutes, durationMinutes(DEFAULT_LIMITS.maxActiveDurationMs)) * 60_000;
}

function dedupeObstacles(
  obstacles: readonly GridCell[],
  width: number,
  height: number,
): readonly GridCell[] {
  const seen = new Set<string>();
  const result: GridCell[] = [];

  for (const cell of obstacles) {
    if (!Number.isFinite(cell.x) || !Number.isFinite(cell.y)) {
      continue;
    }
    const x = Math.trunc(cell.x);
    const y = Math.trunc(cell.y);
    if (x < 0 || y < 0 || x >= width || y >= height) {
      continue;
    }
    const key = `${String(x)}:${String(y)}`;
    if (seen.has(key)) {
      continue;
    }
    seen.add(key);
    result.push({ x, y });
  }

  return result;
}

/** Prima cella diversa dalla partenza e libera; se non esiste, la prima diversa dalla partenza. */
function firstFreeCell(
  width: number,
  height: number,
  start: GridCell,
  obstacles: readonly GridCell[],
): GridCell {
  let fallback: GridCell | null = null;

  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const cell: GridCell = { x, y };
      if (sameCell(cell, start)) {
        continue;
      }
      fallback ??= cell;
      if (!obstacles.some((obstacle) => sameCell(obstacle, cell))) {
        return cell;
      }
    }
  }

  return fallback ?? { x: 0, y: 0 };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
