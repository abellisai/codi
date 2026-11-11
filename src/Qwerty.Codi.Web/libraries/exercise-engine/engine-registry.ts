import type * as Blockly from 'blockly';
import {
  GRID_PATH_ENGINE_VERSION,
  GRID_PATH_EXERCISE_TYPE,
} from '@exercise-engine/grid-path-scene';
import {
  GRID_PATH_BLOCK_TYPES,
  buildGridPathToolbox,
  registerGridPathBlocks,
} from '@exercise-engine/grid-path-blocks';
import {
  VALUE_PROGRAM_BLOCK_TYPES,
  VALUE_PROGRAM_ENGINE_VERSION,
  VALUE_PROGRAM_EXERCISE_TYPE,
  buildValueProgramToolbox,
  registerValueProgramBlocks,
} from '@exercise-engine/value-blocks';

/**
 * Registro delle versioni del motore conosciute da questa build dell'applicazione.
 *
 * Il catalogo è **per versione del motore** (§13.4): una versione registrata porta con sé il tipo di
 * attività e — quando ne ha uno — il catalogo dei blocchi e il cassetto. Aggiungere una versione
 * significa descriverla qui, e i consumatori — pagina dell'alunno, editor, anteprima — non
 * contengono più l'elenco dei tipi.
 *
 * La controparte lato server è `ExerciseEngineRegistry`: le due build devono conoscere le stesse
 * versioni, altrimenti una distribuirebbe un esercizio che l'altra non sa svolgere.
 */
export interface ExerciseEngineDescriptor {
  /** Tipo di esercizio dichiarato nella definizione, come lo espone il server. */
  readonly exerciseType: string;
  /** Versione del motore dichiarata nella definizione. */
  readonly engineVersion: string;
  /**
   * Vero quando l'esercizio si svolge componendo un progetto a blocchi: la pagina dell'alunno monta
   * il workspace e governa il worker del player. Falso per un esercizio dichiarativo, che si risponde
   * con un valore e non ha né blocchi da comporre né esecuzione da sospendere.
   */
  readonly blockly: boolean;
  /** Blocchi del catalogo di questa versione; vuoto per un tipo senza blocchi. */
  readonly blockTypes: readonly string[];
  /** Registra i blocchi di questa versione nel registro globale di Blockly. */
  readonly registerBlocks: () => void;
  /** Cassetto dei blocchi che l'esercizio mette a disposizione. */
  readonly buildToolbox: (
    allowedBlocks: readonly string[],
  ) => Blockly.utils.toolbox.ToolboxDefinition;
}

const GRID_PATH_ENGINE: ExerciseEngineDescriptor = {
  exerciseType: GRID_PATH_EXERCISE_TYPE,
  engineVersion: GRID_PATH_ENGINE_VERSION,
  blockly: true,
  blockTypes: GRID_PATH_BLOCK_TYPES,
  registerBlocks: registerGridPathBlocks,
  buildToolbox: buildGridPathToolbox,
};

/**
 * Esercizio dichiarativo: si legge una rappresentazione e si produce una risposta. Non ha blocchi —
 * il catalogo vuoto è il suo catalogo, non un catalogo mancante — e non passa dal player: la
 * correzione la fa il server, e non c'è un'esecuzione da sospendere e riprendere.
 */
export const QUIZ_EXERCISE_TYPE = 'Quiz';
export const QUIZ_ENGINE_VERSION = 'quiz-1';

const QUIZ_ENGINE: ExerciseEngineDescriptor = {
  exerciseType: QUIZ_EXERCISE_TYPE,
  engineVersion: QUIZ_ENGINE_VERSION,
  blockly: false,
  blockTypes: [],
  registerBlocks: () => {
    // Nessun blocco da registrare. La registrazione resta una funzione perché i consumatori non
    // debbano sapere quali versioni ne abbiano una.
  },
  buildToolbox: () => ({ kind: 'flyoutToolbox', contents: [] }),
};

/**
 * Programma che conserva valori: si compone a blocchi come il percorso, ma lo stato non è una
 * posizione su una griglia — sono variabili con il loro valore — e la corsa può fermarsi in attesa
 * di un valore che l'alunno deve fornire. Il catalogo è il più grande del prodotto, e ha due
 * famiglie: i comandi, che stanno in catena, e le espressioni, che si annidano dentro i comandi.
 */
const VALUE_PROGRAM_ENGINE: ExerciseEngineDescriptor = {
  exerciseType: VALUE_PROGRAM_EXERCISE_TYPE,
  engineVersion: VALUE_PROGRAM_ENGINE_VERSION,
  blockly: true,
  blockTypes: VALUE_PROGRAM_BLOCK_TYPES,
  registerBlocks: registerValueProgramBlocks,
  buildToolbox: buildValueProgramToolbox,
};

const ENGINES: readonly ExerciseEngineDescriptor[] = [
  GRID_PATH_ENGINE,
  QUIZ_ENGINE,
  VALUE_PROGRAM_ENGINE,
];
/** Descrizione di una versione del motore, oppure `null` se questa build non la conosce. */
export function findEngine(
  engineVersion: string | null | undefined,
): ExerciseEngineDescriptor | null {
  if (typeof engineVersion !== 'string' || engineVersion === '') {
    return null;
  }

  return ENGINES.find((engine) => engine.engineVersion === engineVersion) ?? null;
}

/** Vero se questa build sa svolgere un esercizio di quella versione del motore. */
export function knowsEngine(engineVersion: string | null | undefined): boolean {
  return findEngine(engineVersion) !== null;
}

/**
 * Descrizione di un **tipo** di esercizio, alla sua versione più recente registrata.
 *
 * È diversa da `findEngine`, che cerca per versione: l'anagrafica dell'esercizio porta il tipo
 * (`GridPath`, `Quiz`), non la versione — la versione vive nella definizione, che può non esistere
 * ancora. Chi deve sapere **che cosa** mostrare prima di leggere una bozza cerca da qui.
 */
export function findEngineByType(
  exerciseType: string | null | undefined,
): ExerciseEngineDescriptor | null {
  if (typeof exerciseType !== 'string' || exerciseType === '') {
    return null;
  }

  return [...ENGINES].reverse().find((engine) => engine.exerciseType === exerciseType) ?? null;
}

/**
 * Registra i blocchi di tutte le versioni conosciute. La registrazione è idempotente — ogni
 * descrizione lo garantisce — quindi chiamarla prima di montare un workspace non aggiunge nulla.
 */
export function registerKnownEngineBlocks(): void {
  for (const engine of ENGINES) {
    engine.registerBlocks();
  }
}

/** Etichetta italiana di un tipo di esercizio, per chi lo sceglie. */
const TYPE_LABELS: Record<string, string> = {
  [GRID_PATH_EXERCISE_TYPE]: 'Percorso sulla griglia',
  [QUIZ_EXERCISE_TYPE]: 'Domanda con risposta',
  [VALUE_PROGRAM_EXERCISE_TYPE]: 'Programma con variabili',
};

/**
 * Etichetta italiana di un tipo, da mostrare dove il tipo si legge e non si sceglie — per esempio
 * la colonna «Tipologia» dell'elenco degli esercizi. Un tipo che questa build non conosce si mostra
 * con il proprio nome tecnico: meglio di una cella vuota, che nasconderebbe un esercizio esistente.
 */
export function exerciseTypeLabel(exerciseType: string | null | undefined): string {
  if (typeof exerciseType !== 'string' || exerciseType === '') {
    return '—';
  }

  return TYPE_LABELS[exerciseType] ?? exerciseType;
}

/**
 * Tipi di esercizio che questa build sa comporre, con la loro etichetta, nell'ordine di
 * registrazione. È la controparte dell'elenco che il server conosce, e si legge dal registro invece
 * di essere scritto a mano: un tipo nuovo che comparisse solo nel registro di un lato sarebbe un
 * esercizio che una delle due build non sa aprire — ed è esattamente il difetto che ha reso
 * necessario il registro (§13.9).
 */
export function knownExerciseTypes(): readonly { value: string; label: string }[] {
  const types: string[] = [];
  for (const engine of ENGINES) {
    if (!types.includes(engine.exerciseType)) types.push(engine.exerciseType);
  }

  return types.map((value) => ({ value, label: TYPE_LABELS[value] ?? value }));
}
