import { By } from '@angular/platform-browser';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ApiError, ApiService, Exercise, ExerciseDraft, ExerciseVersion } from '@core/api';
import {
  GRID_PATH_ENGINE_VERSION,
  GRID_PATH_EXERCISE_TYPE,
  GRID_PATH_SCHEMA_VERSION,
} from './exercise-definition';
import { ExerciseEditor } from './exercise-editor';
import { ExercisePublication } from './exercise-publication/exercise-publication';
import { GridPathProgramEditor } from './grid-path-program-editor/grid-path-program-editor';
import { answerSolution, buildQuizDefinition, suggestedQuizModel } from './quiz-definition';

/** Definizione di prova: griglia 5×5, partenza in (0, 0) a est, traguardo in (3, 1). */
const sampleDefinitionJson = JSON.stringify({
  schemaVersion: GRID_PATH_SCHEMA_VERSION,
  exerciseType: GRID_PATH_EXERCISE_TYPE,
  engineVersion: GRID_PATH_ENGINE_VERSION,
  title: 'Raggiungi la stella',
  instructions: 'Porta il personaggio sulla stella usando i blocchi.',
  scene: {
    width: 5,
    height: 5,
    start: { x: 0, y: 0, direction: 'East' },
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
  feedback: { success: 'Hai raggiunto la stella!', collision: "C'è un ostacolo." },
});

/** Sostituto deterministico del dispatcher: registra le richieste e simula 404 e rifiuti. */
class ApiStub {
  readonly queries: { name: string; payload: Record<string, unknown> }[] = [];
  readonly commands: { name: string; payload: Record<string, unknown> }[] = [];

  exercise: Exercise = {
    id: 'ex1',
    exerciseType: 'GridPath',
    title: 'Raggiungi la stella',
    description: '',
    isArchived: false,
    rowVersion: '1',
  };

  /** Bozza letta: `null` significa che il server risponde 404, cioè non esiste ancora. */
  draft: ExerciseDraft | null = null;

  /** Soluzione privata della bozza; `null` quando il docente non ne ha composta una. */
  solution: string | null = null;

  /** Versioni pubblicate restituite da `ListExerciseVersions`, dalla più recente. */
  versions: ExerciseVersion[] = [];

  loadFailure: ApiError | null = null;
  saveFailure: ApiError | null = null;

  async executeQuery<TResponse>(
    name: string,
    payload: Record<string, unknown> = {},
  ): Promise<TResponse> {
    this.queries.push({ name, payload });

    if (this.loadFailure !== null) {
      throw this.loadFailure;
    }

    if (name === 'GetExercise') {
      return {
        ...this.exercise,
        publicationStatus: this.versions.length > 0 ? 'Available' : 'Draft',
      } as TResponse;
    }
    if (name === 'GetExerciseDraft') {
      if (this.draft === null) {
        throw new ApiError('ResourceNotFound', 404, 'La bozza non esiste ancora.');
      }

      return this.draft as TResponse;
    }
    if (name === 'GetReferenceSolution') {
      if (this.draft === null) {
        throw new ApiError('ResourceNotFound', 404, 'La bozza non esiste ancora.');
      }

      return { exerciseId: 'ex1', workspaceJson: this.solution } as TResponse;
    }
    if (name === 'ListExerciseVersions') {
      return this.versions as unknown as TResponse;
    }

    throw new Error(`Query non prevista: ${name}`);
  }

  async executeCommand<TResponse>(
    name: string,
    payload: Record<string, unknown> = {},
  ): Promise<TResponse> {
    this.commands.push({ name, payload });

    if (this.saveFailure !== null) {
      throw this.saveFailure;
    }

    if (name === 'ValidateExerciseDraft') {
      return { isValid: true, problems: [] } as TResponse;
    }
    if (name === 'PublishExerciseVersion') {
      const published: ExerciseVersion = {
        id: `version-${String(this.versions.length + 1)}`,
        exerciseId: 'ex1',
        versionNumber: String(this.versions.length + 1),
        definitionJson: String(payload['definitionJson'] ?? '{}'),
        // La versione fotografa il programma iniziale salvato nella bozza.
        workspaceJson: this.draft?.workspaceJson ?? '{}',
        exerciseType: 'GridPath',
        engineVersion: 'gridpath-1',
        schemaVersion: '1',
        contentHash: 'a'.repeat(64),
        referenceHash: null,
        createdAt: '2026-10-02T11:00:00Z',
        authorNotes: null,
      };
      this.versions = [published, ...this.versions];

      return published as TResponse;
    }

    const base = payload['baseRowVersion'] as string | undefined;
    const draft: ExerciseDraft = {
      exerciseId: 'ex1',
      definitionJson: String(payload['definitionJson'] ?? ''),
      workspaceJson: String(payload['workspaceJson'] ?? ''),
      rowVersion: base === undefined ? '1' : String(Number(base) + 1),
      updatedAtUtc: '2026-10-02T10:00:00Z',
    };
    this.draft = draft;

    return draft as TResponse;
  }
}

describe('ExerciseEditor', () => {
  let harness!: RouterTestingHarness;
  let created = false;
  let api: ApiStub;

  beforeEach(async () => {
    api = new ApiStub();
    created = false;

    await TestBed.configureTestingModule({
      providers: [
        { provide: ApiService, useValue: api },
        provideRouter([{ path: 'exercises/:exerciseId/editor', component: ExerciseEditor }]),
      ],
    }).compileComponents();
  });

  it('legge l’esercizio e la bozza e mostra il titolo nei metadati', async () => {
    api.draft = existingDraft();

    await render();

    expect(api.queries.slice(0, 3)).toEqual([
      { name: 'GetExercise', payload: { id: 'ex1' } },
      { name: 'GetExerciseDraft', payload: { exerciseId: 'ex1' } },
      { name: 'GetReferenceSolution', payload: { exerciseId: 'ex1' } },
    ]);
    // La sezione della pubblicazione chiede le versioni già pubblicate.
    expect(api.queries.map((query) => query.name)).toContain('ListExerciseVersions');
    expect(pageText()).toContain('Raggiungi la stella');
    expect(input('#editor-instructions').value).toBe(
      'Porta il personaggio sulla stella usando i blocchi.',
    );
    // La bozza letta coincide con lo stato di partenza: non c'è nulla da salvare.
    expect(saveButton().disabled).toBe(true);
  });

  it('mostra la pubblicazione della domanda senza i comandi e gli avvisi della griglia', async () => {
    api.exercise = { ...api.exercise, exerciseType: 'Quiz' };
    api.draft = {
      ...existingDraft(),
      definitionJson: JSON.stringify(
        buildQuizDefinition(api.exercise.title, {
          ...suggestedQuizModel(),
          stimulus: '0110',
          prompt: 'Quanto vale?',
        }),
      ),
    };
    api.solution = answerSolution('6');

    await render();

    expect(pageText()).toContain('Rendi disponibile');
    expect(pageText()).not.toContain('Non esiste ancora una bozza');
    expect(pageText()).not.toContain('La griglia è fatta di pulsanti');
    expect(page().querySelector('button[form="exercise-editor-form"]')).toBeNull();
    expect(page().querySelector('app-exercise-publication')).not.toBeNull();
    expect(api.queries.filter((query) => query.name === 'GetExerciseDraft')).toHaveLength(1);
  });

  it('tratta il 404 come bozza nuova e propone una definizione iniziale', async () => {
    api.draft = null;

    await render();

    expect(pageText()).toContain('definizione iniziale suggerita');
    expect(input('#editor-width').value).toBe('5');
    expect(input('#editor-height').value).toBe('5');
    expect(cells()[0].getAttribute('aria-label')).toBe(
      'Riga 1, colonna 1: partenza, direzione Est',
    );
    expect(cells()[24].getAttribute('aria-label')).toBe('Riga 5, colonna 5: traguardo');
    // Tutti e quattro i blocchi sono disponibili.
    expect(checkedBlocks()).toEqual([
      'editor-block-start',
      'editor-block-move',
      'editor-block-turn',
      'editor-block-repeat',
    ]);
  });

  it('salva una bozza nuova senza `baseRowVersion`', async () => {
    api.draft = null;
    await render();

    clickButton('Salva bozza');
    await render();

    expect(api.commands, pageText()).toHaveLength(1);
    const payload = api.commands[0]?.payload ?? {};
    expect(Object.hasOwn(payload, 'baseRowVersion')).toBe(false);
    expect(payload['exerciseId']).toBe('ex1');
    expect(payload['referenceSolutionJson']).toBeNull();
    // Una bozza senza progetto composto invia il progetto vuoto, non una stringa vuota: il server
    // rifiuta un documento che non è un oggetto JSON.
    expect(payload['workspaceJson']).toBe('{}');
  });

  it('salva una bozza esistente con la versione letta e compone la definizione dai campi', async () => {
    api.draft = existingDraft();
    await render();

    setInput('#editor-instructions', 'Nuova consegna');
    setInput('#editor-width', '6');
    setInput('#editor-limit-max-duration', '7');
    setInput('#editor-success', 'Bravissimo!');
    await render();

    clickButton('Salva bozza');
    await render();

    const payload = api.commands[0]?.payload ?? {};
    expect(payload['baseRowVersion']).toBe('3');

    const definition = JSON.parse(String(payload['definitionJson'])) as Record<string, never>;
    expect(definition['title']).toBe('Raggiungi la stella');
    expect(definition['instructions']).toBe('Nuova consegna');
    expect(definition['scene']).toMatchObject({ width: 6 });
    // La pagina parla minuti, il documento millisecondi; gli altri limiti tecnici restano quelli
    // letti dalla bozza, perché non sono più campi del docente.
    expect(definition['limits']).toMatchObject({ maxBlocks: 30, maxActiveDurationMs: 420_000 });
    expect(definition['feedback']).toMatchObject({ success: 'Bravissimo!' });
  });

  it('mostra solo la durata massima fra i limiti, in minuti', async () => {
    api.draft = existingDraft();
    await render();

    expect(page().querySelector('#editor-limit-max-duration')).not.toBeNull();
    expect(page().querySelector('#editor-limit-max-blocks')).toBeNull();
    expect(page().querySelector('#editor-limit-max-nesting')).toBeNull();
    expect(page().querySelector('#editor-limit-max-steps')).toBeNull();
    // 120000 millisecondi della bozza si mostrano come 2 minuti.
    expect(input('#editor-limit-max-duration').value).toBe('2');
    expect(pageText()).toContain('Durata massima (minuti)');
  });

  it('aggiorna il completamento quando si sposta il traguardo', async () => {
    api.draft = existingDraft();
    await render();

    // Cursore dalla cella (0, 0) a (0, 1) con la freccia, poi comando accessibile sulla cella attiva.
    cells()[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    await render();
    expect(pageText()).toContain('riga 2, colonna 1');

    clickButton('Metti il traguardo');
    await render();

    clickButton('Salva bozza');
    await render();

    const payload = api.commands[0]?.payload ?? {};
    const definition = JSON.parse(String(payload['definitionJson'])) as {
      scene: { goal: { x: number; y: number } };
      completion: { type: string; x: number; y: number };
    };
    expect(definition.scene.goal).toEqual({ x: 0, y: 1 });
    expect(definition.completion).toEqual({ type: 'ReachCell', x: 0, y: 1 });
  });

  it('conserva i valori digitati quando il server rifiuta il salvataggio', async () => {
    api.draft = existingDraft();
    await render();

    api.saveFailure = new ApiError(
      'EntityVersionConflict',
      412,
      'La bozza è cambiata nel frattempo.',
    );
    setInput('#editor-instructions', 'Bozza non salvata');
    await render();

    clickButton('Salva bozza');
    await render();

    expect(pageText()).toContain('La bozza è cambiata nel frattempo.');
    expect(input('#editor-instructions').value).toBe('Bozza non salvata');
    // Il rifiuto non riporta la form allo stato salvato: si può correggere e ritentare.
    expect(saveButton().disabled).toBe(false);
  });

  it('riporta la form allo stato salvato con «Annulla modifiche»', async () => {
    api.draft = existingDraft();
    await render();

    setInput('#editor-instructions', 'Modifica da annullare');
    await render();
    expect(saveButton().disabled).toBe(false);

    clickButton('Annulla modifiche');
    await render();

    expect(input('#editor-instructions').value).toBe(
      'Porta il personaggio sulla stella usando i blocchi.',
    );
    expect(saveButton().disabled).toBe(true);
  });

  it('riporta la form allo stato salvato dopo un salvataggio riuscito', async () => {
    api.draft = existingDraft();
    await render();

    setInput('#editor-instructions', 'Consegna salvata');
    await render();
    clickButton('Salva bozza');
    await render();

    // L'esito lo annuncia l'avviso standard del dispatcher; la pagina riallinea i propri riferimenti.
    expect(api.commands.at(-1)?.name).toBe('SaveExerciseDraft');
    expect(saveButton().disabled).toBe(true);
  });

  it('rende obbligatorio `aula_start` e lascia scegliere gli altri blocchi', async () => {
    api.draft = existingDraft();
    await render();

    expect(checkbox('#editor-block-start').checked).toBe(true);
    expect(checkbox('#editor-block-start').disabled).toBe(true);
    expect(checkbox('#editor-block-move').disabled).toBe(false);
  });

  it('mostra soltanto la soluzione di riferimento come workspace', async () => {
    api.draft = existingDraft();
    await render();

    expect(pageText()).not.toContain('Progetto a blocchi');
    expect(harness.fixture.debugElement.queryAll(By.directive(GridPathProgramEditor))).toHaveLength(
      1,
    );
    expect(pageText()).not.toContain('Programma iniziale');
    expect(pageText()).toContain('Soluzione di riferimento');
    // L'anteprima della soluzione resta: è la prova che l'esercizio è risolvibile.
    expect(page().querySelector('.cell-character')).not.toBeNull();
  });

  it('salva un progetto iniziale vuoto anche se la bozza precedente contiene blocchi', async () => {
    const savedInitial =
      '{"blocks":{"languageVersion":0,"blocks":[{"type":"aula_start","id":"old"}]}}';
    api.draft = { ...existingDraft(), workspaceJson: savedInitial };
    await render();

    setInput('#editor-instructions', 'Consegna aggiornata');
    await render();

    clickButton('Salva bozza');
    await render();

    expect(api.commands[0]?.payload['workspaceJson']).toBe('{}');
    // La soluzione resta separata e assente quando il docente non l'ha composta.
    expect(api.commands[0]?.payload['referenceSolutionJson']).toBeNull();
  });

  it('compone la soluzione di riferimento e la invia al salvataggio', async () => {
    api.draft = existingDraft();
    await render();

    const solution =
      '{"blocks":{"languageVersion":0,"blocks":[{"type":"aula_start","id":"sol-1"}]}}';
    solutionEditor().workspaceChanged.emit(solution);
    await render();

    clickButton('Salva bozza');
    await render();

    // La soluzione viaggia con la bozza: è privata del docente e non entra nel progetto iniziale.
    const payload = api.commands[0]?.payload ?? {};
    expect(payload['referenceSolutionJson']).toBe(solution);
    // Il programma iniziale resta quello della bozza: la soluzione non lo sostituisce.
    expect(payload['workspaceJson']).toBe('{}');
  });

  it('mostra le versioni pubblicate e pubblica la bozza salvata', async () => {
    api.draft = existingDraft();
    api.versions = [publishedVersion];
    await render();

    expect(pageText()).toContain('Versioni pubblicate');
    expect(pageText()).toContain('Rendi disponibile');
    expect(pageText()).toContain('gridpath-1');

    clickButton('Rendi disponibile');
    await render();

    const command = api.commands.find((entry) => entry.name === 'PublishExerciseVersion');
    // La pubblicazione fotografa la revisione salvata, non ciò che è a schermo.
    expect(command?.payload['baseRowVersion']).toBe('3');
    // La conferma è lo stato nella barra, non un messaggio a parte.
    expect(stateLabel()).toBe('Disponibile');
  });

  it('permette il comando unico anche quando ci sono modifiche non salvate', async () => {
    api.draft = existingDraft();
    await render();

    setInput('#editor-instructions', 'Consegna non ancora salvata');
    await render();

    expect(button('Rendi disponibile').disabled).toBe(false);
    expect(pageText()).toContain('modifiche non salvate');
  });

  it('verifica la bozza senza soluzione dichiarandola assente, non vuota', async () => {
    api.draft = existingDraft();
    await render();

    clickButton('Verifica la bozza');
    await render();

    const command = api.commands.find((entry) => entry.name === 'ValidateExerciseDraft');
    // Un progetto vuoto è «nessuna soluzione»: inviarlo come documento farebbe pretendere il punto
    // di ingresso a una soluzione che il docente non ha mai composto.
    expect(command?.payload['referenceSolutionJson']).toBeNull();
  });

  it('rilegge la bozza dopo la copia di una versione', async () => {
    api.draft = existingDraft();
    await render();

    // La copia avviene nel pannello della pubblicazione: qui si prova quello che la pagina fa
    // quando le arriva l'esito. La conferma la dà l'avviso standard del dispatcher.
    publicationPanel().draftReplaced.emit();
    await render();

    expect(api.queries.filter((query) => query.name === 'GetExerciseDraft').length).toBeGreaterThan(
      1,
    );
  });

  it('usa la griglia da tastiera con etichette comprensibili e comandi accessibili', async () => {
    api.draft = existingDraft();
    await render();

    // Ogni cella dice riga, colonna e stato: lo stato non è affidato al solo colore.
    expect(cells().length).toBe(25);
    expect(cells()[0].getAttribute('aria-label')).toBe(
      'Riga 1, colonna 1: partenza, direzione Est',
    );
    expect(cells()[8].getAttribute('aria-label')).toBe('Riga 2, colonna 4: traguardo');
    expect(cells()[18].getAttribute('aria-label')).toBe('Riga 4, colonna 4: libera');

    // Con lo strumento ostacolo, attivare una cella la rende ostacolo e lo annuncia.
    cells()[18].click();
    await render();
    expect(cells()[18].getAttribute('aria-label')).toBe('Riga 4, colonna 4: ostacolo');
    expect(pageText()).toContain('ora è un ostacolo');

    // Lo strumento partenza sposta la partenza e libera la cella di partenza precedente.
    clickButton('Partenza');
    await render();
    cells()[12].click();
    await render();
    expect(cells()[12].getAttribute('aria-label')).toContain('partenza');
    expect(cells()[0].getAttribute('aria-label')).toBe('Riga 1, colonna 1: libera');
  });

  it('non lascia diventare ostacolo la partenza o il traguardo', async () => {
    api.draft = existingDraft();
    await render();

    cells()[0].click();
    await render();

    expect(cells()[0].getAttribute('aria-label')).toContain('partenza');
    expect(pageText()).toContain('non può diventare un ostacolo');
  });

  it('mostra il rifiuto della lettura senza perdere il ritorno all’elenco', async () => {
    api.loadFailure = new ApiError('ResourceNotFound', 404, 'Esercizio non trovato.');

    await render();

    expect(pageText()).toContain('Esercizio non trovato.');
    expect(pageText()).toContain('Esercizi');
  });

  function existingDraft(): ExerciseDraft {
    return {
      exerciseId: 'ex1',
      definitionJson: sampleDefinitionJson,
      workspaceJson: '{"blocks":{"languageVersion":0,"blocks":[]}}',
      rowVersion: '3',
      updatedAtUtc: '2026-10-01T10:00:00Z',
    };
  }

  async function render(): Promise<void> {
    if (!created) {
      created = true;
      harness = await RouterTestingHarness.create('/exercises/ex1/editor');
    }

    // Due giri: il primo monta la pagina e avvia le letture, il secondo rende le sezioni che
    // esistono solo dopo il caricamento — fra cui il pannello della pubblicazione, che legge le
    // sue versioni e ha bisogno di un ulteriore giro per mostrarle.
    for (let round = 0; round < 2; round += 1) {
      harness.detectChanges();
      await harness.fixture.whenStable();
      await new Promise((resolve) => setTimeout(resolve, 0));
      harness.detectChanges();
    }
  }

  function page(): HTMLElement {
    const element = harness.routeNativeElement;
    if (element === null) {
      throw new Error('La pagina della route non è montata.');
    }

    return element;
  }

  function pageText(): string {
    return page().textContent ?? '';
  }

  /** Stato mostrato dal badge della barra: Bozza, Disponibile o Modifiche da pubblicare. */
  function stateLabel(): string {
    return page().querySelector('.authoring-state')?.textContent?.trim() ?? '';
  }

  function input(selector: string): HTMLInputElement | HTMLTextAreaElement {
    const element = page().querySelector<HTMLInputElement | HTMLTextAreaElement>(selector);
    if (element === null) {
      throw new Error(`Campo non trovato: ${selector}`);
    }

    return element;
  }

  function setInput(selector: string, value: string): void {
    const element = input(selector);
    element.value = value;
    element.dispatchEvent(new Event('input'));
  }

  function checkbox(selector: string): HTMLInputElement {
    const element = page().querySelector<HTMLInputElement>(selector);
    if (element === null) {
      throw new Error(`Casella non trovata: ${selector}`);
    }

    return element;
  }

  /** Identificativi delle caselle dei blocchi effettivamente selezionate. */
  function checkedBlocks(): string[] {
    return [...page().querySelectorAll<HTMLInputElement>('.block-checkbox')]
      .filter((element) => element.checked)
      .map((element) => element.id);
  }

  function cells(): HTMLButtonElement[] {
    return [...page().querySelectorAll<HTMLButtonElement>('.scene-cell')];
  }

  /** Unico workspace della pagina: la soluzione privata del docente. */
  function solutionEditor(): GridPathProgramEditor {
    return editorAt(0);
  }

  function editorAt(index: number): GridPathProgramEditor {
    const found = harness.fixture.debugElement.queryAll(By.directive(GridPathProgramEditor));
    if (found.length <= index) {
      throw new Error(`Editor del progetto a blocchi non montato: indice ${String(index)}.`);
    }

    return found[index].componentInstance as GridPathProgramEditor;
  }

  /** Pannello della pubblicazione: la pagina gli passa i documenti e ne riceve gli esiti. */
  function publicationPanel(): ExercisePublication {
    const found = harness.fixture.debugElement.query(By.directive(ExercisePublication));
    if (found === null) {
      throw new Error('Pannello della pubblicazione non montato.');
    }

    return found.componentInstance as ExercisePublication;
  }

  /** Pulsante con l'etichetta indicata. */
  function button(label: string): HTMLButtonElement {
    const found = [...page().querySelectorAll('button')].find(
      (candidate) => (candidate.textContent ?? '').trim() === label,
    );
    if (found === undefined) {
      throw new Error(`Pulsante non trovato: ${label}`);
    }

    return found;
  }

  function clickButton(label: string): void {
    button(label).click();
  }

  /** Versione pubblicata di prova: numero 1, con un'impronta riconoscibile e un programma iniziale. */
  const publishedVersion: ExerciseVersion = {
    id: 'version-1',
    exerciseId: 'ex1',
    versionNumber: '1',
    definitionJson: sampleDefinitionJson,
    workspaceJson: '{"blocks":{"languageVersion":0,"blocks":[{"type":"aula_move","id":"v1-1"}]}}',
    exerciseType: 'GridPath',
    engineVersion: 'gridpath-1',
    schemaVersion: '1',
    contentHash: 'b'.repeat(64),
    referenceHash: null,
    createdAt: '2026-10-02T11:00:00Z',
    authorNotes: null,
  };

  function saveButton(): HTMLButtonElement {
    const button = [...page().querySelectorAll('button')].find(
      (candidate) => (candidate.textContent ?? '').trim() === 'Salva bozza',
    );
    if (button === undefined) {
      throw new Error('Pulsante di salvataggio non trovato.');
    }

    return button;
  }
});
