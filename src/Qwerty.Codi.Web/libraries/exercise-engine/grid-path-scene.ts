/**
 * Regole e forme della prima versione del motore degli esercizi, `gridpath-1` (§13.1, §13.2).
 *
 * Vivono nella libreria e non nell'editor perché servono a tre consumatori che devono raccontare la
 * stessa storia: l'editor che compone il progetto, l'anteprima che lo esegue e i test. Il dominio
 * .NET ha le stesse costanti e le stesse regole; la versione del motore è il nome con cui entrambe
 * le dichiarano, e un progetto che non le condivide non è della stessa versione.
 */

/** Versione del motore di questa prima forma dell'esercizio. */
export const GRID_PATH_ENGINE_VERSION = 'gridpath-1';

/** Tipo di esercizio dichiarato nella definizione (§13.2). */
export const GRID_PATH_EXERCISE_TYPE = 'GridPath';

/** Versione dello schema di definizione compresa da questa versione del motore. */
export const GRID_PATH_SCHEMA_VERSION = 1;

/** Orientamento del personaggio: i quattro valori del motore. */
export type GridDirection = 'North' | 'East' | 'South' | 'West';

/** Cella della griglia: origine in alto a sinistra, `x` a destra e `y` in basso (§13.2). */
export interface GridCell {
  readonly x: number;
  readonly y: number;
}

/** Partenza: una cella con l'orientamento iniziale. */
export interface GridStart extends GridCell {
  readonly direction: GridDirection;
}

/** Scena di un esercizio `GridPath` (§13.2). */
export interface GridPathScene {
  readonly width: number;
  readonly height: number;
  readonly start: GridStart;
  readonly obstacles: readonly GridCell[];
  readonly goal: GridCell;
}

/** Limiti didattici della definizione (§13.2). */
export interface GridPathLimits {
  readonly maxBlocks: number;
  readonly maxNestingDepth: number;
  readonly maxExecutionSteps: number;
  readonly maxActiveDurationMs: number;
}

/** Cella adiacente nella direzione corrente; può uscire dalla griglia (§13.2). */
export function step(position: GridCell, direction: GridDirection): GridCell {
  switch (direction) {
    case 'North':
      return { x: position.x, y: position.y - 1 };
    case 'East':
      return { x: position.x + 1, y: position.y };
    case 'South':
      return { x: position.x, y: position.y + 1 };
    case 'West':
      return { x: position.x - 1, y: position.y };
  }
}

/**
 * Orientamento dopo una rotazione di 90 gradi. Le rotazioni cambiano soltanto l'orientamento
 * (§13.2), quindi la posizione resta quella di prima.
 */
export function turnDirection(direction: GridDirection, turn: 'Left' | 'Right'): GridDirection {
  const order: readonly GridDirection[] = ['North', 'East', 'South', 'West'];
  const index = order.indexOf(direction);
  const shift = turn === 'Right' ? 1 : order.length - 1;

  return order[(index + shift) % order.length];
}

/** Vero se la cella è dentro la griglia. */
export function isInside(scene: Pick<GridPathScene, 'width' | 'height'>, cell: GridCell): boolean {
  return cell.x >= 0 && cell.y >= 0 && cell.x < scene.width && cell.y < scene.height;
}

/** Vero se la cella coincide con una delle celle indicate. */
export function contains(cells: readonly GridCell[], cell: GridCell): boolean {
  return cells.some((other) => other.x === cell.x && other.y === cell.y);
}
