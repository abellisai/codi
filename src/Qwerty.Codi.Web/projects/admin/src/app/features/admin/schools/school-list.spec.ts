import { OverlayModule } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ApiError, ApiService, School } from '@core/api';
import { SchoolList } from './school-list';

/** Sostituto deterministico del dispatcher: registra le richieste e restituisce quello che serve. */
class ApiStub {
  readonly queries: { name: string; payload: Record<string, unknown> }[] = [];
  readonly commands: { name: string; payload: Record<string, unknown> }[] = [];

  schools: readonly School[] = [];

  /** Rifiuto delle mutazioni, come un nome già in uso o una versione obsoleta. */
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

    return this.schools as TResponse;
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

const schoolsTable = 'table[aria-label="Scuole configurate"]';

/**
 * L'elenco si prova dalle interazioni reali: apertura del dialogo, digitazione nei campi, invio del
 * modulo e clic sulle azioni di riga. I campi del dialogo vivono in un overlay del CDK, quindi le
 * ricerche avvengono sul documento e non solo sull'albero del componente.
 */
describe('SchoolList', () => {
  let fixture: ComponentFixture<SchoolList>;
  let api: ApiStub;

  beforeEach(async () => {
    api = new ApiStub();

    await TestBed.configureTestingModule({
      imports: [SchoolList, OverlayModule],
      providers: [{ provide: ApiService, useValue: api }, provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(SchoolList);
  });

  it('carica le scuole non archiviate e le presenta in tabella', async () => {
    api.schools = [{ id: 'a1', name: 'Scuola Manzoni', isArchived: false, rowVersion: '1' }];

    await render();

    expect(api.queries).toEqual([{ name: 'ListSchools', payload: { includeArchived: false } }]);
    expect(rows(schoolsTable)).toBe(1);
    expect(pageText()).toContain('Scuola Manzoni');
  });

  it('mostra lo stato vuoto quando non ci sono scuole', async () => {
    await render();

    expect(pageText()).toContain('Non ci sono scuole configurate');
    expect(rows(schoolsTable)).toBe(0);
  });

  it('porta alle classi: la riga offre il collegamento alla pagina della scuola', async () => {
    // La navigazione dell'area è per livelli: dall'elenco si entra nella scuola scelta. Il
    // livello successivo si raggiunge da un pulsante che lo nomina: il collegamento sul nome della
    // cella non si vedeva.
    api.schools = [{ id: 'a1', name: 'Scuola Manzoni', isArchived: false, rowVersion: '1' }];

    await render();

    expect(rowLink()).toBe('/schools/a1');
  });

  it('pagina l’elenco delle scuole', async () => {
    api.schools = Array.from({ length: 15 }, (_, index) => ({
      id: `a${String(index)}`,
      name: `Scuola ${String(index)}`,
      isArchived: false,
      rowVersion: '1',
    }));

    await render();

    expect(rows(schoolsTable)).toBe(10);

    clickPage('2');
    await render();

    expect(rows(schoolsTable)).toBe(5);
    expect(pageText()).toContain('Scuola 14');
  });

  it('crea una scuola dal dialogo con il nome digitato e chiude il dialogo', async () => {
    await render();

    clickButton('Nuova scuola', page());
    await render();
    expect(dialogText()).toContain('Nuova scuola');

    setInput('#new-school-name', '  Scuola Rossi  ');
    await render();
    clickButton('Crea scuola', dialog());
    await render();

    expect(api.commands).toEqual([{ name: 'CreateSchool', payload: { name: 'Scuola Rossi' } }]);
    expect(api.queries.length).toBe(2);
    expect(document.querySelector('#new-school-name')).toBeNull();
  });

  it('rinomina la scuola con la versione letta dal server', async () => {
    api.schools = [{ id: 'a1', name: 'Scuola Manzoni', isArchived: false, rowVersion: '7' }];

    await render();

    clickButton('Rinomina', table(schoolsTable));
    await render();
    expect(input('#edit-school-name').value).toBe('Scuola Manzoni');

    setInput('#edit-school-name', 'Scuola Forcellini');
    await render();
    clickButton('Salva scuola', dialog());
    await render();

    expect(api.commands).toEqual([
      {
        name: 'UpdateSchool',
        payload: { id: 'a1', name: 'Scuola Forcellini', baseRowVersion: '7' },
      },
    ]);
  });

  it('archivia la scuola solo dopo la conferma', async () => {
    api.schools = [{ id: 'a1', name: 'Scuola Manzoni', isArchived: false, rowVersion: '2' }];

    await render();

    clickButton('Archivia', table(schoolsTable));
    await render();

    expect(pageText()).toContain('Archiviare «Scuola Manzoni»?');
    expect(api.commands.length).toBe(0);

    clickButton('Conferma archiviazione', table(schoolsTable));
    await render();

    expect(api.commands).toEqual([
      { name: 'ArchiveSchool', payload: { id: 'a1', baseRowVersion: '2' } },
    ]);
    expect(api.queries.length).toBe(2);
  });

  it('mostra il rifiuto del server e non perde quello che è stato digitato', async () => {
    await render();
    api.failure = new ApiError(
      'SchoolAlreadyExists',
      409,
      'Esiste già una scuola con questo nome.',
    );

    clickButton('Nuova scuola', page());
    await render();
    setInput('#new-school-name', 'Scuola Manzoni');
    await render();
    clickButton('Crea scuola', dialog());
    await render();

    expect(api.commands.length).toBe(1);
    expect(text()).toContain('Esiste già una scuola con questo nome.');
    expect(input('#new-school-name').value).toBe('Scuola Manzoni');
  });

  it('invita a riaccedere quando la sessione non è più valida', async () => {
    // Una pagina ricaricata dopo la scadenza non deve sembrare vuota: il rifiuto si mostra, e
    // con esso il rimedio.
    api.loadFailure = new ApiError(
      'AuthenticationRequired',
      401,
      'La richiesta richiede un accesso.',
    );

    await render();

    expect(text()).toContain('La richiesta richiede un accesso.');
    expect(text()).toContain('Accedi di nuovo');
  });

  it('rifiuta un nome di scuola vuoto con un errore nel campo', async () => {
    await render();

    clickButton('Nuova scuola', page());
    await render();
    clickButton('Crea scuola', dialog());
    await render();

    expect(api.commands.length).toBe(0);
    expect(dialogText()).toContain('Nome della scuola: compila il campo.');
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
        .querySelector<HTMLAnchorElement>('a[aria-label^="Classi di "]')
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

  /** Clic su una voce della paginazione, che la libreria rende come collegamento o pulsante. */
  function clickPage(label: string): void {
    const item = [...page().querySelectorAll<HTMLElement>('a, button')].find(
      (candidate) => (candidate.textContent ?? '').trim() === label,
    );
    if (item === undefined) {
      throw new Error(`Voce di paginazione non trovata: ${label}`);
    }

    item.click();
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
