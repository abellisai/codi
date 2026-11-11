import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { GridPathRules } from '@exercise-engine/grid-path-program';
import type { GridPathLimits, GridPathScene } from '@exercise-engine/grid-path-scene';
import { ExercisePreview } from './exercise-preview';

/**
 * Prove dell'anteprima dell'autore (§13.1, §13.5): la corsa parte dal progetto composto, avanza di
 * un'operazione visibile alla volta, si ferma all'esito e non produce nulla di persistente.
 *
 * La prova automatica non viene attesa: i casi usano il comando «Passo avanti», che esercita lo
 * stesso avanzamento senza dipendere dal tempo di animazione.
 */

const scene: GridPathScene = {
  width: 4,
  height: 3,
  start: { x: 0, y: 0, direction: 'East' },
  obstacles: [],
  goal: { x: 3, y: 0 },
};

const rules: GridPathRules = {
  engineVersion: 'gridpath-1',
  allowedBlocks: ['aula_start', 'aula_move', 'aula_turn', 'aula_repeat'],
  maxBlocks: 30,
  maxNestingDepth: 8,
};

const limits: GridPathLimits = {
  maxBlocks: 30,
  maxNestingDepth: 8,
  maxExecutionSteps: 100,
  maxActiveDurationMs: 120000,
};

describe('ExercisePreview', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ExercisePreview] }).compileComponents();
  });

  it('mostra la scena con partenza, traguardo e celle libere', () => {
    const fixture = render(program(move('move-1')));

    expect(cells(fixture).length).toBe(12);
    expect(label(fixture, 'Riga 1, colonna 1')).toBe(
      'Riga 1, colonna 1: partenza, orientamento Est, personaggio rivolto a Est',
    );
    expect(label(fixture, 'Riga 1, colonna 4')).toBe('Riga 1, colonna 4: traguardo');
    expect(label(fixture, 'Riga 2, colonna 2')).toBe('Riga 2, colonna 2: libera');
  });

  it('avanza di un passo e segnala il blocco in esecuzione', () => {
    const fixture = render(program(chain(move('move-1'), move('move-2'))));
    const highlighted: (string | null)[] = [];
    fixture.componentInstance.activeBlockChanged.subscribe((blockId) => {
      highlighted.push(blockId);
    });

    click(fixture, 'Passo avanti');
    fixture.detectChanges();

    expect(label(fixture, 'Riga 1, colonna 2')).toContain('personaggio');
    expect(highlighted).toContain('move-1');
  });

  it('mostra il messaggio di successo della definizione e smette di evidenziare', () => {
    const fixture = render(program(chain(move('move-1'), move('move-2'), move('move-3'))), {
      successMessage: 'Hai raggiunto la stella!',
    });

    step(fixture, 3);

    expect(outcome(fixture)).toBe('Hai raggiunto la stella!');
    expect(label(fixture, 'Riga 1, colonna 4')).toContain('personaggio');
    expect(stepButton(fixture).disabled).toBe(true);
  });

  it('mostra il messaggio di collisione quando il passo è bloccato da un ostacolo', () => {
    const fixture = render(program(move('move-1')), {
      scene: { ...scene, obstacles: [{ x: 1, y: 0 }] },
      collisionMessage: 'C’è un ostacolo: prova a cambiare percorso.',
    });

    click(fixture, 'Passo avanti');
    fixture.detectChanges();

    expect(outcome(fixture)).toBe('C’è un ostacolo: prova a cambiare percorso.');
    // La posizione precedente è mantenuta: l'ostacolo non si attraversa.
    expect(label(fixture, 'Riga 1, colonna 1')).toContain('personaggio');
  });

  it('spiega che il progetto non è eseguibile quando manca il punto di ingresso', () => {
    const fixture = render(
      JSON.stringify({ blocks: { languageVersion: 0, blocks: [move('move-1')] } }),
    );

    click(fixture, 'Passo avanti');
    fixture.detectChanges();

    expect(problem(fixture)).toContain('quando si avvia');
    expect(outcome(fixture)).toBe('');
  });

  it('riporta la scena alla partenza con «Ricomincia»', () => {
    const fixture = render(program(chain(move('move-1'), move('move-2'))));

    step(fixture, 2);
    expect(label(fixture, 'Riga 1, colonna 3')).toContain('personaggio');

    click(fixture, 'Ricomincia');
    fixture.detectChanges();

    expect(label(fixture, 'Riga 1, colonna 1')).toContain('personaggio');
    expect(outcome(fixture)).toBe('');
  });

  it('avvia la prova automatica e la ferma con «Ferma»', () => {
    const fixture = render(program(move('move-1')));

    click(fixture, 'Avvia la prova');
    fixture.detectChanges();

    expect(playButton(fixture).disabled).toBe(true);
    expect(stopButton(fixture).disabled).toBe(false);

    click(fixture, 'Ferma');
    fixture.detectChanges();

    expect(stopButton(fixture).disabled).toBe(true);
    fixture.destroy();
  });
});

interface RenderOverrides {
  readonly scene?: GridPathScene;
  readonly successMessage?: string;
  readonly collisionMessage?: string;
}

/** Progetto che comincia dal punto di ingresso e prosegue con i blocchi indicati. */
function program(...blocks: readonly Record<string, unknown>[]): string {
  return JSON.stringify({
    blocks: { languageVersion: 0, blocks: [chain(start('start-1'), ...blocks)] },
  });
}

function render(
  workspaceJson: string,
  overrides: RenderOverrides = {},
): ComponentFixture<ExercisePreview> {
  const fixture = TestBed.createComponent(ExercisePreview);
  fixture.componentRef.setInput('scene', overrides.scene ?? scene);
  fixture.componentRef.setInput('rules', rules);
  fixture.componentRef.setInput('limits', limits);
  fixture.componentRef.setInput('workspaceJson', workspaceJson);
  fixture.componentRef.setInput('successMessage', overrides.successMessage ?? 'Bravo!');
  fixture.componentRef.setInput('collisionMessage', overrides.collisionMessage ?? 'Ostacolo!');
  fixture.detectChanges();

  return fixture;
}

function step(fixture: ComponentFixture<ExercisePreview>, times: number): void {
  for (let index = 0; index < times; index += 1) {
    click(fixture, 'Passo avanti');
  }

  fixture.detectChanges();
}

function click(fixture: ComponentFixture<ExercisePreview>, label: string): void {
  const button = buttons(fixture).find((candidate) => candidate.textContent.trim() === label);
  expect(button, `Pulsante «${label}» non trovato`).toBeDefined();
  button?.click();
}

function buttons(fixture: ComponentFixture<ExercisePreview>): HTMLButtonElement[] {
  return [...fixture.nativeElement.querySelectorAll('button')] as HTMLButtonElement[];
}

function playButton(fixture: ComponentFixture<ExercisePreview>): HTMLButtonElement {
  return buttons(fixture)[0];
}

function stepButton(fixture: ComponentFixture<ExercisePreview>): HTMLButtonElement {
  return buttons(fixture)[1];
}

function stopButton(fixture: ComponentFixture<ExercisePreview>): HTMLButtonElement {
  return buttons(fixture)[2];
}

function cells(fixture: ComponentFixture<ExercisePreview>): HTMLElement[] {
  return [...fixture.nativeElement.querySelectorAll('.cell')] as HTMLElement[];
}

/** Etichetta della cella che comincia come indicato: la lettura non dipende dall'ordine. */
function label(fixture: ComponentFixture<ExercisePreview>, where: string): string {
  const found = cells(fixture).find((candidate) =>
    (candidate.getAttribute('aria-label') ?? '').startsWith(where),
  );
  expect(found, `Cella «${where}» non trovata`).toBeDefined();

  return found?.getAttribute('aria-label') ?? '';
}

function outcome(fixture: ComponentFixture<ExercisePreview>): string {
  return (fixture.nativeElement.querySelector('.outcome') as HTMLElement).textContent.trim();
}

function problem(fixture: ComponentFixture<ExercisePreview>): string {
  return (
    (fixture.nativeElement.querySelector('.problem') as HTMLElement | null)?.textContent.trim() ??
    ''
  );
}

function chain(...blocks: readonly Record<string, unknown>[]): Record<string, unknown> {
  let tail: Record<string, unknown> | undefined;
  for (let index = blocks.length - 1; index >= 0; index -= 1) {
    tail = tail === undefined ? blocks[index] : { ...blocks[index], next: { block: tail } };
  }

  return tail ?? {};
}

function move(id: string): Record<string, unknown> {
  return { type: 'aula_move', id };
}

function start(id: string): Record<string, unknown> {
  return { type: 'aula_start', id };
}
