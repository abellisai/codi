import { OverlayModule } from '@angular/cdk/overlay';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ApiError, ApiService, Course, Lesson } from '@core/api';
import { CourseDetail } from './course-detail';

/** Sostituto deterministico del dispatcher: registra le richieste e restituisce quello che serve. */
class ApiStub {
  readonly queries: { name: string; payload: Record<string, unknown> }[] = [];
  readonly commands: { name: string; payload: Record<string, unknown> }[] = [];

  course: Course = {
    id: 'c1',
    title: 'Informatica di base',
    description: 'Con esercizi',
    isArchived: false,
    rowVersion: '3',
  };
  lessons: readonly Lesson[] = [];

  /** Rifiuto delle mutazioni, come una versione obsoleta o un titolo già esistente. */
  failure: ApiError | null = null;

  /** Rifiuto della lettura, come un indirizzo di un corso che non esiste più. */
  loadFailure: ApiError | null = null;

  async executeQuery<TResponse>(
    name: string,
    payload: Record<string, unknown> = {},
  ): Promise<TResponse> {
    this.queries.push({ name, payload });

    if (this.loadFailure !== null) {
      throw this.loadFailure;
    }

    return (name === 'GetCourse' ? this.course : this.lessons) as TResponse;
  }

  async executeCommand<TResponse>(
    name: string,
    payload: Record<string, unknown> = {},
  ): Promise<TResponse> {
    this.commands.push({ name, payload });

    if (this.failure !== null) {
      throw this.failure;
    }

    return {} as TResponse;
  }
}

const lessonsTable = 'table[aria-label="Lezioni del corso"]';

/**
 * La pagina si monta da una route vera: il corso da mostrare viaggia nell'indirizzo, quindi il
 * banco di prova deve avere router e parametri come quelli del browser. Le interazioni sono reali —
 * apertura del dialogo, digitazione, invio, azioni di riga — e i campi si cercano sul documento,
 * perché il dialogo vive in un overlay del CDK.
 */
describe('CourseDetail', () => {
  let harness!: RouterTestingHarness;
  let created = false;
  let api: ApiStub;

  const lesson: Lesson = {
    id: 'lz1',
    courseId: 'c1',
    title: 'Lezione uno',
    description: 'Primi passi',
    isArchived: false,
    rowVersion: '5',
  };

  beforeEach(async () => {
    api = new ApiStub();
    created = false;

    await TestBed.configureTestingModule({
      imports: [OverlayModule],
      providers: [
        { provide: ApiService, useValue: api },
        provideRouter([{ path: 'courses/:courseId', component: CourseDetail }]),
      ],
    }).compileComponents();
  });

  it('legge il corso dall’indirizzo e presenta le sue lezioni', async () => {
    api.lessons = [lesson];

    await render();

    expect(api.queries).toEqual([
      { name: 'GetCourse', payload: { id: 'c1' } },
      { name: 'ListLessons', payload: { courseId: 'c1', includeArchived: false } },
    ]);
    expect(pageText()).toContain('Informatica di base');
    expect(pageText()).toContain('1 lezione');
    expect(rows(lessonsTable)).toBe(1);
  });

  it('dice quante lezioni ha il corso, la sua descrizione e da dove si arriva', async () => {
    api.lessons = [lesson, { ...lesson, id: 'lz2', title: 'Lezione due' }];

    await render();

    expect(pageText()).toContain('2 lezioni');
    expect(pageText()).toContain('Con esercizi');
    expect(breadcrumbTrail()).toEqual(['Corsi', 'Informatica di base']);
    expect(breadcrumbLinks()).toEqual(['/courses']);
  });

  it('mostra lo stato vuoto quando il corso non ha lezioni', async () => {
    await render();

    expect(pageText()).toContain('Questo corso non ha ancora lezioni');
    expect(rows(lessonsTable)).toBe(0);
  });

  it('porta alla sequenza con un pulsante che dichiara dove si va', async () => {
    api.lessons = [lesson];

    await render();

    const link = detailLink();
    expect(link.getAttribute('href')).toBe('/courses/c1/lessons/lz1');
    expect((link.textContent ?? '').trim()).toBe('Esercizi');
    expect(link.getAttribute('aria-label')).toBe('Esercizi di Lezione uno');
  });

  it('crea una lezione nel corso dell’indirizzo', async () => {
    await render();

    clickButton('Nuova lezione', page());
    await render();
    expect(dialogText()).toContain('Nuova lezione');

    setInput('#new-lesson-title', '  Lezione due  ');
    setInput('#new-lesson-description', '  Con i cicli  ');
    await render();
    clickButton('Crea lezione', dialog());
    await render();

    expect(api.commands).toEqual([
      {
        name: 'CreateLesson',
        payload: { courseId: 'c1', title: 'Lezione due', description: 'Con i cicli' },
      },
    ]);
    expect(api.queries.length).toBe(4);
    expect(document.querySelector('#new-lesson-title')).toBeNull();
  });

  it('modifica una lezione con i valori della riga e la versione letta', async () => {
    api.lessons = [lesson];

    await render();

    clickButton('Modifica', table(lessonsTable));
    await render();
    expect(input('#edit-lesson-title').value).toBe('Lezione uno');
    expect(input('#edit-lesson-description').value).toBe('Primi passi');

    setInput('#edit-lesson-title', 'Lezione introduttiva');
    await render();
    clickButton('Salva lezione', dialog());
    await render();

    expect(api.commands).toEqual([
      {
        name: 'UpdateLesson',
        payload: {
          id: 'lz1',
          title: 'Lezione introduttiva',
          description: 'Primi passi',
          baseRowVersion: '5',
        },
      },
    ]);
  });

  it('archivia la lezione solo dopo la conferma', async () => {
    api.lessons = [lesson];

    await render();

    clickButton('Archivia', table(lessonsTable));
    await render();

    expect(pageText()).toContain('Archiviare «Lezione uno»?');
    expect(api.commands.length).toBe(0);

    clickButton('Conferma archiviazione', table(lessonsTable));
    await render();

    expect(api.commands).toEqual([
      { name: 'ArchiveLesson', payload: { id: 'lz1', baseRowVersion: '5' } },
    ]);
  });

  it('mostra l’errore di lettura con il ritorno all’elenco quando il corso non esiste', async () => {
    api.loadFailure = new ApiError('ResourceNotFound', 404, 'Risorsa non trovata.');

    await render();

    expect(text()).toContain('Risorsa non trovata.');
    expect(page().querySelector('app-detail-header a')?.getAttribute('href')).toBe('/courses');
    expect(rows(lessonsTable)).toBe(0);
  });

  /**
   * Aggiorna la vista e attende che la catena di promesse avviata dall'interazione sia conclusa.
   * `whenStable` da solo non basta: con la change detection senza `zone.js` la promessa di un
   * gestore non è un'attività registrata, quindi il test deve cedere il turno all'event loop.
   *
   * La pagina si monta qui e non in `beforeEach`: all'avvio il componente legge il corso, quindi i
   * dati di prova devono essere già impostati quando il test chiede la prima resa.
   */
  async function render(): Promise<void> {
    if (!created) {
      created = true;
      harness = await RouterTestingHarness.create('/courses/c1');
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

  /** Testo visibile complessivo: comprende la pagina e i dialoghi, che vivono in un overlay. */
  function text(): string {
    return document.body.textContent ?? '';
  }

  /** Testo della sola pagina, senza il contenuto dei dialoghi. */
  function pageText(): string {
    return page().textContent ?? '';
  }

  function table(selector: string): HTMLElement {
    const element = page().querySelector<HTMLElement>(selector);
    if (element === null) {
      throw new Error(`Tabella non trovata: ${selector}`);
    }

    return element;
  }

  /** Il dialogo aperto, quando c'è: i suoi campi vivono in un overlay, fuori dalla pagina. */
  function dialog(): HTMLElement {
    const element = document.querySelector<HTMLElement>('hlm-dialog-content');
    if (element === null) {
      throw new Error('Nessun dialogo aperto.');
    }

    return element;
  }

  function dialogText(): string {
    return dialog().textContent ?? '';
  }

  function rows(selector: string): number {
    return page().querySelectorAll(`${selector} tbody tr`).length;
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

  /** Pulsante di riga che porta alla pagina di dettaglio: è un collegamento con aspetto di pulsante. */
  function detailLink(): HTMLAnchorElement {
    const element = page().querySelector<HTMLAnchorElement>('a[hlmBtn]');
    if (element === null) {
      throw new Error('Pulsante di navigazione della riga non trovato.');
    }

    return element;
  }

  function input(selector: string): HTMLInputElement {
    const element = document.querySelector<HTMLInputElement>(selector);
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

  /** Clic su un pulsante per etichetta, limitato all'ambito indicato. */
  function clickButton(label: string, scope: ParentNode): void {
    const button = [...scope.querySelectorAll('button')].find((candidate) => {
      // Le azioni di riga sono pulsanti icona: il nome accessibile è l'unica etichetta
      // leggibile, quindi si confronta anche quello.
      const text = (candidate.textContent ?? '').trim();
      const accessibleName = (candidate.getAttribute('aria-label') ?? '').trim();

      return text === label || accessibleName.startsWith(label);
    });
    if (button === undefined) {
      throw new Error(`Pulsante non trovato: ${label}`);
    }

    button.click();
  }
});
