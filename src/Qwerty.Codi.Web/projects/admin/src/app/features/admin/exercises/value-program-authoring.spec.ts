import { Component, input, output } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { ApiError, ApiService, type ExerciseDraft, type ExerciseVersion } from '@codi/api';
import { VALUE_PROGRAM_BLOCK_TYPES } from '@exercise-engine/value-blocks';
import { ExercisePublication } from './exercise-publication/exercise-publication';
import { ValueProgramEditor } from './value-program-editor';
import { ValueProgramProgramEditor } from './value-program-program-editor/value-program-program-editor';

/** Il motore Blockly ha verifiche proprie. Qui si controllano form, revisioni e pubblicazione. */
@Component({ selector: 'app-value-program-program-editor', template: '' })
class WorkspaceStub {
  readonly allowedBlocks = input.required<readonly string[]>();
  readonly workspaceJson = input.required<string>();
  readonly activeBlockId = input<string | null>(null);
  readonly readOnly = input(false);
  readonly workspaceChanged = output<string>();
}

const workspace = JSON.stringify({
  blocks: { languageVersion: 0, blocks: [{ type: 'val_start', id: 's', x: 0, y: 0 }] },
});
const definition = {
  schemaVersion: 1,
  exerciseType: 'ValueProgram',
  engineVersion: 'valueprogram-1',
  title: 'La macchina dei punti',
  instructions: 'Conta le carte maggiori di cinque.',
  variables: [
    { name: 'Numero', initialValue: 0 },
    { name: 'Punti', initialValue: 0 },
  ],
  testCases: [{ inputs: [3, 8, 6], expectedOutput: ['2'] }],
  toolbox: { allowedBlocks: [...VALUE_PROGRAM_BLOCK_TYPES] },
  limits: {
    maxBlocks: 40,
    maxNestingDepth: 8,
    maxExecutionSteps: 1000,
    maxActiveDurationMs: 120000,
  },
  feedback: { success: 'Bravo!', collision: 'Riprova.' },
};

class ApiStub {
  readonly commands: { name: string; payload: Record<string, unknown> }[] = [];
  draft: ExerciseDraft | null = {
    exerciseId: 'e1',
    definitionJson: JSON.stringify(definition),
    workspaceJson: workspace,
    rowVersion: '9007199254740993',
    updatedAtUtc: '2026-10-16T12:00:00Z',
  };
  solution: string | null = null;
  versions: ExerciseVersion[] = [];
  copiedSolution: string | null = null;
  failure: ApiError | null = null;
  loadFailure: ApiError | null = null;
  saveGate: Promise<void> | null = null;

  async executeQuery(name: string): Promise<unknown> {
    if (this.loadFailure !== null) throw this.loadFailure;
    if (name === 'GetExerciseDraft') {
      if (this.draft === null) throw new ApiError('ResourceNotFound', 404, 'Bozza assente.');
      return this.draft;
    }
    if (name === 'GetReferenceSolution') return { exerciseId: 'e1', workspaceJson: this.solution };
    if (name === 'ListExerciseVersions') return this.versions;
    if (name === 'GetExercise')
      return { publicationStatus: this.versions.length === 0 ? 'Draft' : 'Available' };
    throw new Error(`Lettura inattesa: ${name}`);
  }

  async executeCommand(name: string, payload: Record<string, unknown>): Promise<unknown> {
    this.commands.push({ name, payload });
    if (this.failure !== null) throw this.failure;
    if (name === 'SaveExerciseDraft') {
      await this.saveGate;
      if (this.draft !== null && payload['baseRowVersion'] !== this.draft.rowVersion) {
        throw new ApiError('EntityVersionRequired', 428, 'Versione della bozza richiesta.');
      }
      this.draft = {
        exerciseId: 'e1',
        definitionJson: String(payload['definitionJson']),
        workspaceJson: String(payload['workspaceJson']),
        rowVersion: this.draft === null ? '1' : String(BigInt(this.draft.rowVersion) + 1n),
        updatedAtUtc: '2026-10-16T12:01:00Z',
      };
      this.solution = payload['referenceSolutionJson'] as string | null;
      return this.draft;
    }
    if (name === 'ValidateExerciseDraft') return { isValid: true, problems: [] };
    if (name === 'PublishExerciseVersion') {
      const draft = this.draft;
      if (draft === null || payload['baseRowVersion'] !== draft.rowVersion)
        throw new Error('Revisione non salvata.');
      const version: ExerciseVersion = {
        id: 'v1',
        exerciseId: 'e1',
        versionNumber: '1',
        definitionJson: draft.definitionJson,
        workspaceJson: draft.workspaceJson,
        exerciseType: 'ValueProgram',
        engineVersion: 'valueprogram-1',
        schemaVersion: '1',
        contentHash: '12345678',
        createdAt: '2026-10-16T12:00:00Z',
        authorNotes: null,
      };
      this.versions = [version];
      return version;
    }
    if (name === 'CopyExerciseVersionToDraft') {
      this.draft = {
        ...this.draft!,
        definitionJson: this.versions[0]!.definitionJson,
        workspaceJson: this.versions[0]!.workspaceJson,
        rowVersion: '9007199254741000',
      };
      this.solution = this.copiedSolution;
      return this.draft;
    }
    throw new Error(`Comando inatteso: ${name}`);
  }
}

describe('Autore ValueProgram: salvataggio e pubblicazione', () => {
  let api: ApiStub;
  let fixture: ComponentFixture<ValueProgramEditor>;
  let page: HTMLElement;

  beforeEach(async () => {
    api = new ApiStub();
    TestBed.configureTestingModule({
      providers: [{ provide: ApiService, useValue: api }, provideRouter([])],
    });
    for (const component of [ValueProgramEditor, ExercisePublication]) {
      TestBed.overrideComponent(component, {
        remove: { imports: [ValueProgramProgramEditor] },
        add: { imports: [WorkspaceStub] },
      });
    }
    await TestBed.compileComponents();
  });

  it('carica una bozza esistente con pubblicazione disponibile e senza modifiche da salvare', async () => {
    await mount();
    expect(value('#value-variables')).toBe('Numero = 0\nPunti = 0');
    expect(value('#value-cases')).toBe('3, 8, 6 -> 2');
    expect(button('Salva bozza').disabled).toBe(true);
    expect(button('Rendi disponibile').disabled).toBe(false);
  });

  it('crea senza versione e aggiorna due volte con la revisione restituita dal server', async () => {
    api.draft = null;
    await mount();
    expect(button('Rendi disponibile').disabled).toBe(false);
    fill('#value-instructions', 'Leggi e somma uno.');
    fill('#value-variables', 'Numero = 0');
    fill('#value-cases', '6 -> 7');
    await submit();
    expect(api.commands[0]!.payload['baseRowVersion']).toBeUndefined();
    expect(api.commands[0]!.name).toBe('SaveExerciseDraft');
    expect(button('Rendi disponibile').disabled).toBe(false);
    fill('#value-success', 'Corretto!');
    await submit();
    fill('#value-wrong', 'Controlla il calcolo.');
    await submit();
    expect(api.commands.map((command) => command.payload['baseRowVersion'])).toEqual([
      undefined,
      '1',
      '2',
    ]);
  });

  it('conserva le revisioni a 64 bit nei salvataggi consecutivi', async () => {
    await mount();
    fill('#value-instructions', 'Conta le carte.');
    await submit();
    fill('#value-success', 'Hai contato bene!');
    await submit();
    expect(api.commands.map((command) => command.payload['baseRowVersion'])).toEqual([
      '9007199254740993',
      '9007199254740994',
    ]);
    expect(button('Rendi disponibile').disabled).toBe(false);
  });

  it('salva e pubblica anche le modifiche al programma iniziale con un solo comando', async () => {
    await mount();
    const changed = JSON.stringify({ blocks: { languageVersion: 0, blocks: [] } });
    editor().workspaceChanged.emit(changed);
    await settle();
    expect(button('Rendi disponibile').disabled).toBe(false);
    await click('Rendi disponibile');
    expect(api.commands[0]!.payload['workspaceJson']).toBe(changed);
    expect(api.commands.map((command) => command.name)).toEqual([
      'SaveExerciseDraft',
      'ValidateExerciseDraft',
      'PublishExerciseVersion',
    ]);
    expect(button('Rendi disponibile').disabled).toBe(false);
  });

  it('verifica i dati correnti, pubblica e mostra il programma della versione in sola lettura', async () => {
    await mount();
    await click('Verifica la bozza');
    expect(api.commands[0]!.payload['referenceSolutionJson']).toBeNull();
    expect(api.commands[0]!.payload['workspaceJson']).toBe(workspace);
    expect(JSON.parse(String(api.commands[0]!.payload['definitionJson'])).testCases).toEqual(
      definition.testCases,
    );
    await click('Rendi disponibile');
    expect(api.commands.at(-1)?.payload['baseRowVersion']).toBe('9007199254740993');
    // La conferma è lo stato nella barra: il badge passa a «Disponibile».
    expect(page.querySelector('.authoring-state')?.textContent?.trim()).toBe('Disponibile');
    await click('Guarda la versione');
    const inspected = fixture.debugElement.queryAll(By.directive(WorkspaceStub)).at(-1)!
      .componentInstance as WorkspaceStub;
    expect(inspected.readOnly()).toBe(true);
    expect(inspected.workspaceJson()).toBe(workspace);
    expect(page.querySelector('.version-preview [role="alert"]')).toBeNull();
  });

  it('ricarica una versione copiata e conserva catalogo, limiti e soluzione privata al salvataggio successivo', async () => {
    await mount();
    await click('Rendi disponibile');
    const copied = {
      ...definition,
      instructions: 'Mostra i punti iniziali.',
      variables: [{ name: 'Punti', initialValue: 3 }],
      testCases: [{ inputs: [], expectedOutput: ['3'] }],
      limits: { ...definition.limits, maxExecutionSteps: 400 },
      toolbox: { allowedBlocks: ['val_start', 'val_show', 'val_variable'] },
    };
    api.versions = [{ ...api.versions[0]!, definitionJson: JSON.stringify(copied) }];
    api.copiedSolution = workspace;
    await click('Copia nell’editor');
    expect(value('#value-instructions')).toBe(copied.instructions);
    expect(value('#value-variables')).toBe('Punti = 3');
    expect(value('#value-cases')).toBe(' -> 3');
    expect(button('Rendi disponibile').disabled).toBe(false);
    fill('#value-success', 'Perfetto!');
    await submit();
    const payload = api.commands.at(-1)!.payload;
    expect(payload['baseRowVersion']).toBe('9007199254741000');
    expect(payload['referenceSolutionJson']).toBe(workspace);
    const saved = JSON.parse(String(payload['definitionJson']));
    expect(saved.limits).toEqual(copied.limits);
    expect(saved.toolbox).toEqual(copied.toolbox);
  });

  it('conserva le modifiche su conflitto senza ripetere il comando o pubblicare', async () => {
    await mount();
    api.failure = new ApiError('EntityVersionConflict', 412, 'La bozza è cambiata. Ricaricala.');
    fill('#value-instructions', 'La mia consegna.');
    await submit();
    expect(api.commands).toHaveLength(1);
    expect(value('#value-instructions')).toBe('La mia consegna.');
    expect(page.querySelector('[role="alert"]')?.textContent).toContain('La bozza è cambiata.');
    expect(button('Rendi disponibile').disabled).toBe(false);
    expect(button('Salva bozza').disabled).toBe(false);
  });

  it('mostra errori di form associati al campo e non salva dati incompleti', async () => {
    await mount();
    fill('#value-cases', 'sei -> 7');
    await submit();
    const field = page.querySelector('#value-cases')!;
    expect(field.getAttribute('aria-invalid')).toBe('true');
    const descriptionIds = field.getAttribute('aria-describedby')?.split(' ') ?? [];
    expect(
      descriptionIds.some((id) => page.querySelector('#' + id)?.textContent?.includes('sei')),
    ).toBe(true);
    expect(api.commands).toHaveLength(0);
    expect(button('Rendi disponibile').disabled).toBe(false);
  });

  it('impedisce il doppio invio e la pubblicazione durante un salvataggio', async () => {
    let release!: () => void;
    api.saveGate = new Promise<void>((resolve) => {
      release = resolve;
    });
    await mount();
    fill('#value-instructions', 'Una consegna aggiornata.');
    await submit();
    expect((page.querySelector('#value-instructions') as HTMLTextAreaElement).disabled).toBe(true);
    expect(button('Rendi disponibile').disabled).toBe(true);
    await submit();
    expect(api.commands).toHaveLength(1);
    release();
    await settle();
    expect(button('Rendi disponibile').disabled).toBe(false);
  });

  it('non permette di sovrascrivere una bozza illeggibile o di un formato futuro', async () => {
    api.draft = {
      ...api.draft!,
      definitionJson: JSON.stringify({ ...definition, schemaVersion: 2 }),
    };
    await mount();
    expect(page.textContent).toContain('La bozza salvata non è leggibile');
    expect((page.querySelector('#value-instructions') as HTMLTextAreaElement).disabled).toBe(true);
    expect(button('Rendi disponibile').disabled).toBe(true);
    await submit();
    expect(api.commands).toHaveLength(0);
  });

  async function mount(): Promise<void> {
    fixture = TestBed.createComponent(ValueProgramEditor);
    fixture.componentRef.setInput('exerciseId', 'e1');
    fixture.componentRef.setInput('title', definition.title);
    fixture.detectChanges();
    page = fixture.nativeElement as HTMLElement;
    await settle();
  }
  async function settle(): Promise<void> {
    for (let round = 0; round < 2; round++) {
      await new Promise((resolve) => setTimeout(resolve, 0));
      fixture.detectChanges();
    }
  }
  function value(selector: string): string {
    return (page.querySelector(selector) as HTMLInputElement).value;
  }
  function fill(selector: string, text: string): void {
    const field = page.querySelector(selector) as HTMLInputElement;
    field.value = text;
    field.dispatchEvent(new Event('input'));
  }
  function button(label: string): HTMLButtonElement {
    if (label === 'Rendi disponibile')
      return page.querySelector('[data-action="make-available"]') as HTMLButtonElement;
    const found = [...page.querySelectorAll('button')].find(
      (element) => element.textContent?.trim() === label,
    );
    if (!found) throw new Error('Pulsante assente: ' + label);
    return found;
  }
  async function click(label: string): Promise<void> {
    button(label).click();
    await settle();
  }
  async function submit(): Promise<void> {
    fixture.detectChanges();
    page.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }
  function editor(): WorkspaceStub {
    return fixture.debugElement.query(By.directive(WorkspaceStub))
      .componentInstance as WorkspaceStub;
  }
});
