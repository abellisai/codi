import type { GridPathCheckpointState } from './grid-path-checkpoint';
import { GridPathPlayerRuntime } from './grid-path-player';
import {
  PlayerErrorCode,
  type CheckpointCreatedEvent,
  type PlayerEvent,
  type PlayerRequest,
} from './player-protocol';

/**
 * Prove della macchina del player (§13.6). La macchina è la stessa che vive nel worker: qui le si
 * dà una funzione che raccoglie gli eventi, così il protocollo si prova senza un browser. Le prove
 * coprono il permesso verificato prima di ogni passo, la pausa a un confine, la ripresa e il
 * rifiuto dei messaggi di un'istanza o di una generazione superata.
 */

const definitionJson = JSON.stringify({
  schemaVersion: 1,
  exerciseType: 'GridPath',
  engineVersion: 'gridpath-1',
  title: 'Raggiungi la stella',
  instructions: 'Porta il personaggio sulla stella.',
  scene: {
    width: 5,
    height: 5,
    start: { x: 0, y: 0, direction: 'East' },
    obstacles: [],
    goal: { x: 4, y: 0 },
  },
  toolbox: { allowedBlocks: ['aula_start', 'aula_move', 'aula_turn', 'aula_repeat'] },
  limits: {
    maxBlocks: 30,
    maxNestingDepth: 8,
    maxExecutionSteps: 1000,
    maxActiveDurationMs: 120000,
  },
  completion: { type: 'ReachCell', x: 4, y: 0 },
  feedback: { success: 'Bravo!', collision: 'Attento!' },
});

/** Progetto: ripeti quattro volte un passo, verso il traguardo in (4, 0). */
const workspaceJson = JSON.stringify({
  blocks: {
    languageVersion: 0,
    blocks: [
      {
        type: 'aula_start',
        id: 'start',
        next: {
          block: {
            type: 'aula_repeat',
            id: 'repeat-1',
            fields: { COUNT: 4 },
            inputs: { DO: { block: { type: 'aula_move', id: 'move-1' } } },
          },
        },
      },
    ],
  },
});

const identity = {
  protocolVersion: 1,
  workerInstanceId: 'w1',
  attemptId: 't1',
  executionGeneration: '0',
  runId: null,
} as const;

function load(project: string = workspaceJson): PlayerRequest {
  return {
    ...identity,
    type: 'LoadProgram',
    workspaceJson: project,
    definitionJson,
    projectRevision: '1',
    workspaceHash: 'h1',
  };
}

function start(runId: string | null = 'run-1'): PlayerRequest {
  return { ...identity, type: 'StartRun', runId };
}

function permit(permitId: number, permitted: boolean, validForMs: number): PlayerRequest {
  return { ...identity, type: 'UpdatePermit', permitId, permitted, validForMs };
}

function pause(reason: string): PlayerRequest {
  return { ...identity, type: 'PauseRun', runId: 'run-1', reason };
}

function resume(checkpoint: GridPathCheckpointState): PlayerRequest {
  return { ...identity, type: 'ResumeRun', runId: 'run-1', checkpoint };
}

/** Fa girare il driver finché la corsa non si ferma, con un tetto per non restare appesi. */
function drive(machine: GridPathPlayerRuntime, maxPumps = 1000): void {
  let pumps = 0;
  while (machine.runtimeStatus === 'Running' && pumps < maxPumps) {
    machine.pump();
    pumps += 1;
  }
  expect(pumps).toBeLessThan(maxPumps);
}

function lastCheckpoint(events: readonly PlayerEvent[]): CheckpointCreatedEvent | null {
  for (let index = events.length - 1; index >= 0; index -= 1) {
    const event = events[index];
    if (event.type === 'CheckpointCreated') return event;
  }

  return null;
}

function machineFor(events: PlayerEvent[], now: () => number): GridPathPlayerRuntime {
  return new GridPathPlayerRuntime((event) => events.push(event), {
    workerInstanceId: 'w1',
    clock: now,
    checkpointEvery: 10,
  });
}

describe('macchina del player', () => {
  it('esegue il programma con il permesso e conclude con successo', () => {
    const events: PlayerEvent[] = [];
    const machine = machineFor(events, () => 0);

    machine.handle(load());
    machine.handle(permit(1, true, 10000));
    machine.handle(start());
    drive(machine);

    const finished = events.find((event) => event.type === 'RunFinished');
    expect(finished?.type === 'RunFinished' && finished.outcome).toBe('success');
    expect(machine.runtimeStatus).toBe('Completed');
    expect(machine.scene().position).toEqual({ x: 4, y: 0 });
    expect(events.at(-1)?.type).toBe('RuntimeState');
  });

  it('si sospende con un checkpoint quando il permesso manca', () => {
    const events: PlayerEvent[] = [];
    const machine = machineFor(events, () => 0);

    machine.handle(load());
    machine.handle(start());
    machine.pump();

    expect(machine.runtimeStatus).toBe('Paused');
    expect(lastCheckpoint(events)?.checkpoint.pauseReason).toBe('PermitExpired');
  });

  it('non prolunga la validità con un permesso più vecchio', () => {
    const events: PlayerEvent[] = [];
    let now = 0;
    const machine = machineFor(events, () => now);

    machine.handle(load());
    machine.handle(permit(2, true, 50));
    machine.handle(start());
    now = 60;
    machine.pump();

    expect(machine.runtimeStatus).toBe('Paused');

    machine.handle(permit(1, true, 100000));
    machine.pump();
    expect(machine.runtimeStatus).toBe('Paused');
  });

  it('sospende al confine richiesto e riprende dallo stesso checkpoint', () => {
    const events: PlayerEvent[] = [];
    const machine = machineFor(events, () => 0);

    machine.handle(load());
    machine.handle(permit(1, true, 10000));
    machine.handle(start());
    machine.pump();
    machine.handle(pause('Hidden'));
    machine.pump();

    const checkpoint = lastCheckpoint(events)?.checkpoint;
    expect(machine.runtimeStatus).toBe('Paused');
    expect(checkpoint?.pauseReason).toBe('Hidden');
    expect(checkpoint?.executions).toBe(1);

    machine.handle(resume(checkpoint as GridPathCheckpointState));
    drive(machine);

    expect(machine.runtimeStatus).toBe('Completed');
    expect(machine.scene().position).toEqual({ x: 4, y: 0 });
  });

  it('ignora i messaggi di un’altra istanza o di un’altra generazione', () => {
    const events: PlayerEvent[] = [];
    const machine = machineFor(events, () => 0);

    machine.handle(load());
    machine.handle({ ...start(), workerInstanceId: 'other' } as PlayerRequest);
    machine.handle({ ...start(), executionGeneration: '9' } as PlayerRequest);

    expect(machine.runtimeStatus).toBe('Ready');
    expect(events.some((event) => event.type === 'RunFinished')).toBe(false);
  });

  it('segnala un progetto senza punto di ingresso invece di eseguirlo', () => {
    const events: PlayerEvent[] = [];
    const machine = machineFor(events, () => 0);

    machine.handle(
      load(
        JSON.stringify({
          blocks: { languageVersion: 0, blocks: [{ type: 'aula_move', id: 'move-1' }] },
        }),
      ),
    );

    const error = events.find((event) => event.type === 'RuntimeError');
    expect(error?.type === 'RuntimeError' && error.code).toBe(PlayerErrorCode.ProgramInvalid);
    expect(machine.runtimeStatus).toBe('Error');
  });
});
