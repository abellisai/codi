import { blocksForEngine } from './grid-path-blocks';
import {
  compileGridPathProgram,
  GridPathProgramError,
  type GridPathRules,
} from './grid-path-program';
import { createGridPathRun, describeOutcome } from './grid-path-runner';
import type { GridPathScene } from './grid-path-scene';

/**
 * Prove del motore `gridpath-1` (§13.4, §13.5): compilazione del progetto in istruzioni, rifiuti
 * della composizione e interpretazione a passi con i suoi limiti.
 *
 * Sono prove pure: nessun workspace Blockly viene montato, perché l'oggetto in ingresso è lo stato
 * JSON prodotto dalla serializzazione — lo stesso documento che il server verifica e che il
 * docente salva.
 */

const rules: GridPathRules = {
  engineVersion: 'gridpath-1',
  allowedBlocks: ['aula_start', 'aula_move', 'aula_turn', 'aula_repeat'],
  maxBlocks: 30,
  maxNestingDepth: 8,
};

/** Scena 5×5, partenza in alto a sinistra verso est, traguardo in (3, 0). */
const scene: GridPathScene = {
  width: 5,
  height: 5,
  start: { x: 0, y: 0, direction: 'East' },
  obstacles: [],
  goal: { x: 3, y: 0 },
};

describe('catalogo dei blocchi', () => {
  it('conosce i quattro blocchi della versione dichiarata', () => {
    expect(blocksForEngine('gridpath-1')).toEqual([
      'aula_start',
      'aula_move',
      'aula_turn',
      'aula_repeat',
    ]);
  });

  it('non conosce blocchi di una versione futura', () => {
    expect(blocksForEngine('gridpath-2')).toEqual([]);
  });
});

describe('compilazione del progetto', () => {
  it('compila catena, rotazione e ripetizione conservando il blocco di origine', () => {
    const program = compileGridPathProgram(
      workspace(chain(start('start'), move('move-1'), repeat('repeat-1', 3, [turn('turn-1')]))),
      rules,
    );

    expect(program.blockCount).toBe(4);
    expect(program.disconnectedBlocks).toBe(0);
    expect(program.instructions).toEqual([
      { op: 'move', sourceBlockId: 'move-1', steps: 1 },
      {
        op: 'repeat',
        sourceBlockId: 'repeat-1',
        count: 3,
        body: [{ op: 'turn', sourceBlockId: 'turn-1', turn: 'Left' }],
      },
    ]);
  });

  it('conta i blocchi scollegati senza eseguirli', () => {
    const program = compileGridPathProgram(
      workspace(chain(start('start'), move('move-1')), move('move-2')),
      rules,
    );

    expect(program.blockCount).toBe(3);
    expect(program.disconnectedBlocks).toBe(1);
    expect(program.instructions).toEqual([{ op: 'move', sourceBlockId: 'move-1', steps: 1 }]);
  });

  it('rifiuta un progetto vuoto', () => {
    expect(() => compileGridPathProgram(workspace(), rules)).toThrow(GridPathProgramError);
    expect(() => compileGridPathProgram(workspace(), rules)).toThrow('vuoto');
  });

  it('rifiuta un progetto senza punto di ingresso', () => {
    expect(() => compileGridPathProgram(workspace(move('move-1')), rules)).toThrow(
      'quando si avvia',
    );
  });

  it('rifiuta due punti di ingresso', () => {
    expect(() =>
      compileGridPathProgram(workspace(start('start-1'), start('start-2')), rules),
    ).toThrow('uno solo');
  });

  it('rifiuta un blocco che la versione del motore non conosce', () => {
    expect(() =>
      compileGridPathProgram(workspace(chain(start('start'), raw('aula_jump', 'jump-1'))), rules),
    ).toThrow('non conosce');
  });

  it('rifiuta un blocco che l’esercizio non mette a disposizione', () => {
    const restricted: GridPathRules = { ...rules, allowedBlocks: ['aula_start', 'aula_move'] };

    expect(() =>
      compileGridPathProgram(workspace(chain(start('start'), turn('turn-1'))), restricted),
    ).toThrow('non mette a disposizione');
  });

  it('rifiuta più blocchi del limite dichiarato', () => {
    const tiny: GridPathRules = { ...rules, maxBlocks: 2 };

    expect(() =>
      compileGridPathProgram(
        workspace(chain(start('start'), move('move-1'), move('move-2'))),
        tiny,
      ),
    ).toThrow('limite');
  });

  it('rifiuta un annidamento oltre il limite dichiarato', () => {
    const shallow: GridPathRules = { ...rules, maxNestingDepth: 1 };
    const nested = repeat('repeat-1', 2, [repeat('repeat-2', 2, [move('move-1')])]);

    expect(() => compileGridPathProgram(workspace(chain(start('start'), nested)), shallow)).toThrow(
      'annida',
    );
  });

  it('rifiuta un verso di rotazione non ammesso', () => {
    const wrong = raw('aula_turn', 'turn-1', { fields: { TURN: 'Up' } });

    expect(() => compileGridPathProgram(workspace(chain(start('start'), wrong)), rules)).toThrow(
      'TURN',
    );
  });

  it('rifiuta un numero di ripetizioni fuori intervallo', () => {
    expect(() =>
      compileGridPathProgram(workspace(chain(start('start'), repeat('repeat-1', 21, []))), rules),
    ).toThrow('COUNT');
  });
});

describe('esecuzione', () => {
  it('conclude con successo quando raggiunge il traguardo', () => {
    const program = compileGridPathProgram(
      workspace(chain(start('start'), move('move-1'), move('move-2'), move('move-3'))),
      rules,
    );
    const run = createGridPathRun(program, scene, limits());

    const state = run.runAll();

    expect(state.outcome).toBe('success');
    expect(state.position).toEqual({ x: 3, y: 0 });
    // Il traguardo chiude la corsa: il resto del programma non viene eseguito.
    expect(state.executions).toBe(3);
    expect(state.activeBlockId).toBe('move-3');
  });

  it('termina con collisione e mantiene la posizione precedente', () => {
    const program = compileGridPathProgram(workspace(chain(start('start'), move('move-1'))), rules);
    const blocked: GridPathScene = { ...scene, obstacles: [{ x: 1, y: 0 }] };
    const run = createGridPathRun(program, blocked, limits());

    const state = run.runAll();

    expect(state.outcome).toBe('collision');
    expect(state.position).toEqual({ x: 0, y: 0 });
    expect(state.executions).toBe(0);
  });

  it('distingue l’uscita dalla griglia', () => {
    const program = compileGridPathProgram(workspace(chain(start('start'), move('move-1'))), rules);
    const narrow: GridPathScene = { ...scene, width: 1, goal: { x: 0, y: 2 } };
    const run = createGridPathRun(program, narrow, limits());

    expect(run.runAll().outcome).toBe('leftTheGrid');
  });

  it('ripete la sequenza interna il numero di volte dichiarato', () => {
    const program = compileGridPathProgram(
      workspace(chain(start('start'), repeat('repeat-1', 3, [move('move-1')]))),
      rules,
    );
    const run = createGridPathRun(program, scene, limits());

    const state = run.runAll();

    // Tre passi e tre giri di controllo: anche il controllo consuma budget (§13.5).
    expect(state.position).toEqual({ x: 3, y: 0 });
    expect(state.outcome).toBe('success');
    expect(state.consumed).toBe(6);
    expect(state.executions).toBe(3);
  });

  it('consuma budget anche in un ciclo senza istruzioni', () => {
    const program = compileGridPathProgram(
      workspace(chain(start('start'), repeat('repeat-1', 5, []))),
      rules,
    );
    const run = createGridPathRun(program, scene, {
      maxExecutionSteps: 3,
      maxActiveDurationMs: 1000,
    });

    const state = run.runAll();

    expect(state.outcome).toBe('budget');
    expect(state.consumed).toBe(3);
  });

  it('esaurisce il budget prima di superare i passi ammessi', () => {
    const program = compileGridPathProgram(
      workspace(chain(start('start'), move('move-1'), move('move-2'), move('move-3'))),
      rules,
    );
    const run = createGridPathRun(program, scene, {
      maxExecutionSteps: 2,
      maxActiveDurationMs: 1000,
    });

    const state = run.runAll();

    expect(state.outcome).toBe('budget');
    expect(state.consumed).toBeLessThanOrEqual(2);
  });

  it('esaurisce il budget di durata attiva con l’orologio iniettato', () => {
    const program = compileGridPathProgram(workspace(chain(start('start'), move('move-1'))), rules);
    let now = 0;
    const run = createGridPathRun(
      program,
      scene,
      { maxExecutionSteps: 100, maxActiveDurationMs: 50 },
      () => {
        now += 60;

        return now;
      },
    );

    expect(run.runAll().outcome).toBe('budget');
  });

  it('chiude con esito «finito» un programma che non raggiunge il traguardo', () => {
    const program = compileGridPathProgram(workspace(chain(start('start'), turn('turn-1'))), rules);
    const run = createGridPathRun(program, scene, limits());

    const state = run.runAll();

    expect(state.outcome).toBe('finished');
    expect(state.direction).toBe('North');
    expect(describeOutcome(state.outcome)).toBe(
      'Il programma è finito senza raggiungere il traguardo.',
    );
  });

  it('avanza di una sola operazione visibile per passo', () => {
    const program = compileGridPathProgram(
      workspace(chain(start('start'), repeat('repeat-1', 2, [move('move-1')]))),
      rules,
    );
    const run = createGridPathRun(program, scene, limits());

    const first = run.step();
    expect(first.executions).toBe(1);
    expect(first.position).toEqual({ x: 1, y: 0 });
    expect(run.done()).toBe(false);

    const second = run.step();
    expect(second.executions).toBe(2);
    expect(second.position).toEqual({ x: 2, y: 0 });

    // Dopo la fine, ripetere il passo non riparte: la corsa è conclusa.
    run.runAll();
    expect(run.state().position).toEqual({ x: 2, y: 0 });
    expect(run.done()).toBe(true);
  });
});

function limits(): { maxExecutionSteps: number; maxActiveDurationMs: number } {
  return { maxExecutionSteps: 1000, maxActiveDurationMs: 120000 };
}

function workspace(...blocks: readonly Record<string, unknown>[]): unknown {
  return { blocks: { languageVersion: 0, blocks } };
}

/** Concatena i blocchi in una catena `next`, dall'ultimo al primo. */
function chain(...blocks: readonly Record<string, unknown>[]): Record<string, unknown> {
  let tail: Record<string, unknown> | undefined;
  for (let index = blocks.length - 1; index >= 0; index -= 1) {
    tail = tail === undefined ? blocks[index] : { ...blocks[index], next: { block: tail } };
  }

  return tail ?? {};
}

function start(id: string): Record<string, unknown> {
  return raw('aula_start', id);
}

function move(id: string): Record<string, unknown> {
  return raw('aula_move', id);
}

function turn(id: string): Record<string, unknown> {
  return raw('aula_turn', id, { fields: { TURN: 'Left' } });
}

function repeat(
  id: string,
  count: number,
  body: readonly Record<string, unknown>[],
): Record<string, unknown> {
  return raw('aula_repeat', id, {
    fields: { COUNT: count },
    inputs: body.length === 0 ? {} : { DO: { block: chain(...body) } },
  });
}

function raw(
  type: string,
  id: string,
  extra: Record<string, unknown> = {},
): Record<string, unknown> {
  return { type, id, ...extra };
}
