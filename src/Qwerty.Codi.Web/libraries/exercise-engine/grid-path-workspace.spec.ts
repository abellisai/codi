import * as Blockly from 'blockly';
import {
  registerGridPathBlocks,
  REPEAT_BODY_INPUT,
  REPEAT_COUNT_FIELD,
  TURN_FIELD,
} from './grid-path-blocks';
import { serializeWorkspace } from './workspace-serialization';

/**
 * Contratto fra i blocchi del catalogo e il formato che il server verifica (§13.3, §13.4).
 *
 * Il progetto non si salva «come lo capisce Blockly»: si salva nella forma che il server controlla.
 * Questa prova costruisce i blocchi **dalle definizioni** — non caricando uno stato scritto a mano —
 * e verifica che la serializzazione contenga i nomi dei tipi, dei campi e dell'ingresso che il
 * dominio .NET `ExerciseBlockCatalog` dichiara. Se un campo cambiasse nome da una parte sola, il
 * progetto composto dall'editor verrebbe rifiutato al salvataggio, e questo caso lo segnala prima.
 */
registerGridPathBlocks();

describe('serializzazione del catalogo gridpath-1', () => {
  it('produce la forma che il server verifica: tipi, campi e sequenza interna', () => {
    const workspace = new Blockly.Workspace();
    const start = workspace.newBlock('aula_start');
    const repeat = workspace.newBlock('aula_repeat');
    const turn = workspace.newBlock('aula_turn');
    const body = workspace.newBlock('aula_move');

    repeat.setFieldValue('3', REPEAT_COUNT_FIELD);
    turn.setFieldValue('Left', TURN_FIELD);

    // La composizione passa dalle connessioni di Blockly: è il legame vero fra i blocchi, non una
    // proprietà scritta a mano.
    connect(start, repeat);
    connect(repeat, turn);
    connectStatement(repeat, REPEAT_BODY_INPUT, body);

    const state = JSON.parse(serializeWorkspace(workspace)) as {
      blocks: { languageVersion: number; blocks: Record<string, unknown>[] };
    };

    expect(state.blocks.languageVersion).toBe(0);
    const program = state.blocks.blocks[0] as Record<string, unknown>;
    expect(program['type']).toBe('aula_start');
    expect(typeof program['id']).toBe('string');
    // Blockly aggiunge la posizione al blocco appoggiato: sono metadati di presentazione, e il
    // server li ignora mentre verifica tipi, campi e collegamenti.
    expect(Object.keys(program).sort()).toEqual(['id', 'next', 'type', 'x', 'y']);

    const afterStart = next(program);
    expect(afterStart['type']).toBe('aula_repeat');
    expect(afterStart['fields']).toEqual({ COUNT: 3 });
    expect(Object.keys(afterStart).sort()).toEqual(['fields', 'id', 'inputs', 'next', 'type']);

    const repeatBody = statement(afterStart, REPEAT_BODY_INPUT);
    expect(repeatBody['type']).toBe('aula_move');

    const afterRepeat = next(afterStart);
    expect(afterRepeat['type']).toBe('aula_turn');
    expect(afterRepeat['fields']).toEqual({ TURN: 'Left' });
    expect(Object.keys(afterRepeat).sort()).toEqual(['fields', 'id', 'type']);
  });

  it('conserva la sequenza ripetuta come blocco, senza espanderla', () => {
    const workspace = new Blockly.Workspace();
    const repeat = workspace.newBlock('aula_repeat');
    const body = workspace.newBlock('aula_move');
    connectStatement(repeat, REPEAT_BODY_INPUT, body);

    const state = JSON.parse(serializeWorkspace(workspace)) as {
      blocks: { blocks: Record<string, unknown>[] };
    };

    const instruction = state.blocks.blocks[0];
    // Il ciclo resta un blocco con la sua sequenza: nessuna espansione, come vuole §13.5.
    expect(Object.keys(statement(instruction, REPEAT_BODY_INPUT))).toContain('type');
    expect(statement(instruction, REPEAT_BODY_INPUT)['type']).toBe('aula_move');
  });
});

/** Collega i due blocchi in catena, verificando che le connessioni esistano. */
function connect(program: Blockly.Block, following: Blockly.Block): void {
  const source = program.nextConnection;
  const target = following.previousConnection;
  if (source === null || target === null) {
    throw new Error('I blocchi non hanno le connessioni attese.');
  }

  source.connect(target);
}

/** Collega un blocco dentro la sequenza interna indicata. */
function connectStatement(program: Blockly.Block, input: string, nested: Blockly.Block): void {
  const connection = program.getInput(input)?.connection ?? null;
  const target = nested.previousConnection;
  if (connection === null || target === null) {
    throw new Error('La sequenza interna non ha la connessione attesa.');
  }

  connection.connect(target);
}

/** Blocco collegato alla catena del blocco indicato. */
function next(block: Record<string, unknown>): Record<string, unknown> {
  return (block['next'] as { block: Record<string, unknown> }).block;
}

/** Blocco contenuto in una sequenza interna. */
function statement(block: Record<string, unknown>, name: string): Record<string, unknown> {
  const inputs = block['inputs'] as Record<string, { block: Record<string, unknown> }>;

  return inputs[name].block;
}
