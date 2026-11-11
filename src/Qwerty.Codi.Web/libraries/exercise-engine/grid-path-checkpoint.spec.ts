import {
  GRID_PATH_CHECKPOINT_SCHEMA_VERSION,
  GridPathCheckpointError,
  isGridPathCheckpointState,
  parseGridPathCheckpointState,
  type GridPathCheckpointState,
} from './grid-path-checkpoint';
import { compileGridPathProgram, type GridPathRules } from './grid-path-program';
import { createGridPathRun, resumeGridPathRun } from './grid-path-runner';
import type { GridPathScene } from './grid-path-scene';

/**
 * Prove del checkpoint e della ripresa (§13.7, §19.4): un checkpoint è ripristinabile soltanto con
 * lo stesso progetto e la stessa versione del motore, e una corsa ripresa deve dare lo stesso esito
 * di una corsa continua. Il tempo trascorso in pausa non deve consumare la durata attiva.
 */

const rules: GridPathRules = {
  engineVersion: 'gridpath-1',
  allowedBlocks: ['aula_start', 'aula_move', 'aula_turn', 'aula_repeat'],
  maxBlocks: 30,
  maxNestingDepth: 8,
};

const scene: GridPathScene = {
  width: 5,
  height: 5,
  start: { x: 0, y: 0, direction: 'East' },
  obstacles: [],
  goal: { x: 4, y: 0 },
};

/** Progetto: ripeti due volte «due passi», poi un altro passo: raggiunge il traguardo al quarto. */
function program(): ReturnType<typeof compileGridPathProgram> {
  return compileGridPathProgram(
    workspace(chain(start('start'), repeat('repeat-1', 2, [move('move-1'), move('move-2')]))),
    rules,
  );
}

describe('checkpoint del motore', () => {
  it('descrive schema, motore, motivo e confine di passo', () => {
    const run = createGridPathRun(program(), scene, limits(), () => 0);
    run.step();

    const checkpoint = run.checkpoint('Hidden');

    expect(checkpoint.schemaVersion).toBe(GRID_PATH_CHECKPOINT_SCHEMA_VERSION);
    expect(checkpoint.engineVersion).toBe('gridpath-1');
    expect(checkpoint.pauseReason).toBe('Hidden');
    expect(checkpoint.executions).toBe(1);
    expect(checkpoint.position).toEqual({ x: 1, y: 0 });
    expect(checkpoint.frames[0].path).toEqual([]);
    expect(checkpoint.frames[1].path).toEqual([0]);
  });

  it('riprende dallo stesso confine e dà lo stesso esito di una corsa continua', () => {
    const continuous = createGridPathRun(program(), scene, limits(), () => 0);
    continuous.step();
    continuous.step();
    const expected = continuous.runAll();

    const interrupted = createGridPathRun(program(), scene, limits(), () => 0);
    interrupted.step();
    interrupted.step();
    const checkpoint = interrupted.checkpoint('Paused');

    const resumed = resumeGridPathRun(program(), scene, limits(), checkpoint, () => 0);
    const actual = resumed.runAll();

    expect(actual).toEqual(expected);
    expect(actual.outcome).toBe('success');
    expect(actual.position).toEqual({ x: 4, y: 0 });
  });

  it('non consuma la durata attiva mentre è in pausa', () => {
    let now = 0;
    const interrupted = createGridPathRun(
      program(),
      scene,
      { maxExecutionSteps: 1000, maxActiveDurationMs: 50 },
      () => now,
    );
    interrupted.step();
    const checkpoint = interrupted.checkpoint('Paused');

    // Cinque secondi in pausa: senza la conservazione della durata consumata la ripresa partirebbe
    // con il budget già esaurito e terminerebbe per `budget`.
    now = 5000;
    const resumed = resumeGridPathRun(
      program(),
      scene,
      { maxExecutionSteps: 1000, maxActiveDurationMs: 50 },
      checkpoint,
      () => now,
    );

    expect(resumed.runAll().outcome).toBe('success');
  });

  it('rifiuta un checkpoint di un’altra versione dello schema o del motore', () => {
    const valid = createGridPathRun(program(), scene, limits(), () => 0).checkpoint('Paused');

    expect(() => parseGridPathCheckpointState({ ...valid, schemaVersion: 99 })).toThrow(
      GridPathCheckpointError,
    );
    expect(() => parseGridPathCheckpointState({ ...valid, engineVersion: 'gridpath-2' })).toThrow(
      GridPathCheckpointError,
    );
    expect(isGridPathCheckpointState({ ...valid, engineVersion: 'gridpath-2' })).toBe(false);
    expect(isGridPathCheckpointState('non un checkpoint')).toBe(false);
  });

  it('rifiuta un checkpoint che rimanda a una ripetizione inesistente nel progetto', () => {
    const flat = compileGridPathProgram(workspace(chain(start('start'), move('move-1'))), rules);
    const forged: GridPathCheckpointState = {
      schemaVersion: GRID_PATH_CHECKPOINT_SCHEMA_VERSION,
      engineVersion: 'gridpath-1',
      position: { x: 1, y: 0 },
      direction: 'East',
      outcome: 'running',
      executions: 1,
      consumed: 1,
      activeBlockId: 'move-1',
      activeDurationMs: 0,
      pauseReason: 'Paused',
      frames: [
        { path: [], index: 1, remaining: 0 },
        { path: [0], index: 0, remaining: 0 },
      ],
    };

    expect(() => resumeGridPathRun(flat, scene, limits(), forged, () => 0)).toThrow(
      GridPathCheckpointError,
    );
  });
});

function limits(): { maxExecutionSteps: number; maxActiveDurationMs: number } {
  return { maxExecutionSteps: 1000, maxActiveDurationMs: 120000 };
}

function workspace(...blocks: readonly Record<string, unknown>[]): unknown {
  return { blocks: { languageVersion: 0, blocks } };
}

function chain(...blocks: readonly Record<string, unknown>[]): Record<string, unknown> {
  let tail: Record<string, unknown> | undefined;
  for (let index = blocks.length - 1; index >= 0; index -= 1) {
    tail = tail === undefined ? blocks[index] : { ...blocks[index], next: { block: tail } };
  }

  return tail ?? {};
}

function start(id: string): Record<string, unknown> {
  return { type: 'aula_start', id };
}

function move(id: string): Record<string, unknown> {
  return { type: 'aula_move', id };
}

function repeat(
  id: string,
  count: number,
  body: readonly Record<string, unknown>[],
): Record<string, unknown> {
  return {
    type: 'aula_repeat',
    id,
    fields: { COUNT: count },
    inputs: body.length === 0 ? {} : { DO: { block: chain(...body) } },
  };
}
