import {
  MAX_REPEAT_COUNT,
  MIN_REPEAT_COUNT,
  REPEAT_BODY_INPUT,
  REPEAT_COUNT_FIELD,
  TURN_FIELD,
  blocksForEngine,
} from '@exercise-engine/grid-path-blocks';

/**
 * Compilazione del progetto a blocchi in istruzioni didattiche (§13.5). Il progetto è lo stato di
 * Blockly, non la definizione e non un checkpoint: sono tre formati distinti e non vanno scambiati
 * (§13.3). Qui si produce l'unica rappresentazione che il motore sa eseguire, e ogni istruzione
 * conserva `sourceBlockId`, così l'anteprima può evidenziare il blocco in esecuzione.
 *
 * Non si genera codice da eseguire — niente `eval` e niente `Function` — e i cicli non vengono
 * espansi in elenchi di istruzioni: la ripetizione resta un'istruzione con una sequenza interna e
 * sarà il motore a contarne i giri con i propri frame (§13.5).
 */

/** Regole che il progetto deve rispettare: le stesse che il server applica quando lo conserva. */
export interface GridPathRules {
  readonly engineVersion: string;
  readonly allowedBlocks: readonly string[];
  readonly maxBlocks: number;
  readonly maxNestingDepth: number;
}

/** Istruzione didattica compresa dal motore. */
export interface GridPathInstruction {
  readonly op: 'move' | 'turn' | 'repeat';
  readonly sourceBlockId: string;
  readonly steps?: number;
  readonly turn?: 'Left' | 'Right';
  readonly count?: number;
  readonly body?: readonly GridPathInstruction[];
}

/** Programma compilato, pronto per l'interprete. */
export interface GridPathProgram {
  readonly irVersion: 1;
  readonly instructions: readonly GridPathInstruction[];
  readonly blockCount: number;
  readonly maxDepth: number;
  /** Blocchi presenti nel workspace ma non collegati al punto di ingresso: non vengono eseguiti. */
  readonly disconnectedBlocks: number;
}

/** Errore di composizione del progetto: il messaggio è la diagnosi mostrata al docente. */
export class GridPathProgramError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'GridPathProgramError';
  }
}

/**
 * Compila il progetto. Un progetto vuoto o senza punto di ingresso è un errore e non un programma
 * che non fa nulla: §13.4 dice che la mancanza di `aula_start` impedisce la prova, e una prova che
 * parte e non esegue niente sarebbe una diagnosi peggiore di un rifiuto spiegato.
 */
export function compileGridPathProgram(workspace: unknown, rules: GridPathRules): GridPathProgram {
  const known = new Set<string>(blocksForEngine(rules.engineVersion));
  const allowed = new Set(rules.allowedBlocks);
  const topLevel = topLevelBlocks(workspace);
  const counter = { blocks: 0, disconnected: 0 };

  if (topLevel.length === 0) {
    throw new GridPathProgramError(
      'Il progetto è vuoto: aggiungi il blocco «quando si avvia» e i blocchi da eseguire.',
    );
  }

  // Il conteggio comprende i blocchi scollegati: concorrono al limite per evitare workspace
  // illimitati (§13.4), e non vengono eseguiti.
  for (const block of topLevel) {
    countBlocks(block, counter, rules);
  }

  const starts = topLevel.filter((block) => text(block['type']) === 'aula_start');
  if (starts.length === 0) {
    throw new GridPathProgramError(
      'Manca il blocco «quando si avvia»: senza punto di ingresso il programma non parte.',
    );
  }

  if (starts.length > 1) {
    throw new GridPathProgramError(
      `Il progetto ha ${String(starts.length)} blocchi «quando si avvia»: il punto di ingresso è uno solo.`,
    );
  }

  // La catena eseguibile comincia dal blocco che segue il punto di ingresso: `aula_start` non è
  // un'istruzione, è il segnaposto da cui il programma parte.
  const entry = connected(starts[0], 'next');
  const compiled =
    entry === null
      ? { instructions: [], count: 0, maxDepth: 1 }
      : compileChain(entry, rules, known, allowed, 1);

  // Il punto di ingresso non è un'istruzione eseguita: non conta fra i blocchi scollegati.
  counter.disconnected = Math.max(0, counter.blocks - compiled.count - 1);

  return {
    irVersion: 1,
    instructions: compiled.instructions,
    blockCount: counter.blocks,
    maxDepth: compiled.maxDepth,
    disconnectedBlocks: counter.disconnected,
  };
}

/** Verifica il blocco e i suoi campi, restituendo il tipo riconosciuto. */
function accept(
  block: Record<string, unknown>,
  rules: GridPathRules,
  known: ReadonlySet<string>,
  allowed: ReadonlySet<string>,
): string {
  const type = text(block['type']);
  if (type === null) {
    throw new GridPathProgramError('Il progetto contiene un blocco senza tipo.');
  }

  if (!known.has(type)) {
    throw new GridPathProgramError(
      `Il progetto usa il blocco «${type}», che la versione del motore «${rules.engineVersion}» non conosce.`,
    );
  }

  if (!allowed.has(type)) {
    throw new GridPathProgramError(
      `Il progetto usa il blocco «${type}», che questo esercizio non mette a disposizione.`,
    );
  }

  return type;
}

/** Conta ricorsivamente un blocco e quelli collegati, senza eseguire nulla. */
function countBlocks(
  block: UnknownBlock,
  counter: { blocks: number; disconnected: number },
  rules: GridPathRules,
): void {
  counter.blocks += 1;
  if (counter.blocks > rules.maxBlocks) {
    throw new GridPathProgramError(
      `Il progetto ha più di ${String(rules.maxBlocks)} blocchi, il limite dell'esercizio.`,
    );
  }

  for (const connection of connections(block)) {
    countBlocks(connection, counter, rules);
  }
}

/** Compila la catena di istruzioni che comincia da un blocco. */
function compileChain(
  first: UnknownBlock,
  rules: GridPathRules,
  known: ReadonlySet<string>,
  allowed: ReadonlySet<string>,
  depth: number,
): Compilation {
  const instructions: GridPathInstruction[] = [];
  let maxDepth = depth;
  let count = 0;
  let current: UnknownBlock | null = first;

  while (current !== null) {
    const type = accept(current, rules, known, allowed);
    const sourceBlockId = text(current['id']) ?? '';
    count += 1;

    if (type === 'aula_start') {
      // Il punto di ingresso apre la catena e non può comparire dentro di essa.
      throw new GridPathProgramError(
        'Il blocco «quando si avvia» non può stare dentro il programma: è il punto di partenza.',
      );
    }

    if (type === 'aula_move') {
      instructions.push({ op: 'move', sourceBlockId, steps: 1 });
    } else if (type === 'aula_turn') {
      instructions.push({ op: 'turn', sourceBlockId, turn: turnField(current) });
    } else if (type === 'aula_repeat') {
      const body = statement(current, REPEAT_BODY_INPUT);
      if (depth + 1 > rules.maxNestingDepth && body !== null) {
        throw new GridPathProgramError(
          `Il progetto annida i blocchi oltre il limite di ${String(rules.maxNestingDepth)} livelli.`,
        );
      }

      const nested =
        body === null
          ? { instructions: [], count: 0, maxDepth: depth }
          : compileChain(body, rules, known, allowed, depth + 1);

      instructions.push({
        op: 'repeat',
        sourceBlockId,
        count: repeatCount(current),
        body: nested.instructions,
      });
      maxDepth = Math.max(maxDepth, nested.maxDepth);
      count += nested.count;
    }

    current = connected(current, 'next');
  }

  return { instructions, count, maxDepth };
}

/** Interfaccia ridotta di un blocco letto dal JSON, per non spargere asserzioni nel codice. */
type UnknownBlock = Record<string, unknown>;

interface Compilation {
  readonly instructions: readonly GridPathInstruction[];
  readonly count: number;
  readonly maxDepth: number;
}

/** Blocchi di primo livello del workspace. */
function topLevelBlocks(workspace: unknown): readonly UnknownBlock[] {
  const root = asRecord(workspace);
  const container = root === null ? null : asRecord(root['blocks']);
  const list = container === null ? undefined : container['blocks'];

  return Array.isArray(list) ? list.filter(isRecord) : [];
}

/** Tutte le connessioni di un blocco: la catena e gli ingressi. */
function connections(block: UnknownBlock): readonly UnknownBlock[] {
  const found: UnknownBlock[] = [];
  const next = connected(block, 'next');
  if (next !== null) {
    found.push(next);
  }

  const inputs = asRecord(block['inputs']);
  if (inputs !== null) {
    for (const value of Object.values(inputs)) {
      const input = asRecord(value);
      const nested = input === null ? null : asRecord(input['block']);
      if (nested !== null) {
        found.push(nested);
      }
    }
  }

  return found;
}

/** Blocco collegato a una proprietà (`next`) del blocco indicato. */
function connected(block: UnknownBlock, property: string): UnknownBlock | null {
  const connection = asRecord(block[property]);

  return connection === null ? null : asRecord(connection['block']);
}

/** Blocco contenuto in una sequenza interna, se c'è. */
function statement(block: UnknownBlock, name: string): UnknownBlock | null {
  const inputs = asRecord(block['inputs']);

  return inputs === null ? null : connected(inputs, name);
}

/** Verso di rotazione dichiarato dal blocco. */
function turnField(block: UnknownBlock): 'Left' | 'Right' {
  const value = field(block, TURN_FIELD);
  const text = value === null ? null : String(value);
  if (text !== 'Left' && text !== 'Right') {
    throw new GridPathProgramError(
      `Il blocco «gira» ha il campo «${TURN_FIELD}» con valore «${text ?? 'mancante'}»: scegli sinistra o destra.`,
    );
  }

  return text;
}

/** Numero di ripetizioni dichiarato dal blocco, entro l'intervallo ammesso. */
function repeatCount(block: UnknownBlock): number {
  const value = field(block, REPEAT_COUNT_FIELD);
  const count = typeof value === 'number' ? value : Number(value);
  if (!Number.isInteger(count) || count < MIN_REPEAT_COUNT || count > MAX_REPEAT_COUNT) {
    throw new GridPathProgramError(
      `Il blocco «ripeti» ha il campo «${REPEAT_COUNT_FIELD}» con valore «${value === null ? 'mancante' : String(value)}»: è un numero intero da ${String(MIN_REPEAT_COUNT)} a ${String(MAX_REPEAT_COUNT)}.`,
    );
  }

  return count;
}

/** Valore di un campo del blocco, se dichiarato. */
function field(block: UnknownBlock, name: string): unknown {
  const fields = asRecord(block['fields']);

  return fields === null ? null : (fields[name] ?? null);
}

function text(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

function isRecord(value: unknown): value is UnknownBlock {
  return asRecord(value) !== null;
}

function asRecord(value: unknown): UnknownBlock | null {
  return typeof value === 'object' && value !== null ? (value as UnknownBlock) : null;
}
