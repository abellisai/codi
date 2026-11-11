import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiError, ApiService, ExerciseVersion } from '@core/api';
import { QuizEditor } from './quiz-editor';
import { QUIZ_ENGINE_VERSION, QUIZ_EXERCISE_TYPE, answerSolution } from './quiz-definition';

/**
 * Editor dell'esercizio dichiarativo (§13.9.1). I casi che contano sono due: la definizione si
 * compone dai campi, e **la risposta attesa non entra nella definizione** — vive nella soluzione
 * privata, e il salvataggio la manda lì.
 */
describe('Editor della domanda con risposta', () => {
  const existingDefinition = JSON.stringify({
    schemaVersion: 1,
    exerciseType: QUIZ_EXERCISE_TYPE,
    engineVersion: QUIZ_ENGINE_VERSION,
    title: 'Quanto vale 0110',
    instructions: 'Quanto vale in decimale?',
    toolbox: { allowedBlocks: [] },
    limits: { maxBlocks: 1, maxNestingDepth: 1, maxExecutionSteps: 1, maxActiveDurationMs: 1000 },
    quiz: { stimulus: '0110', prompt: 'Quanto vale in decimale?', match: 'Integer' },
    feedback: { success: 'Giusto!', collision: 'Riprova.' },
  });

  let commands: { name: string; payload: Record<string, unknown> }[] = [];
  let draft: { definitionJson: string; rowVersion: string } | null = null;
  let solution: string | null = null;
  let lastFixture: ComponentFixture<QuizEditor>;
  let versions: ExerciseVersion[] = [];
  let publishFailure: ApiError | null = null;
  let publishedSolution: string | null = null;
  let validationProblems: string[] = [];
  let validationGate: Promise<void> | null = null;

  beforeEach(() => {
    commands = [];
    draft = { definitionJson: existingDefinition, rowVersion: '4' };
    solution = answerSolution('6');
    versions = [];
    publishFailure = null;
    publishedSolution = null;
    validationProblems = [];
    validationGate = null;

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ApiService,
          useValue: {
            executeQuery: (name: string) => {
              if (name === 'GetExerciseDraft') {
                return draft === null
                  ? Promise.reject(
                      new ApiError('ResourceNotFound', 404, 'La bozza non esiste ancora.'),
                    )
                  : Promise.resolve(draft);
              }
              if (name === 'GetReferenceSolution') {
                return solution === null
                  ? Promise.reject(
                      new ApiError('ResourceNotFound', 404, 'La soluzione non esiste.'),
                    )
                  : Promise.resolve({ exerciseId: 'e1', workspaceJson: solution });
              }
              if (name === 'ListExerciseVersions') return Promise.resolve(versions);
              if (name === 'GetExercise')
                return Promise.resolve({
                  publicationStatus:
                    versions.length === 0
                      ? 'Draft'
                      : draft?.definitionJson === versions[0]?.definitionJson &&
                          solution === publishedSolution
                        ? 'Available'
                        : 'UnpublishedChanges',
                });
              return Promise.resolve(null);
            },
            executeCommand: (name: string, payload: Record<string, unknown>) => {
              commands.push({ name, payload });
              if (name === 'ValidateExerciseDraft') {
                return (validationGate ?? Promise.resolve()).then(() => ({
                  isValid: validationProblems.length === 0,
                  problems: validationProblems,
                }));
              }
              if (name === 'PublishExerciseVersion') {
                if (publishFailure !== null) return Promise.reject(publishFailure);
                const version: ExerciseVersion = {
                  id: 'v1',
                  exerciseId: 'e1',
                  versionNumber: '1',
                  definitionJson: draft!.definitionJson,
                  workspaceJson: '{"answer":""}',
                  exerciseType: QUIZ_EXERCISE_TYPE,
                  engineVersion: QUIZ_ENGINE_VERSION,
                  schemaVersion: '1',
                  contentHash: '12345678',
                  createdAt: '2026-10-16T10:00:00Z',
                  authorNotes: null,
                };
                versions = [version];
                publishedSolution = solution;
                return Promise.resolve(version);
              }
              if (name === 'CopyExerciseVersionToDraft') {
                draft = { definitionJson: versions[0]!.definitionJson, rowVersion: '6' };
                solution = answerSolution('6');
                return Promise.resolve(draft);
              }
              draft = {
                definitionJson: JSON.stringify(
                  Object.fromEntries(
                    Object.entries(JSON.parse(String(payload['definitionJson']))).reverse(),
                  ),
                ),
                rowVersion: '5',
              };
              solution = String(payload['referenceSolutionJson']);
              return Promise.resolve(draft);
            },
          },
        },
      ],
    });
  });

  it('mostra i valori della bozza salvata, compresa la risposta attesa', async () => {
    const page = await mount();

    expect(inputValue(page, '#quiz-stimulus')).toBe('0110');
    expect(inputValue(page, '#quiz-prompt')).toBe('Quanto vale in decimale?');
    expect(inputValue(page, '#quiz-match')).toBe('Integer');
    expect(inputValue(page, '#quiz-answer')).toBe('6');
  });

  it('salva la definizione composta e manda la risposta attesa nella soluzione privata', async () => {
    const page = await mount();

    setValue(page, '#quiz-answer', '8');
    await save();
    await flush();

    expect(commands).toHaveLength(1);
    const payload = commands[0]!.payload;
    expect(commands[0]!.name).toBe('SaveExerciseDraft');
    expect(payload['referenceSolutionJson']).toBe('{"answer":"8"}');
    expect(payload['baseRowVersion']).toBe('4');

    // La definizione composta non contiene la risposta attesa: il documento che l'alunno riceve
    // per mostrare l'esercizio non deve contenere la soluzione.
    const definition = String(payload['definitionJson']);
    expect(definition).not.toContain('"answer"');
    expect(definition).not.toContain('"8"');
    expect(definition).toContain('"stimulus":"0110"');
    expect(definition).toContain('"match":"Integer"');
  });

  it('avvisa quando manca la risposta attesa', async () => {
    solution = null;
    const page = await mount();

    expect(page.textContent).toContain('Inserisci la risposta corretta');
  });

  it('con una bozza assente propone i campi iniziali e non dichiara una versione base', async () => {
    draft = null;
    solution = null;
    const page = await mount();

    expect(page.textContent).toContain('Non esiste ancora una bozza');
    expect(inputValue(page, '#quiz-stimulus')).toBe('');

    setValue(page, '#quiz-stimulus', '0011');
    setValue(page, '#quiz-prompt', 'Quanto vale?');
    await save();
    await flush();

    expect(commands[0]!.payload['baseRowVersion']).toBeUndefined();
  });

  it('verifica domanda e risposta privata dal pannello di pubblicazione', async () => {
    const page = await mount();

    await click(page, 'Verifica la bozza');

    expect(commands[0]!.name).toBe('ValidateExerciseDraft');
    expect(commands[0]!.payload['referenceSolutionJson']).toBe(answerSolution('6'));
    expect(commands[0]!.payload['workspaceJson']).toBe('{"answer":""}');
    expect(String(commands[0]!.payload['definitionJson'])).not.toContain('"answer"');
  });

  it('pubblica una bozza salvata e mostra la domanda della versione pubblicata', async () => {
    const page = await mount();

    await click(page, 'Rendi disponibile');

    expect(commands.find((command) => command.name === 'PublishExerciseVersion')).toEqual({
      name: 'PublishExerciseVersion',
      payload: { exerciseId: 'e1', baseRowVersion: '4', authorNotes: null },
    });
    expect(stateLabel(page)).toBe('Disponibile');
    expect(page.querySelector('table.versions')?.textContent).toContain('quiz-1');

    await click(page, 'Guarda la versione');

    const preview = page.querySelector('.version-preview');
    expect(preview?.textContent).toContain('0110');
    expect(preview?.textContent).toContain('Quanto vale in decimale?');
    expect(preview?.querySelector('[role="alert"]')).toBeNull();
    expect(preview?.textContent).not.toContain('"answer"');
  });

  it('salva e rende disponibile una domanda nuova con un solo comando', async () => {
    draft = null;
    solution = null;
    const page = await mount();
    expect(button(page, 'Rendi disponibile').disabled).toBe(false);

    setValue(page, '#quiz-stimulus', '0011');
    setValue(page, '#quiz-prompt', 'Quanto vale?');
    setValue(page, '#quiz-answer', '3');
    await click(page, 'Rendi disponibile');

    expect(button(page, 'Rendi disponibile').disabled).toBe(false);
    expect(commands.at(-1)?.payload['baseRowVersion']).toBe('5');
    expect(commands.map((command) => command.name)).toEqual([
      'SaveExerciseDraft',
      'ValidateExerciseDraft',
      'PublishExerciseVersion',
    ]);
    // La conferma è lo stato nella barra: il badge passa a «Disponibile».
    expect(stateLabel(page)).toBe('Disponibile');
  });

  it('salva le modifiche e pubblica la revisione restituita senza un salvataggio manuale', async () => {
    const page = await mount();
    setValue(page, '#quiz-answer', '8');
    await settle();

    expect(button(page, 'Rendi disponibile').disabled).toBe(false);
    expect(page.textContent).toContain('Ci sono modifiche non salvate');
    await click(page, 'Rendi disponibile');

    expect(commands.at(-1)?.payload['baseRowVersion']).toBe('5');
    expect(commands[0]!.payload['referenceSolutionJson']).toBe(answerSolution('8'));
  });

  it('conserva la domanda e mostra il rifiuto della pubblicazione', async () => {
    publishFailure = new ApiError('EntityVersionConflict', 409, 'La bozza è cambiata. Ricaricala.');
    const page = await mount();
    await click(page, 'Rendi disponibile');

    expect(page.querySelector('.exercise-authoring-toolbar [role="alert"]')?.textContent).toContain(
      'La bozza è cambiata. Ricaricala.',
    );
    expect(inputValue(page, '#quiz-answer')).toBe('6');
    expect(button(page, 'Rendi disponibile').disabled).toBe(false);
    expect(stateLabel(page)).not.toBe('Disponibile');
  });

  it('ricarica domanda, risposta e revisione dopo la copia di una versione', async () => {
    const page = await mount();
    await click(page, 'Rendi disponibile');
    setValue(page, '#quiz-answer', '8');
    await saveFromForm(page);
    await click(page, 'Copia nell’editor');

    expect(inputValue(page, '#quiz-answer')).toBe('6');
    expect(button(page, 'Rendi disponibile').disabled).toBe(false);
    setValue(page, '#quiz-answer', '9');
    await saveFromForm(page);

    expect(commands.at(-1)?.payload['baseRowVersion']).toBe('6');
  });

  it('mostra la risposta mancante e porta al campo senza salvare o pubblicare', async () => {
    solution = null;
    const page = await mount();
    await click(page, 'Rendi disponibile');
    expect(commands).toHaveLength(0);
    expect(page.querySelector('#quiz-answer')?.getAttribute('aria-invalid')).toBe('true');
    const issue = page.querySelector(
      '.exercise-authoring-toolbar .issue-link',
    ) as HTMLButtonElement;
    expect(issue.textContent).toContain('Inserisci la risposta corretta.');
    issue.click();
    expect(document.activeElement?.id).toBe('quiz-answer');
    setValue(page, '#quiz-prompt', 'Domanda da completare in seguito.');
    await settle();
    await click(page, 'Salva bozza');
    expect(page.querySelector('#quiz-answer')?.getAttribute('aria-invalid')).not.toBe('true');
  });

  it('distingue bozza, disponibilità e modifiche private ancora da pubblicare', async () => {
    const page = await mount();
    const state = () => page.querySelector('.authoring-state')?.textContent?.trim();
    expect(state()).toBe('Bozza');
    await click(page, 'Rendi disponibile');
    expect(state()).toBe('Disponibile');
    setValue(page, '#quiz-answer', '9');
    await settle();
    expect(state()).toBe('Modifiche da pubblicare');
    await click(page, 'Salva bozza');
    expect(state()).toBe('Modifiche da pubblicare');
    await click(page, 'Rendi disponibile');
    expect(state()).toBe('Disponibile');
  });

  it('non pubblica quando il controllo server richiede una correzione', async () => {
    validationProblems = ['La risposta attesa non è valida.'];
    const page = await mount();
    await click(page, 'Rendi disponibile');
    expect(commands.map((command) => command.name)).toEqual(['ValidateExerciseDraft']);
    const issue = page.querySelector(
      '.exercise-authoring-toolbar .issue-link',
    ) as HTMLButtonElement;
    expect(issue.textContent).toContain('La risposta attesa non è valida.');
    issue.click();
    expect(document.activeElement?.id).toBe('quiz-answer');
  });

  it('blocca modifiche e doppio clic durante la verifica server del comando unico', async () => {
    let release!: () => void;
    validationGate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const page = await mount();
    await click(page, 'Rendi disponibile');
    expect((page.querySelector('#quiz-answer') as HTMLInputElement).disabled).toBe(true);
    expect(button(page, 'Rendi disponibile').disabled).toBe(true);
    await click(page, 'Rendi disponibile');
    expect(commands.map((command) => command.name)).toEqual(['ValidateExerciseDraft']);
    release();
    await settle();
    expect(commands.map((command) => command.name)).toEqual([
      'ValidateExerciseDraft',
      'PublishExerciseVersion',
    ]);
    expect((page.querySelector('#quiz-answer') as HTMLInputElement).disabled).toBe(false);
  });

  it('non segnala modifiche quando il server cambia l’ordine delle proprietà del documento', async () => {
    draft = {
      ...draft!,
      definitionJson: JSON.stringify(
        Object.fromEntries(Object.entries(JSON.parse(existingDefinition)).reverse()),
      ),
    };
    const page = await mount();
    expect(button(page, 'Salva bozza').disabled).toBe(true);
    setValue(page, '#quiz-answer', '8');
    await settle();
    await click(page, 'Rendi disponibile');
    expect(commands.map((command) => command.name)).toEqual([
      'SaveExerciseDraft',
      'ValidateExerciseDraft',
      'PublishExerciseVersion',
    ]);
    expect(button(page, 'Salva bozza').disabled).toBe(true);
  });

  async function mount(): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(QuizEditor);
    lastFixture = fixture;
    fixture.componentRef.setInput('exerciseId', 'e1');
    fixture.componentRef.setInput('title', 'Quanto vale 0110');
    fixture.detectChanges();
    await settle();

    return fixture.nativeElement as HTMLElement;
  }

  function inputValue(page: HTMLElement, selector: string): string {
    return (
      (page.querySelector(selector) as HTMLInputElement | HTMLSelectElement | null)?.value ?? ''
    );
  }

  /** Stato mostrato dal badge della barra: Bozza, Disponibile o Modifiche da pubblicare. */
  function stateLabel(page: HTMLElement): string {
    return page.querySelector('.authoring-state')?.textContent?.trim() ?? '';
  }

  function setValue(page: HTMLElement, selector: string, value: string): void {
    const element = page.querySelector(selector) as HTMLInputElement;
    element.value = value;
    element.dispatchEvent(new Event('input'));
  }

  function flush(): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, 0));
  }

  async function settle(): Promise<void> {
    await flush();
    lastFixture.detectChanges();
    await flush();
    lastFixture.detectChanges();
  }

  function button(page: HTMLElement, label: string): HTMLButtonElement {
    if (label === 'Rendi disponibile')
      return page.querySelector('[data-action="make-available"]') as HTMLButtonElement;
    const found = [...page.querySelectorAll('button')].find(
      (candidate) => candidate.textContent?.trim() === label,
    );
    if (!found) throw new Error(`Pulsante assente: ${label}`);
    return found;
  }

  async function click(page: HTMLElement, label: string): Promise<void> {
    button(page, label).click();
    await settle();
  }

  async function saveFromForm(page: HTMLElement): Promise<void> {
    await settle();
    page.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  /**
   * Salva come farebbe il modulo. Il pulsante è governato dal modulo Signal Forms, la cui validità
   * non è l'oggetto di questi casi: qui interessa che cosa il salvataggio compone e dove lo manda.
   */
  async function save(): Promise<void> {
    await (lastFixture.componentInstance as unknown as { save: () => Promise<void> }).save();
  }
});
