import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TrialRun } from './trial-run';

/** Definizione di prova: griglia 5×5, partenza in alto a sinistra, traguardo in (4, 0). */
function definition(overrides: Record<string, unknown> = {}): string {
  return JSON.stringify({
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
    ...overrides,
  });
}

/** Progetto: ripeti quattro volte un passo, verso il traguardo in (4, 0). */
const program = JSON.stringify({
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

/** Progetto che non si compila: manca il punto di ingresso. */
const programWithoutStart = JSON.stringify({
  blocks: { languageVersion: 0, blocks: [{ type: 'aula_move', id: 'move-1' }] },
});

describe('Prova locale del programma', () => {
  let trial: TrialRun;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({});
    trial = TestBed.inject(TrialRun);
  });

  afterEach(() => {
    trial.reset();
    vi.useRealTimers();
  });

  it('parte dalla partenza e scorre i passi fino al traguardo', () => {
    trial.play(definition(), program);

    expect(trial.active()).toBe(true);
    expect(trial.running()).toBe(true);
    expect(trial.position()).toEqual({ x: 0, y: 0 });

    // Quattro passi, uno ogni intervallo: è lo stesso ritmo osservabile del player (§13.6).
    for (let step = 0; step < 4; step++) {
      vi.advanceTimersByTime(300);
    }

    expect(trial.running()).toBe(false);
    expect(trial.position()).toEqual({ x: 4, y: 0 });
    expect(trial.steps()).toBe(4);
    expect(trial.message()).toBe('Bravo!');
  });

  it('un passo alla volta avanza di una sola operazione', () => {
    trial.step(definition(), program);
    trial.step(definition(), program);

    expect(trial.position()).toEqual({ x: 2, y: 0 });
    expect(trial.direction()).toBe('East');
    expect(trial.running()).toBe(false);
  });

  it('con un ostacolo davanti usa il messaggio della definizione', () => {
    const withObstacle = definition({
      scene: {
        width: 5,
        height: 5,
        start: { x: 0, y: 0, direction: 'East' },
        obstacles: [{ x: 1, y: 0 }],
        goal: { x: 4, y: 0 },
      },
    });

    trial.step(withObstacle, program);
    trial.step(withObstacle, program);

    expect(trial.message()).toBe('Attento!');
    expect(trial.running()).toBe(false);
  });

  it('un progetto senza punto di ingresso si spiega invece di muoversi', () => {
    trial.play(definition(), programWithoutStart);

    expect(trial.active()).toBe(false);
    expect(trial.running()).toBe(false);
    expect(trial.problem()).not.toBe('');
    expect(trial.position()).toBeNull();
  });

  it('fermare conserva la posizione, azzerare riporta alla partenza', () => {
    trial.play(definition(), program);
    vi.advanceTimersByTime(600);
    const reached = trial.position();
    expect(reached).toEqual({ x: 2, y: 0 });

    trial.stop();
    expect(trial.running()).toBe(false);
    expect(trial.position()).toEqual(reached);

    trial.reset();
    expect(trial.active()).toBe(false);
    expect(trial.position()).toBeNull();
    expect(trial.steps()).toBe(0);
  });

  it('una prova nuova riparte dalla partenza anche dopo un esito', () => {
    trial.play(definition(), program);
    for (let step = 0; step < 4; step++) {
      vi.advanceTimersByTime(300);
    }
    expect(trial.message()).toBe('Bravo!');

    // Dopo l'esito, «Un passo» mostra di nuovo il programma dall'inizio: è ciò che si aspetta un
    // bambino che preme ancora, e non un pulsante che non fa nulla.
    trial.step(definition(), program);

    expect(trial.position()).toEqual({ x: 1, y: 0 });
    expect(trial.message()).toBe('');
  });

  it('la prova non tocca il worker: nessun messaggio e nessuna risorsa condivisa', () => {
    // Il servizio non conosce il protocollo del player: se comparisse un `postMessage` qui, la prova
    // starebbe passando dal canale dei comandi invece che dal motore locale (§13.1).
    expect('postMessage' in trial).toBe(false);
    trial.play(definition(), program);
    vi.advanceTimersByTime(300);

    expect(trial.position()).toEqual({ x: 1, y: 0 });
  });
});
