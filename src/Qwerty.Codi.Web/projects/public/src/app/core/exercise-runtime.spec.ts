import { TestBed } from '@angular/core/testing';
import type { Attempt, DesiredState } from '@codi/api';
import type { GridPathCheckpointState } from '@exercise-engine/grid-path-checkpoint';
import { GridPathPlayerRuntime } from '@exercise-engine/grid-path-player';
import type { PlayerEvent, PlayerRequest } from '@exercise-engine/player-protocol';
import { vi } from 'vitest';
import {
  ExerciseRuntime,
  PLAYER_WORKER_FACTORY,
  type LoadedWork,
  type PlayerWorker,
} from './exercise-runtime';

/**
 * Prove del servizio che governa il player (§13.6): traduzione dello stato desiderato in messaggi,
 * avvio una sola volta per direttiva, ripresa da un checkpoint persistito e inoltro degli eventi del
 * worker. Il worker è un doppio: la macchina che esegue davvero è provata altrove.
 */

/** Doppio del worker: registra i messaggi e permette di forgiare gli eventi in ingresso. */
class PlayerWorkerStub implements PlayerWorker {
  onmessage: ((event: MessageEvent<PlayerEvent>) => void) | null = null;
  readonly sent: PlayerRequest[] = [];
  postMessage(message: PlayerRequest): void {
    this.sent.push(message);
  }
  terminate(): void {
    this.terminated = true;
  }
  terminated = false;

  emit(event: PlayerEvent): void {
    this.onmessage?.({ data: event } as MessageEvent<PlayerEvent>);
  }

  /** Evento riferito a questa esecuzione, con l'identità presa da un messaggio già inviato. */
  envelope(): Pick<
    PlayerEvent,
    'protocolVersion' | 'workerInstanceId' | 'attemptId' | 'executionGeneration' | 'runId'
  > {
    const load = this.sent.find((message) => message.type === 'LoadProgram');
    const start = [...this.sent].reverse().find((message) => message.type === 'StartRun');
    const resume = [...this.sent].reverse().find((message) => message.type === 'ResumeRun');

    return {
      protocolVersion: 1,
      workerInstanceId: load?.workerInstanceId ?? '',
      attemptId: load?.attemptId ?? '',
      executionGeneration: load?.executionGeneration ?? '0',
      runId: start?.runId ?? resume?.runId ?? null,
    };
  }
}

const work: LoadedWork = {
  attemptId: 'a1',
  executionGeneration: '0',
  definitionJson: '{"scene":{}}',
  workspaceJson: '{"blocks":[]}',
  projectRevision: '3',
  workspaceHash: 'h1',
};

const ready: DesiredState = {
  view: 'Exercise',
  interactionEnabled: true,
  assignmentId: 'as1',
  attemptId: 'a1',
  teacherPaused: false,
  recoveryBlocked: false,
  executionGeneration: '0',
  executionDirective: null,
};

function directive(id: string, kind: string): DesiredState {
  return {
    ...ready,
    executionDirective: { directiveId: id, kind, executionGeneration: '0', issuedAtUtc: '' },
  };
}

const checkpointState: GridPathCheckpointState = {
  schemaVersion: 1,
  engineVersion: 'gridpath-1',
  position: { x: 2, y: 0 },
  direction: 'East',
  outcome: 'running',
  executions: 2,
  consumed: 3,
  activeBlockId: 'move-1',
  activeDurationMs: 0,
  pauseReason: 'Suspended',
  frames: [{ path: [], index: 1, remaining: 0 }],
};

function attemptWith(checkpointJson: string | null): Attempt {
  return {
    attemptId: 'a1',
    assignmentId: 'as1',
    exerciseVersionId: 'v1',
    attemptNumber: '1',
    status: 'Ready',
    workspaceJson: '{"blocks":[]}',
    workspaceHash: 'h1',
    projectRevision: '3',
    checkpointJson,
    checkpointEngineVersion: checkpointJson === null ? null : 'gridpath-1',
    lastSavedAtUtc: null,
    updatedAtUtc: '',
  };
}

const checkpointEnvelope = JSON.stringify({
  schemaVersion: 1,
  engineVersion: 'gridpath-1',
  attemptId: 'a1',
  exerciseVersionId: 'v1',
  executionGeneration: '0',
  runId: '11111111-1111-1111-1111-111111111111',
  workspaceHash: 'h1',
  projectRevision: '3',
  runtimeStatus: 'Paused',
  runtimeState: checkpointState,
});

describe('ExerciseRuntime', () => {
  let worker: PlayerWorkerStub;
  let createWorker: ReturnType<typeof vi.fn>;
  let runtime: ExerciseRuntime;

  beforeEach(() => {
    worker = new PlayerWorkerStub();
    createWorker = vi.fn(() => worker);
    TestBed.configureTestingModule({
      providers: [{ provide: PLAYER_WORKER_FACTORY, useValue: createWorker }],
    });
    runtime = TestBed.inject(ExerciseRuntime);
  });

  it('carica il progetto nel worker senza avviarlo', () => {
    runtime.loadWork(work);

    const load = worker.sent[0];
    expect(load.type).toBe('LoadProgram');
    expect(load.type === 'LoadProgram' && load.workspaceJson).toBe('{"blocks":[]}');
    expect(load.attemptId).toBe('a1');
    expect(worker.sent.some((message) => message.type === 'StartRun')).toBe(false);
  });

  it('resta inerte per un esercizio che il player non esegue', async () => {
    // L'esercizio che conserva valori compone un progetto a blocchi ma non ha una corsa da
    // comandare: il player non deve creare il worker né dichiarare un errore che non esiste.
    runtime.loadWork({ ...work, definitionJson: '{"engineVersion":"valueprogram-1"}' });

    expect(createWorker).not.toHaveBeenCalled();
    expect(worker.sent).toHaveLength(0);
    expect(runtime.status()).toBe('Ready');
    expect(runtime.scene()).toBeNull();

    expect(await runtime.apply(directive('d1', 'Run'))).toBe('applied');
    expect(createWorker).not.toHaveBeenCalled();
    expect(runtime.status()).toBe('Ready');
  });

  it('avvia una corsa una volta sola per direttiva e ignora una ripetizione', () => {
    runtime.loadWork(work);
    void runtime.apply(directive('d1', 'Run'));
    expect(worker.sent.filter((message) => message.type === 'StartRun')).toHaveLength(1);

    void runtime.apply(directive('d1', 'Run'));
    expect(worker.sent.filter((message) => message.type === 'StartRun')).toHaveLength(1);
  });

  it('riavvia con una corsa nuova quando la direttiva è un riavvio', () => {
    runtime.loadWork(work);
    void runtime.apply(directive('d1', 'Run'));
    const first = runtime.runId();

    void runtime.apply(directive('d2', 'Restart'));

    const starts = worker.sent.filter((message) => message.type === 'StartRun');
    expect(starts).toHaveLength(2);
    expect(runtime.runId()).not.toBe(first);
  });

  it('riprende dal checkpoint persistito invece di ripartire da capo', () => {
    runtime.loadWork(work);
    expect(runtime.restoreCheckpoint(attemptWith(checkpointEnvelope), '0')).toBe(true);

    void runtime.apply(directive('d1', 'Run'));

    const resume = worker.sent.find((message) => message.type === 'ResumeRun');
    expect(resume?.type === 'ResumeRun' && resume.checkpoint.position).toEqual({ x: 2, y: 0 });
    expect(worker.sent.some((message) => message.type === 'StartRun')).toBe(false);
  });

  it('rifiuta un checkpoint di un’altra generazione e riparte dall’inizio', () => {
    runtime.loadWork(work);
    expect(runtime.restoreCheckpoint(attemptWith(checkpointEnvelope), '9')).toBe(false);

    void runtime.apply(directive('d1', 'Run'));

    expect(worker.sent.some((message) => message.type === 'StartRun')).toBe(true);
    expect(worker.sent.some((message) => message.type === 'ResumeRun')).toBe(false);
  });

  it('revoca il permesso senza fermare la corsa in modo deliberato', () => {
    runtime.loadWork(work);
    runtime.tick(true, 10000);
    runtime.revokePermit();

    const permits = worker.sent.filter((message) => message.type === 'UpdatePermit');
    expect(permits.at(-1)).toMatchObject({ permitted: false, validForMs: 0 });
    expect(worker.sent.some((message) => message.type === 'PauseRun')).toBe(false);
  });

  it('invia permessi con identificativo crescente e durata solo quando sono attivi', () => {
    runtime.loadWork(work);
    runtime.tick(true, 5000);
    runtime.tick(false, 0);

    const permits = worker.sent.filter((message) => message.type === 'UpdatePermit');
    expect(permits).toHaveLength(2);
    expect(permits[0]).toMatchObject({ permitId: 1, permitted: true, validForMs: 5000 });
    expect(permits[1]).toMatchObject({ permitId: 2, permitted: false, validForMs: 0 });
  });

  it('ignora gli eventi di un’altra istanza e applica i propri', () => {
    runtime.loadWork(work);
    void runtime.apply(directive('d1', 'Run'));
    const envelope = worker.envelope();

    worker.emit({
      ...envelope,
      workerInstanceId: 'other',
      type: 'RuntimeState',
      runtimeStatus: 'Running',
      scene: sceneAt(4),
    });
    expect(runtime.scene()).toBeNull();

    worker.emit({ ...envelope, type: 'RuntimeState', runtimeStatus: 'Paused', scene: sceneAt(4) });
    expect(runtime.status()).toBe('Paused');
    expect(runtime.scene()?.position).toEqual({ x: 4, y: 0 });

    worker.emit({
      ...envelope,
      type: 'CheckpointCreated',
      runtimeStatus: 'Paused',
      checkpoint: checkpointState,
    });
    expect(runtime.checkpoint()?.checkpoint.executions).toBe(2);
    expect(runtime.checkpoint()?.runId).toBe(envelope.runId);
  });

  it('conferma la pausa quando il worker risponde entro la soglia', async () => {
    runtime.loadWork(work);
    await runtime.apply(directive('d1', 'Run'));
    const envelope = worker.envelope();

    const pending = runtime.apply({ ...ready, teacherPaused: true, interactionEnabled: false });
    expect(worker.sent.some((message) => message.type === 'PauseRun')).toBe(true);

    // Il worker conferma al confine di passo con un nuovo stato: la pausa è applicata, non recuperata.
    worker.emit({ ...envelope, type: 'RuntimeState', runtimeStatus: 'Paused', scene: sceneAt(2) });

    await expect(pending).resolves.toBe('applied');
    expect(runtime.status()).toBe('Paused');
    expect(worker.terminated).toBe(false);
  });

  it('richiede recupero quando il worker non conferma la pausa entro la soglia', async () => {
    vi.useFakeTimers();
    try {
      runtime.loadWork(work);
      await runtime.apply(directive('d1', 'Run'));

      const pending = runtime.apply({ ...ready, teacherPaused: true, interactionEnabled: false });
      await vi.advanceTimersByTimeAsync(500);

      await expect(pending).resolves.toBe('recoveryRequired');
      // Il player è bloccato: il worker non è più affidabile (§13.7).
      expect(runtime.status()).toBe('Paused');
      expect(worker.terminated).toBe(true);
      expect(runtime.problem()).toContain('non è stata confermata');
    } finally {
      vi.useRealTimers();
    }
  });

  it('smaltisce il worker alla distruzione della vista', () => {
    runtime.loadWork(work);
    runtime.dispose();

    expect(worker.terminated).toBe(true);
  });
});

describe('ExerciseRuntime con la macchina del worker vera', () => {
  /** Worker in-process: usa `GridPathPlayerRuntime`, così si prova il confine servizio↔worker. */
  class LoopbackWorker implements PlayerWorker {
    onmessage: ((event: MessageEvent<PlayerEvent>) => void) | null = null;
    private readonly machine = new GridPathPlayerRuntime(
      (event) => this.onmessage?.({ data: event } as MessageEvent<PlayerEvent>),
      { clock: () => 0 },
    );

    postMessage(message: PlayerRequest): void {
      this.machine.handle(message);
    }
    terminate(): void {
      /* Nessuna risorsa da smontare. */
    }
    pump(): void {
      this.machine.pump();
    }
  }

  const definition = JSON.stringify({
    schemaVersion: 1,
    exerciseType: 'GridPath',
    engineVersion: 'gridpath-1',
    title: 'Raggiungi la stella',
    instructions: 'Tre passi verso est.',
    scene: {
      width: 5,
      height: 4,
      start: { x: 0, y: 1, direction: 'East' },
      obstacles: [],
      goal: { x: 3, y: 1 },
    },
    toolbox: { allowedBlocks: ['aula_start', 'aula_move', 'aula_turn', 'aula_repeat'] },
    limits: {
      maxBlocks: 30,
      maxNestingDepth: 8,
      maxExecutionSteps: 1000,
      maxActiveDurationMs: 120000,
    },
    completion: { type: 'ReachCell', x: 3, y: 1 },
    feedback: { success: 'Hai raggiunto la stella!', collision: 'Ops!' },
  });

  const project = JSON.stringify({
    blocks: {
      languageVersion: 0,
      blocks: [
        {
          type: 'aula_start',
          id: 'start',
          next: {
            block: {
              type: 'aula_move',
              id: 'm1',
              next: {
                block: {
                  type: 'aula_move',
                  id: 'm2',
                  next: { block: { type: 'aula_move', id: 'm3' } },
                },
              },
            },
          },
        },
      ],
    },
  });

  it('carica il progetto e lo esegue davvero quando arriva la direttiva', async () => {
    TestBed.resetTestingModule();
    const worker = new LoopbackWorker();
    TestBed.configureTestingModule({
      providers: [{ provide: PLAYER_WORKER_FACTORY, useValue: () => worker }],
    });
    const runtime = TestBed.inject(ExerciseRuntime);

    runtime.loadWork({
      attemptId: 'a1',
      executionGeneration: '0',
      definitionJson: definition,
      workspaceJson: project,
      projectRevision: '1',
      workspaceHash: 'h1',
    });

    // Il worker ha accettato il caricamento: la scena iniziale è quella della definizione.
    expect(runtime.scene()?.position).toMatchObject({ x: 0, y: 1 });

    runtime.tick(true, 10000);
    await runtime.apply({
      view: 'Exercise',
      interactionEnabled: true,
      assignmentId: 'as1',
      attemptId: 'a1',
      teacherPaused: false,
      recoveryBlocked: false,
      executionGeneration: '0',
      executionDirective: {
        directiveId: 'd1',
        kind: 'Run',
        executionGeneration: '0',
        issuedAtUtc: '',
      },
    });

    for (let step = 0; step < 4; step += 1) worker.pump();

    expect(runtime.scene()?.position).toMatchObject({ x: 3, y: 1 });
    expect(runtime.outcome()?.outcome).toBe('success');
    expect(runtime.checkpoint()?.checkpoint.position).toMatchObject({ x: 3, y: 1 });
  });

  it('riprende da sola dopo una sospensione transitoria del permesso', async () => {
    TestBed.resetTestingModule();
    const worker = new LoopbackWorker();
    TestBed.configureTestingModule({
      providers: [{ provide: PLAYER_WORKER_FACTORY, useValue: () => worker }],
    });
    const runtime = TestBed.inject(ExerciseRuntime);

    runtime.loadWork({
      attemptId: 'a1',
      executionGeneration: '0',
      definitionJson: definition,
      workspaceJson: project,
      projectRevision: '1',
      workspaceHash: 'h1',
    });
    runtime.tick(true, 10000);
    await runtime.apply({
      view: 'Exercise',
      interactionEnabled: true,
      assignmentId: 'as1',
      attemptId: 'a1',
      teacherPaused: false,
      recoveryBlocked: false,
      executionGeneration: '0',
      executionDirective: {
        directiveId: 'd1',
        kind: 'Run',
        executionGeneration: '0',
        issuedAtUtc: '',
      },
    });

    worker.pump();
    expect(runtime.status()).toBe('Running');

    // Il permesso cade — perdita di rete o rinnovo in ritardo: la corsa si sospende da sé…
    runtime.revokePermit();
    worker.pump();
    expect(runtime.status()).toBe('Paused');

    // …e riprende quando il permesso torna, senza un nuovo comando del docente (§11.5).
    runtime.tick(true, 10000);
    expect(runtime.status()).toBe('Running');

    for (let step = 0; step < 4; step += 1) worker.pump();
    expect(runtime.outcome()?.outcome).toBe('success');
  });
});

function sceneAt(x: number): {
  position: { x: number; y: number };
  direction: 'East';
  activeBlockId: string | null;
  executions: number;
  consumed: number;
  outcome: 'running';
} {
  return {
    position: { x, y: 0 },
    direction: 'East',
    activeBlockId: 'move-1',
    executions: x,
    consumed: x,
    outcome: 'running',
  };
}
