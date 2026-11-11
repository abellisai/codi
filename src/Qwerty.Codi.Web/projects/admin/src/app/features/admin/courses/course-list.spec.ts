import { OverlayModule } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ApiError, ApiService, Course } from '@core/api';
import { CourseList } from './course-list';

/** Sostituto deterministico del dispatcher: registra le richieste e restituisce quello che serve. */
class ApiStub {
  readonly queries: { name: string; payload: Record<string, unknown> }[] = [];
  readonly commands: { name: string; payload: Record<string, unknown> }[] = [];

  courses: readonly Course[] = [];

  /** Rifiuto delle mutazioni, come un titolo già in uso o una versione obsoleta. */
  failure: ApiError | null = null;

  /** Rifiuto della lettura, come una sessione scaduta su una pagina ricaricata. */
  loadFailure: ApiError | null = null;

  async executeQuery<TResponse>(
    name: string,
    payload: Record<string, unknown> = {},
  ): Promise<TResponse> {
    this.queries.push({ name, payload });

    if (this.loadFailure !== null) {
      throw this.loadFailure;
    }

    return this.courses as TResponse;
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

const coursesTable = 'table[aria-label="Corsi configurati"]';

/**
 * L'elenco si prova dalle interazioni reali: apertura del dialogo, digitazione nei campi, invio del
 * modulo e clic sulle azioni di riga. I campi del dialogo vivono in un overlay del CDK, quindi le
 * ricerche avvengono sul documento e non solo sull'albero del componente.
 */
describe('CourseList', () => {
  let fixture: ComponentFixture<CourseList>;
  let api: ApiStub;

  const course: Course = {
    id: 'c1',
    title: 'Informatica di base',
    description: 'Con esercizi',
    isArchived: false,
    rowVersion: '3',
  };

  beforeEach(async () => {
    api = new ApiStub();

    await TestBed.configureTestingModule({
      imports: [CourseList, OverlayModule],
      providers: [{ provide: ApiService, useValue: api }, provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(CourseList);
  });

  it('carica i corsi non archiviati e li presenta in tabella', async () => {
    api.courses = [course];

    await render();

    expect(api.queries).toEqual([{ name: 'ListCourses', payload: { includeArchived: false } }]);
    expect(rows(coursesTable)).toBe(1);
    expect(pageText()).toContain('Informatica di base');
    expect(pageText()).toContain('Con esercizi');
  });

  it('mostra lo stato vuoto quando non ci sono corsi', async () => {
    await render();

    expect(pageText()).toContain('Non ci sono corsi configurati');
    expect(rows(coursesTable)).toBe(0);
  });

  it('porta alle lezioni: la riga offre il collegamento alla pagina del corso', async () => {
    api.courses = [course];

    await render();

    expect(rowLink()).toBe('/courses/c1');
  });

  it('crea un corso dal dialogo con titolo e descrizione digitati', async () => {
    await render();

    clickButton('Nuovo corso', page());
    await render();
    expect(dialogText()).toContain('Nuovo corso');

    setInput('#new-course-title', '  Atelier di robotica  ');
    setInput('#new-course-description', '  Con i sensori  ');
    await render();
    clickButton('Crea corso', dialog());
    await render();

    expect(api.commands).toEqual([
      {
        name: 'CreateCourse',
        payload: { title: 'Atelier di robotica', description: 'Con i sensori' },
      },
    ]);
    expect(api.queries.length).toBe(2);
    expect(document.querySelector('#new-course-title')).toBeNull();
  });

  it('modifica un corso con la versione letta dal server', async () => {
    api.courses = [course];

    await render();

    clickButton('Modifica', table(coursesTable));
    await render();
    expect(input('#edit-course-title').value).toBe('Informatica di base');
    expect(input('#edit-course-description').value).toBe('Con esercizi');

    setInput('#edit-course-title', '  Informatica  ');
    await render();
    clickButton('Salva corso', dialog());
    await render();

    expect(api.commands).toEqual([
      {
        name: 'UpdateCourse',
        payload: {
          id: 'c1',
          title: 'Informatica',
          description: 'Con esercizi',
          baseRowVersion: '3',
        },
      },
    ]);
  });

  it('archivia il corso solo dopo la conferma', async () => {
    api.courses = [course];

    await render();

    clickButton('Archivia', table(coursesTable));
    await render();

    expect(pageText()).toContain('Archiviare «Informatica di base»?');
    expect(api.commands.length).toBe(0);

    clickButton('Conferma archiviazione', table(coursesTable));
    await render();

    expect(api.commands).toEqual([
      { name: 'ArchiveCourse', payload: { id: 'c1', baseRowVersion: '3' } },
    ]);
    expect(api.queries.length).toBe(2);
  });

  it('rifiuta un titolo vuoto con un errore nel campo', async () => {
    await render();

    clickButton('Nuovo corso', page());
    await render();
    clickButton('Crea corso', dialog());
    await render();

    expect(api.commands.length).toBe(0);
    expect(dialogText()).toContain('Titolo: compila il campo.');
  });

  it('mostra il rifiuto del server mantenendo i valori digitati', async () => {
    await render();
    api.failure = new ApiError('EntityVersionMismatch', 409, 'La riga è cambiata nel frattempo.');

    clickButton('Nuovo corso', page());
    await render();
    setInput('#new-course-title', 'Informatica di base');
    await render();
    clickButton('Crea corso', dialog());
    await render();

    expect(text()).toContain('La riga è cambiata nel frattempo.');
    expect(input('#new-course-title').value).toBe('Informatica di base');
  });

  it('invita a riaccedere quando la sessione non è più valida', async () => {
    api.loadFailure = new ApiError(
      'AuthenticationRequired',
      401,
      'La richiesta richiede un accesso.',
    );

    await render();

    expect(text()).toContain('La richiesta richiede un accesso.');
    expect(text()).toContain('Accedi di nuovo');
  });

  /**
   * Aggiorna la vista e attende che la catena di promesse avviata dall'interazione sia conclusa.
   * `whenStable` da solo non basta: con la change detection senza `zone.js` la promessa di un
   * gestore non è un'attività registrata, quindi il test deve cedere il turno all'event loop.
   */
  async function render(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  /** Testo visibile complessivo: comprende la pagina e i dialoghi, che vivono in un overlay. */
  function text(): string {
    return document.body.textContent ?? '';
  }

  /** Testo della sola pagina, senza il contenuto dei dialoghi. */
  function pageText(): string {
    return page().textContent ?? '';
  }

  function page(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
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

  /** Destinazione del pulsante che nomina il livello successivo della riga. */
  function rowLink(): string {
    return (
      page()
        .querySelector<HTMLAnchorElement>('a[aria-label^="Lezioni di "]')
        ?.getAttribute('href') ?? ''
    );
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
