import { OverlayModule } from '@angular/cdk/overlay';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ApiError, ApiService, Classroom, School } from '@core/api';
import { SchoolDetail } from './school-detail';

/** Sostituto deterministico del dispatcher: registra le richieste e restituisce quello che serve. */
class ApiStub {
  readonly queries: { name: string; payload: Record<string, unknown> }[] = [];
  readonly commands: { name: string; payload: Record<string, unknown> }[] = [];

  school: School = { id: 'a1', name: 'Scuola Manzoni', isArchived: false, rowVersion: '4' };
  classrooms: readonly Classroom[] = [];

  /** Rifiuto delle mutazioni, come una versione obsoleta o una classe già esistente. */
  failure: ApiError | null = null;

  /** Rifiuto della lettura, come un indirizzo di una scuola che non esiste più. */
  loadFailure: ApiError | null = null;

  async executeQuery<TResponse>(
    name: string,
    payload: Record<string, unknown> = {},
  ): Promise<TResponse> {
    this.queries.push({ name, payload });

    if (this.loadFailure !== null) {
      throw this.loadFailure;
    }

    return (name === 'GetSchool' ? this.school : this.classrooms) as TResponse;
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

const classroomsTable = 'table[aria-label="Classi della scuola"]';

/**
 * La pagina si monta da una route vera: la scuola da mostrare viaggia nell'indirizzo, quindi il
 * banco di prova deve avere router e parametri come quelli del browser. Le interazioni sono reali —
 * apertura del dialogo, digitazione, invio, azioni di riga — e i campi si cercano sul documento,
 * perché il dialogo vive in un overlay del CDK.
 */
describe('SchoolDetail', () => {
  let harness!: RouterTestingHarness;
  let created = false;
  let api: ApiStub;

  const classroom: Classroom = {
    id: 'k1',
    schoolId: 'a1',
    name: '2ª A',
    schoolYear: '2026/2027',
    isArchived: false,
    rowVersion: '2',
  };

  beforeEach(async () => {
    api = new ApiStub();
    created = false;

    await TestBed.configureTestingModule({
      imports: [OverlayModule],
      providers: [
        { provide: ApiService, useValue: api },
        provideRouter([{ path: 'schools/:schoolId', component: SchoolDetail }]),
      ],
    }).compileComponents();
  });

  it('legge la scuola dall’indirizzo e presenta le sue classi', async () => {
    api.classrooms = [classroom];

    await render();

    expect(api.queries).toEqual([
      { name: 'GetSchool', payload: { id: 'a1' } },
      { name: 'ListClassrooms', payload: { schoolId: 'a1', includeArchived: false } },
    ]);
    expect(pageText()).toContain('Scuola Manzoni');
    expect(pageText()).toContain('1 classe');
    expect(rows(classroomsTable)).toBe(1);
  });

  it('dichiara quante classi ha la scuola e da dove si arriva', async () => {
    // L'intestazione dice dove si è (nome della scuola), da dove si arriva (l'elenco, nel percorso)
    // e che cosa contiene la pagina: è ciò che distingue un dettaglio da un secondo elenco piatto.
    api.classrooms = [classroom, { ...classroom, id: 'k2', name: '2ª B' }];

    await render();

    expect(pageText()).toContain('2 classi');
    expect(breadcrumbTrail()).toEqual(['Scuole', 'Scuola Manzoni']);
    expect(breadcrumbLinks()).toEqual(['/schools']);
  });

  it('mostra lo stato vuoto quando la scuola non ha classi', async () => {
    await render();

    expect(pageText()).toContain('Questa scuola non ha ancora classi');
    expect(rows(classroomsTable)).toBe(0);
  });

  it('dichiara una scuola archiviata invece di farla sembrare attiva', async () => {
    api.school = { ...api.school, isArchived: true };

    await render();

    expect(pageText()).toContain('scuola archiviata');
  });

  it('crea una classe nella scuola dell’indirizzo', async () => {
    await render();

    clickButton('Nuova classe', page());
    await render();
    expect(dialogText()).toContain('Nuova classe');

    setInput('#new-classroom-name', '  3ª B  ');
    setInput('#new-classroom-year', '2027/2028');
    await render();
    clickButton('Crea classe', dialog());
    await render();

    expect(api.commands).toEqual([
      {
        name: 'CreateClassroom',
        payload: { schoolId: 'a1', name: '3ª B', schoolYear: '2027/2028' },
      },
    ]);
    // La pagina rilegge: la classe appena creata non è quella che la pagina ricordava.
    expect(api.queries.length).toBe(4);
    expect(document.querySelector('#new-classroom-name')).toBeNull();
  });

  it('modifica una classe con i valori della riga e la versione letta', async () => {
    api.classrooms = [classroom];

    await render();

    clickButton('Modifica', table(classroomsTable));
    await render();
    expect(input('#edit-classroom-name').value).toBe('2ª A');
    expect(input('#edit-classroom-year').value).toBe('2026/2027');

    setInput('#edit-classroom-name', '2ª C');
    await render();
    clickButton('Salva classe', dialog());
    await render();

    expect(api.commands).toEqual([
      {
        name: 'UpdateClassroom',
        payload: {
          id: 'k1',
          name: '2ª C',
          schoolYear: '2026/2027',
          baseRowVersion: '2',
        },
      },
    ]);
  });

  it('archivia la classe solo dopo la conferma', async () => {
    api.classrooms = [classroom];

    await render();

    clickButton('Archivia', table(classroomsTable));
    await render();

    expect(pageText()).toContain('Archiviare «2ª A»?');
    expect(api.commands.length).toBe(0);

    clickButton('Conferma archiviazione', table(classroomsTable));
    await render();

    expect(api.commands).toEqual([
      { name: 'ArchiveClassroom', payload: { id: 'k1', baseRowVersion: '2' } },
    ]);
  });

  it('rifiuta un anno scolastico non consecutivo con un errore nel campo', async () => {
    await render();

    clickButton('Nuova classe', page());
    await render();
    setInput('#new-classroom-name', '3ª B');
    setInput('#new-classroom-year', '2026/2028');
    await render();
    clickButton('Crea classe', dialog());
    await render();

    expect(api.commands.length).toBe(0);
    expect(dialogText()).toContain('Usa due anni consecutivi');
  });

  it('mostra il rifiuto del server e lascia il modulo aperto', async () => {
    await render();
    api.failure = new ApiError('EntityVersionMismatch', 409, 'La riga è cambiata nel frattempo.');

    clickButton('Nuova classe', page());
    await render();
    setInput('#new-classroom-name', '3ª B');
    await render();
    clickButton('Crea classe', dialog());
    await render();

    expect(text()).toContain('La riga è cambiata nel frattempo.');
    expect(input('#new-classroom-name').value).toBe('3ª B');
  });

  it('mostra l’errore di lettura con il ritorno all’elenco quando la scuola non esiste', async () => {
    api.loadFailure = new ApiError('ResourceNotFound', 404, 'Risorsa non trovata.');

    await render();

    expect(text()).toContain('Risorsa non trovata.');
    expect(page().querySelector('app-detail-header a')?.getAttribute('href')).toBe('/schools');
    expect(rows(classroomsTable)).toBe(0);
  });

  /**
   * Aggiorna la vista e attende che la catena di promesse avviata dall'interazione sia conclusa.
   * `whenStable` da solo non basta: con la change detection senza `zone.js` la promessa di un
   * gestore non è un'attività registrata, quindi il test deve cedere il turno all'event loop.
   *
   * La pagina si monta qui e non in `beforeEach`: all'avvio il componente legge la scuola, quindi i
   * dati di prova devono essere già impostati quando il test chiede la prima resa.
   */
  async function render(): Promise<void> {
    if (!created) {
      created = true;
      harness = await RouterTestingHarness.create('/schools/a1');
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
