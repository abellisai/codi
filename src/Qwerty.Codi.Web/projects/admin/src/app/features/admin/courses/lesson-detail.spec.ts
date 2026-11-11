import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import {
  ApiError,
  ApiService,
  Course,
  Exercise,
  Lesson,
  LessonExercise,
  LessonExerciseInput,
} from '@core/api';
import { LessonDetail } from './lesson-detail';

/**
 * Sostituto deterministico del dispatcher. La sostituzione della sequenza aggiorna lo stato letto,
 * come fa il server: dopo il salvataggio la pagina rilegge e deve trovare il valore nuovo, non
 * quello di prima.
 */
class ApiStub {
  readonly queries: { name: string; payload: Record<string, unknown> }[] = [];
  readonly commands: { name: string; payload: Record<string, unknown> }[] = [];

  lesson: Lesson = {
    id: 'lz1',
    courseId: 'c1',
    title: 'Lezione uno',
    description: '',
    isArchived: false,
    rowVersion: '5',
  };
  course: Course = {
    id: 'c1',
    title: 'Informatica di base',
    description: '',
    isArchived: false,
    rowVersion: '2',
  };
  exercises: readonly Exercise[] = [];
  sequence: readonly LessonExercise[] = [];

  /** Rifiuto delle mutazioni, come una versione obsoleta della lezione. */
  failure: ApiError | null = null;

  /** Rifiuto della lettura, come un indirizzo di una lezione che non esiste più. */
  loadFailure: ApiError | null = null;

  async executeQuery<TResponse>(
    name: string,
    payload: Record<string, unknown> = {},
  ): Promise<TResponse> {
    this.queries.push({ name, payload });

    if (this.loadFailure !== null) {
      throw this.loadFailure;
    }

    switch (name) {
      case 'GetCourse':
        return this.course as TResponse;
      case 'GetLesson':
        return this.lesson as TResponse;
      case 'ListExercises':
        return this.exercises as TResponse;
      default:
        return this.sequence as TResponse;
    }
  }

  async executeCommand<TResponse>(
    name: string,
    payload: Record<string, unknown> = {},
  ): Promise<TResponse> {
    this.commands.push({ name, payload });

    if (this.failure !== null) {
      throw this.failure;
    }

    if (name === 'ReplaceLessonExercises') {
      const exercises = (payload['exercises'] ?? []) as readonly LessonExerciseInput[];
      this.sequence = exercises.map((input, index) => ({
        id: `le${String(index)}`,
        lessonId: this.lesson.id,
        exerciseId: input.exerciseId,
        position: index + 1,
        rowVersion: '1',
      }));
    }

    return this.sequence as TResponse;
  }
}

const firstExerciseId = 'ex1';
const secondExerciseId = 'ex2';

/**
 * La pagina si monta da una route vera: corso e lezione viaggiano nell'indirizzo, quindi il banco
 * di prova deve avere router e parametri come quelli del browser.
 */
describe('LessonDetail', () => {
  let harness!: RouterTestingHarness;
  let created = false;
  let api: ApiStub;

  const exercises: readonly Exercise[] = [
    {
      id: firstExerciseId,
      exerciseType: 'GridPath',
      title: 'Somma',
      description: '',
      isArchived: false,
      rowVersion: '1',
    },
    {
      id: secondExerciseId,
      exerciseType: 'GridPath',
      title: 'Ciclo',
      description: '',
      isArchived: false,
      rowVersion: '1',
    },
  ];

  const firstAssociation: LessonExercise = {
    id: 'le1',
    lessonId: 'lz1',
    exerciseId: firstExerciseId,
    position: 1,
    rowVersion: '1',
  };

  /** Seconda posizione della stessa lezione: il secondo esercizio dell'anagrafica. */
  const secondAssociation: LessonExercise = {
    ...firstAssociation,
    id: 'le2',
    exerciseId: secondExerciseId,
    position: 2,
  };

  beforeEach(async () => {
    api = new ApiStub();
    api.exercises = exercises;
    created = false;

    await TestBed.configureTestingModule({
      providers: [
        { provide: ApiService, useValue: api },
        provideRouter([{ path: 'courses/:courseId/lessons/:lessonId', component: LessonDetail }]),
      ],
    }).compileComponents();
  });

  it('legge la lezione dall’indirizzo e mostra la sequenza con i nomi degli esercizi', async () => {
    api.sequence = [firstAssociation];

    await render();

    expect(api.queries).toEqual([
      { name: 'GetCourse', payload: { id: 'c1' } },
      { name: 'GetLesson', payload: { id: 'lz1' } },
      { name: 'ListExercises', payload: { includeArchived: false } },
      { name: 'ListLessonExercises', payload: { lessonId: 'lz1' } },
    ]);
    expect(pageText()).toContain('Lezione uno');
    expect(pageText()).toContain('1 esercizio');
    expect(positions()).toEqual(['1']);
    expect(sequenceNames()).toEqual(['Somma']);
  });

  it('rifiuta una lezione di un altro corso senza caricare o modificare la sequenza', async () => {
    api.lesson = { ...api.lesson, courseId: 'altro-corso' };
    api.sequence = [firstAssociation];

    await render();

    expect(pageText()).toContain('La lezione non appartiene al corso selezionato.');
    expect(sequenceNames()).toEqual([]);
    expect(api.queries.map((query) => query.name)).toEqual(['GetCourse', 'GetLesson']);
    expect(api.commands).toEqual([]);
  });

  it('numera le posizioni nell’ordine salvato e dichiara da dove si arriva', async () => {
    // La posizione non è un dato della riga: è l'ordine in cui la sequenza arriva dal server.
    api.sequence = [
      { ...firstAssociation, exerciseId: secondExerciseId },
      { ...firstAssociation, id: 'le2', exerciseId: firstExerciseId, position: 2 },
    ];

    await render();

    expect(positions()).toEqual(['1', '2']);
    expect(sequenceNames()).toEqual(['Ciclo', 'Somma']);
    expect(breadcrumbTrail()).toEqual(['Corsi', 'Informatica di base', 'Lezione uno']);
    expect(breadcrumbLinks()).toEqual(['/courses', '/courses/c1']);
  });

  it('mostra lo stato vuoto e ammette una sequenza senza esercizi', async () => {
    await render();

    expect(pageText()).toContain('La sequenza non ha ancora esercizi');
    expect(positions()).toEqual([]);
  });

  it('tiene spenti i salvataggi finché la bozza non cambia', async () => {
    api.sequence = [firstAssociation];

    await render();

    expect(saveDisabled()).toBe(true);
    expect(resetDisabled()).toBe(true);
  });

  it('aggiunge in coda l’esercizio scelto e abilita il salvataggio', async () => {
    api.sequence = [firstAssociation];

    await render();

    setSelect('#add-exercise', secondExerciseId);
    await render();
    clickButton('Aggiungi', page());
    await render();

    expect(positions()).toEqual(['1', '2']);
    expect(sequenceNames()).toEqual(['Somma', 'Ciclo']);
    expect(saveDisabled()).toBe(false);
    // La scelta non resta nel selettore: si aggiunge un esercizio per volta.
    expect(select().value).toBe('');
  });

  it('toglie dal selettore gli esercizi già in sequenza', async () => {
    // Lo stesso esercizio non si aggiunge due volte alla stessa lezione: appena entra nella bozza
    // esce dall'elenco delle scelte, quindi non si creano due caselle identiche.
    api.sequence = [firstAssociation, secondAssociation];

    await render();

    expect(optionValues()).toEqual([]);
    expect(select().disabled).toBe(true);
    expect(pageText()).toContain('Tutti gli esercizi dell’anagrafica sono già in questa lezione');
    expect(textButton('Aggiungi').disabled).toBe(true);
  });

  it('rimette nel selettore l’esercizio tolto dalla sequenza', async () => {
    api.sequence = [firstAssociation, secondAssociation];

    await render();

    clickButton('Rimuovi Somma', page());
    await render();

    expect(optionValues()).toEqual([firstExerciseId]);
    expect(select().disabled).toBe(false);
    expect(textButton('Aggiungi').disabled).toBe(false);
  });

  it('porta una maniglia di trascinamento su ogni riga', async () => {
    // Il trascinamento parte solo dalla maniglia: i pulsanti dentro la riga restano cliccabili e la
    // riga non si sposta per sbaglio.
    api.sequence = [firstAssociation];

    await render();

    const handles = page().querySelectorAll('.sequence-handle');
    expect(handles.length).toBe(1);
    expect(handles[0].hasAttribute('cdkDragHandle')).toBe(true);
    expect(page().querySelector('.sequence-item')?.hasAttribute('cdkDrag')).toBe(true);
    expect(handle(0).getAttribute('aria-label')).toContain('Posizione 1 di Somma');
  });

  it('cambia l’ordine dalla maniglia con le frecce, senza inviare nulla', async () => {
    api.sequence = [
      firstAssociation,
      { ...firstAssociation, id: 'le2', exerciseId: secondExerciseId, position: 2 },
    ];

    await render();

    pressArrow(0, 'ArrowDown');
    await render();

    expect(sequenceNames()).toEqual(['Ciclo', 'Somma']);
    expect(positions()).toEqual(['1', '2']);
    expect(api.commands.length).toBe(0);

    pressArrow(1, 'ArrowUp');
    await render();

    expect(sequenceNames()).toEqual(['Somma', 'Ciclo']);
  });

  it('annuncia lo spostamento a chi non vede l’elenco', async () => {
    // Il trascinamento è silenzioso: l'esito dello spostamento si sente dalla regione di stato.
    api.sequence = [
      firstAssociation,
      { ...firstAssociation, id: 'le2', exerciseId: secondExerciseId, position: 2 },
    ];

    await render();

    pressArrow(0, 'ArrowDown');
    await render();

    expect(page().querySelector('[role="status"].sr-only')?.textContent).toContain(
      '«Somma» è ora in posizione 2',
    );
  });

  it('non sposta la prima riga verso l’alto né l’ultima verso il basso', async () => {
    api.sequence = [
      firstAssociation,
      { ...firstAssociation, id: 'le2', exerciseId: secondExerciseId, position: 2 },
    ];

    await render();

    pressArrow(0, 'ArrowUp');
    await render();
    expect(sequenceNames()).toEqual(['Somma', 'Ciclo']);

    pressArrow(1, 'ArrowDown');
    await render();
    expect(sequenceNames()).toEqual(['Somma', 'Ciclo']);
  });

  it('rimuove un esercizio senza inviare nulla', async () => {
    api.sequence = [
      firstAssociation,
      { ...firstAssociation, id: 'le2', exerciseId: secondExerciseId, position: 2 },
    ];

    await render();

    clickButton('Rimuovi Somma', page());
    await render();

    expect(sequenceNames()).toEqual(['Ciclo']);
    expect(positions()).toEqual(['1']);
    expect(api.commands.length).toBe(0);
  });

  it('annulla le modifiche riportando la bozza all’ordine letto dal server', async () => {
    api.sequence = [firstAssociation];

    await render();

    setSelect('#add-exercise', secondExerciseId);
    await render();
    clickButton('Aggiungi', page());
    await render();

    expect(sequenceNames()).toEqual(['Somma', 'Ciclo']);

    clickButton('Annulla modifiche', page());
    await render();

    expect(sequenceNames()).toEqual(['Somma']);
    expect(saveDisabled()).toBe(true);
    expect(api.commands.length).toBe(0);
  });

  it('sostituisce la sequenza in blocco con la versione della lezione', async () => {
    api.sequence = [firstAssociation];

    await render();

    setSelect('#add-exercise', secondExerciseId);
    await render();
    clickButton('Aggiungi', page());
    await render();
    clickButton('Salva sequenza', page());
    await render();

    expect(api.commands).toEqual([
      {
        name: 'ReplaceLessonExercises',
        payload: {
          lessonId: 'lz1',
          baseRowVersion: '5',
          exercises: [{ exerciseId: firstExerciseId }, { exerciseId: secondExerciseId }],
        },
      },
    ]);
    // Dopo il salvataggio la pagina rilegge la sequenza e la bozza coincide con il server.
    expect(sequenceNames()).toEqual(['Somma', 'Ciclo']);
    expect(saveDisabled()).toBe(true);
  });

  it('salva anche una sequenza svuotata', async () => {
    api.sequence = [firstAssociation];

    await render();

    clickButton('Rimuovi Somma', page());
    await render();
    clickButton('Salva sequenza', page());
    await render();

    expect(api.commands).toEqual([
      {
        name: 'ReplaceLessonExercises',
        payload: { lessonId: 'lz1', baseRowVersion: '5', exercises: [] },
      },
    ]);
    expect(pageText()).toContain('La sequenza non ha ancora esercizi');
  });

  it('mostra il rifiuto del server e conserva la bozza da salvare', async () => {
    api.sequence = [firstAssociation];

    await render();
    api.failure = new ApiError('EntityVersionMismatch', 409, 'La riga è cambiata nel frattempo.');

    setSelect('#add-exercise', secondExerciseId);
    await render();
    clickButton('Aggiungi', page());
    await render();
    clickButton('Salva sequenza', page());
    await render();

    expect(text()).toContain('La riga è cambiata nel frattempo.');
    expect(sequenceNames()).toEqual(['Somma', 'Ciclo']);
    expect(saveDisabled()).toBe(false);
  });

  it('mostra l’errore di lettura conservando il percorso verso il corso', async () => {
    api.loadFailure = new ApiError('ResourceNotFound', 404, 'Risorsa non trovata.');

    await render();

    expect(text()).toContain('Risorsa non trovata.');
    expect(breadcrumbLinks()).toContain('/courses/c1');
  });

  /**
   * Aggiorna la vista e attende che la catena di promesse avviata dall'interazione sia conclusa.
   * `whenStable` da solo non basta: con la change detection senza `zone.js` la promessa di un
   * gestore non è un'attività registrata, quindi il test deve cedere il turno all'event loop.
   *
   * La pagina si monta qui e non in `beforeEach`: all'avvio il componente legge la lezione, quindi i
   * dati di prova devono essere già impostati quando il test chiede la prima resa.
   */
  async function render(): Promise<void> {
    if (!created) {
      created = true;
      harness = await RouterTestingHarness.create('/courses/c1/lessons/lz1');
    }

    harness.detectChanges();
    await harness.fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    harness.detectChanges();
  }

  /** Elemento della pagina montata dal router: si rilegge perché la navigazione può rimontarla. */
  function page(): HTMLElement {
    const element = harness.routeNativeElement;
    if (element === null) {
      throw new Error('La pagina della route non è montata.');
    }

    return element;
  }

  /** Testo visibile complessivo: comprende la pagina e gli avvisi, che vivono in un overlay. */
  function text(): string {
    return document.body.textContent ?? '';
  }

  /** Testo della sola pagina. */
  function pageText(): string {
    return page().textContent ?? '';
  }

  function positions(): string[] {
    return [...page().querySelectorAll('.sequence-item .position')].map((element) =>
      (element.textContent ?? '').trim(),
    );
  }

  /** Voci del percorso, nell'ordine mostrato: l'ultima è la pagina corrente. */
  function breadcrumbTrail(): string[] {
    return [...page().querySelectorAll('app-detail-header [hlmBreadcrumbItem]')].map((item) =>
      (item.textContent ?? '').trim(),
    );
  }

  /** Destinazioni dei livelli visitabili del percorso. */
  function breadcrumbLinks(): string[] {
    return [
      ...page().querySelectorAll<HTMLAnchorElement>('app-detail-header [hlmBreadcrumbLink]'),
    ].map((link) => link.getAttribute('href') ?? '');
  }

  function sequenceNames(): string[] {
    return [...page().querySelectorAll('.sequence-item .sequence-name')].map((element) =>
      (element.textContent ?? '').trim(),
    );
  }

  /** Maniglia di trascinamento della riga: è il controllo da cui si cambia l'ordine. */
  function handle(index: number): HTMLButtonElement {
    const element = page().querySelectorAll<HTMLButtonElement>('.sequence-handle')[index];
    if (element === undefined) {
      throw new Error(`Maniglia non trovata alla riga ${String(index)}.`);
    }

    return element;
  }

  /**
   * Freccia da tastiera sulla maniglia: è la via alternativa al trascinamento, l'unica che un test
   * DOM può esercitare, perché il gesto del puntatore dipende dal layout.
   */
  function pressArrow(index: number, key: 'ArrowUp' | 'ArrowDown'): void {
    handle(index).dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
  }

  function saveDisabled(): boolean {
    return textButton('Salva sequenza').disabled;
  }

  function resetDisabled(): boolean {
    return textButton('Annulla modifiche').disabled;
  }

  /** Pulsante riconosciuto dal solo testo visibile: le azioni della pagina non sono icone. */
  function textButton(label: string): HTMLButtonElement {
    const element = [...page().querySelectorAll('button')].find(
      (candidate) => (candidate.textContent ?? '').trim() === label,
    );
    if (element === undefined) {
      throw new Error(`Pulsante non trovato: ${label}`);
    }

    return element;
  }

  /** Clic su un pulsante riconosciuto dal testo visibile o dal nome accessibile. */
  function clickButton(label: string, scope: ParentNode): void {
    const element = [...scope.querySelectorAll('button')].find((candidate) => {
      const text = (candidate.textContent ?? '').trim();
      const accessibleName = (candidate.getAttribute('aria-label') ?? '').trim();

      return text === label || accessibleName.startsWith(label);
    });
    if (element === undefined) {
      throw new Error(`Pulsante non trovato: ${label}`);
    }

    element.click();
  }

  function select(): HTMLSelectElement {
    const element = page().querySelector<HTMLSelectElement>('#add-exercise');
    if (element === null) {
      throw new Error('Selettore degli esercizi non trovato.');
    }

    return element;
  }

  /** Valori offerti dal selettore, escluso il segnaposto che invita a scegliere. */
  function optionValues(): string[] {
    return [...select().querySelectorAll('option')]
      .map((option) => option.value)
      .filter((value) => value !== '');
  }

  function setSelect(selector: string, value: string): void {
    const element = page().querySelector<HTMLSelectElement>(selector);
    if (element === null) {
      throw new Error(`Elenco non trovato: ${selector}`);
    }

    element.value = value;
    element.dispatchEvent(new Event('change'));
  }
});
