import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { Attempt, ValueProgramOutcome } from '@codi/api';
import { ApiService } from '@codi/api';
import { vi } from 'vitest';
import { AttemptAutosave } from '../core/attempt-autosave';
import { AttemptTransport } from '../core/attempt-transport';
import { WorkspacePreviewPublisher } from '../core/workspace-preview';
import { ValueProgramPanel } from './value-program-panel';

/**
 * Pannello del programma a valori: prova locale, correzione del server, salvataggio tramite
 * l'autosave del tentativo. Il caso che conta è la via di salvataggio: il pannello non apre una
 * seconda scrittura diretta, che correrebbe in parallelo all'autosave con revisione e versione di
 * riga divergenti e il server rifiuterebbe una delle due.
 */
describe('Pannello del programma a valori', () => {
  const definition = JSON.stringify({
    schemaVersion: 1,
    exerciseType: 'ValueProgram',
    variables: [{ name: 'Numero', initialValue: 3 }],
    limits: { maxExecutionSteps: 100 },
  });

  const workspaceJson = JSON.stringify({
    blocks: {
      languageVersion: 0,
      blocks: [
        {
          type: 'val_start',
          id: 's',
          // La catena annida il blocco successivo: `next` è una proprietà il cui valore è il
          // blocco, non un identificatore.
          next: {
            block: {
              type: 'val_show',
              id: 'h',
              inputs: { VALUE: { block: { type: 'val_number', id: 'n', fields: { NUM: '7' } } } },
            },
          },
        },
      ],
    },
  });

  const attempt: Attempt = {
    attemptId: 'a1',
    assignmentId: 'as1',
    exerciseVersionId: 'v1',
    attemptNumber: '1',
    status: 'Ready',
    workspaceJson,
    workspaceHash: 'impronta',
    projectRevision: '1',
    checkpointJson: null,
    checkpointEngineVersion: null,
    lastSavedAtUtc: null,
    updatedAtUtc: '2026-11-14T10:00:00Z',
    rowVersion: '1',
  };

  let order: string[] = [];
  let directSaves: Record<string, unknown>[] = [];
  let anteprime: string[] = [];
  let outcome: ValueProgramOutcome = {
    passed: true,
    successMessage: 'Giusto!',
    wrongMessage: 'Riprova.',
    problem: '',
  };

  beforeEach(() => {
    order = [];
    directSaves = [];
    anteprime = [];
    outcome = { passed: true, successMessage: 'Giusto!', wrongMessage: 'Riprova.', problem: '' };
    TestBed.configureTestingModule({
      providers: [
        {
          // Il salvataggio è dell'autosave: qui si verifica che il pannello passi da lì.
          provide: AttemptAutosave,
          useValue: {
            flush: () => {
              order.push('salvataggio');
              return Promise.resolve();
            },
          },
        },
        {
          // Presente per dimostrare che il pannello non la usa più: una scrittura diretta dal
          // pannello è il difetto che questo test impedisce.
          provide: AttemptTransport,
          useValue: {
            save: (_id: string, _kind: string, body: Record<string, unknown>) => {
              directSaves.push(body);
              return Promise.resolve({
                attemptId: _id,
                saveId: 's',
                rowVersion: '2',
                projectRevision: '2',
              });
            },
          },
        },
        {
          provide: WorkspacePreviewPublisher,
          useValue: {
            publishNow: (workspace: string) => {
              anteprime.push(workspace);
            },
          },
        },
        {
          provide: ApiService,
          useValue: {
            executeQuery: (name: string) => {
              order.push('domanda');
              return Promise.resolve(outcome);
            },
          },
        },
      ],
    });
  });

  async function mount(enabled: boolean): Promise<Fixture> {
    const fixture = TestBed.createComponent(ValueProgramPanel);
    fixture.componentRef.setInput('definitionJson', definition);
    fixture.componentRef.setInput('attempt', attempt);
    fixture.componentRef.setInput('workspaceJson', workspaceJson);
    fixture.componentRef.setInput('enabled', enabled);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  }

  type Fixture = ComponentFixture<ValueProgramPanel>;

  function button(page: Fixture, etichetta: string): HTMLButtonElement {
    const found = Array.from(
      (page.nativeElement as HTMLElement).querySelectorAll('button'),
    ).find((b) => (b.textContent ?? '').trim().includes(etichetta));
    if (!found) throw new Error(`Manca il pulsante «${etichetta}».`);
    return found;
  }

  it("«Prova il programma» salva con l'autosave del tentativo, non con una scrittura diretta", async () => {
    const page = await mount(true);
    button(page, 'Prova il programma').click();
    await vi.waitFor(() => expect(order.length).toBeGreaterThan(0));

    expect(order).toContain('salvataggio');
    expect(directSaves).toEqual([]);
    // L'anteprima del docente mostra ciò che il programma ha mostrato, non i blocchi.
    expect(anteprime).toHaveLength(1);
    expect(anteprime[0]).toContain('7');
  });

  it("«Fai correggere» assicura il salvataggio prima di chiedere la correzione al server", async () => {
    const page = await mount(true);
    button(page, 'Prova il programma').click();
    await vi.waitFor(() => expect(order).toContain('salvataggio'));
    button(page, 'Fai correggere').click();
    await vi.waitFor(() => expect(order).toContain('domanda'));

    expect(directSaves).toEqual([]);
    const primaDomanda = order.indexOf('domanda');
    expect(order.slice(0, primaDomanda)).toContain('salvataggio');
    expect((page.nativeElement as HTMLElement).textContent).toContain('Giusto!');
  });

  it('non salva nulla quando il maestro non lascia lavorare', async () => {
    const page = await mount(false);
    button(page, 'Fai correggere').click();
    await new Promise((resolve) => setTimeout(resolve, 0));
    page.detectChanges();

    expect(directSaves).toEqual([]);
    expect(order).toEqual([]);
  });
});
