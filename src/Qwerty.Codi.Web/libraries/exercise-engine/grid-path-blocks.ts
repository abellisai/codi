import * as Blockly from 'blockly';
import { GRID_PATH_ENGINE_VERSION } from '@exercise-engine/grid-path-scene';

/**
 * Catalogo dei blocchi della versione `gridpath-1` (§13.4). I nomi dei tipi, dei campi e
 * dell'ingresso devono coincidere con quelli del dominio .NET (`ExerciseBlockCatalog`), perché il
 * server verifica il progetto che l'editor produce: due nomi diversi per lo stesso blocco
 * significherebbero un progetto salvato che il server rifiuta.
 */
export const GRID_PATH_BLOCK_TYPES = [
  'aula_start',
  'aula_move',
  'aula_turn',
  'aula_repeat',
] as const;

/** Tipo di un blocco del catalogo. */
export type GridPathBlockType = (typeof GRID_PATH_BLOCK_TYPES)[number];

/** Campo del verso di rotazione di `aula_turn` (§13.4). */
export const TURN_FIELD = 'TURN';

/** Campo con il numero di ripetizioni di `aula_repeat` (§13.4). */
export const REPEAT_COUNT_FIELD = 'COUNT';

/** Ingresso che contiene la sequenza ripetuta di `aula_repeat` (§13.4). */
export const REPEAT_BODY_INPUT = 'DO';

/** Conteggio minimo e massimo di `aula_repeat` (§13.4). */
export const MIN_REPEAT_COUNT = 1;
export const MAX_REPEAT_COUNT = 20;

/** Blocchi ammessi da una versione del motore; vuoto se la versione è sconosciuta. */
export function blocksForEngine(engineVersion: string): readonly GridPathBlockType[] {
  return engineVersion === GRID_PATH_ENGINE_VERSION ? GRID_PATH_BLOCK_TYPES : [];
}

let registered = false;

/**
 * Registra i blocchi del catalogo. Va invocata prima di montare un workspace: la definizione vive
 * in un registro globale di Blockly, e registrarla di nuovo a ogni navigazione non aggiunge nulla.
 */
export function registerGridPathBlocks(): void {
  if (registered) {
    return;
  }

  Blockly.common.defineBlocksWithJsonArray([
    {
      type: 'aula_start',
      message0: 'quando si avvia',
      // Il punto di ingresso non si attacca sotto un altro blocco ma apre la catena: è così che il
      // progetto ha un inizio riconoscibile senza che il server debba indovinarlo.
      previousStatement: null,
      nextStatement: null,
      colour: 120,
      tooltip: 'Punto di ingresso: il programma comincia da qui.',
      helpUrl: '',
    },
    {
      type: 'aula_move',
      message0: 'fai un passo',
      previousStatement: null,
      nextStatement: null,
      colour: 200,
      tooltip: 'Un passo nella direzione corrente.',
      helpUrl: '',
    },
    {
      type: 'aula_turn',
      message0: 'gira a %1',
      args0: [
        {
          type: 'field_dropdown',
          name: TURN_FIELD,
          options: [
            ['sinistra', 'Left'],
            ['destra', 'Right'],
          ],
        },
      ],
      previousStatement: null,
      nextStatement: null,
      colour: 290,
      tooltip: 'Rotazione di 90 gradi: la posizione non cambia.',
      helpUrl: '',
    },
    {
      type: 'aula_repeat',
      message0: 'ripeti %1 volte',
      args0: [
        {
          type: 'field_number',
          name: REPEAT_COUNT_FIELD,
          value: 2,
          min: MIN_REPEAT_COUNT,
          max: MAX_REPEAT_COUNT,
          precision: 1,
        },
      ],
      message1: 'fai %1',
      args1: [{ type: 'input_statement', name: REPEAT_BODY_INPUT }],
      previousStatement: null,
      nextStatement: null,
      colour: 40,
      tooltip: `Ripete i blocchi interni, da ${String(MIN_REPEAT_COUNT)} a ${String(MAX_REPEAT_COUNT)} volte.`,
      helpUrl: '',
    },
  ]);

  registered = true;
}

/**
 * Cassetto dei blocchi disponibili: solo quelli che la definizione mette a disposizione, nell'ordine
 * del catalogo. Il cassetto è una scelta dell'editor; ciò che il progetto può contenere lo decide
 * comunque il server, quindi un blocco trascinato per errore non entra in una bozza valida.
 */
export function buildGridPathToolbox(
  allowedBlocks: readonly string[],
): Blockly.utils.toolbox.ToolboxDefinition {
  return {
    kind: 'flyoutToolbox',
    contents: GRID_PATH_BLOCK_TYPES.filter((type) => allowedBlocks.includes(type)).map((type) => ({
      kind: 'block',
      type,
    })),
  };
}
