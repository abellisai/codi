import { OverlayModule } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ApiError, ApiService, Exercise } from '@core/api';
import { ExerciseList } from './exercise-list';

/** Sostituto deterministico del dispatcher: registra le richieste e restituisce quello che serve. */
class ApiStub {
  readonly queries: { name: string; payload: Record<string, unknown> }[] = [];
  readonly commands: { name: string; payload: Record<string, unknown> }[] = [];

  exercises: readonly Exercise[] = [];

  /** Rifiuto delle mutazioni, come una versione obsoleta o un titolo già in uso. */
  failure: ApiError | null = null;

  /** Rifiuto della lettura, come una sessione scaduta su una pagina ricaricata. */
  loadFailure: ApiError | null = null;

  executeQuery<TResponse>(name: string, payload: Record<string, unknown> = {}): Promise<TResponse> {
    this.queries.push({ name, payload });
    return this.respond<TResponse>(name);
  }

  executeCommand<TResponse>(
    name: string,
    payload: Record<string, unknown> = {},
  ): Promise<TResponse> {
    this.commands.push({ name, payload });
    return this.respond<TResponse>(name);
  }

  private async respond<TResponse>(name: string): Promise<TResponse> {
    if (name === 'ListExercises') {
      if (this.loadFailure !== null) {
        throw this.loadFailure;
      }

      return this.exercises as TResponse;
    }

    if (this.failure !== null) {
      throw this.failure;
    }

    return {} as TResponse;
  }
}

/**
 * La pagina si prova dalle interazioni reali: apertura del dialogo, digitazione nei campi,
 * invio del modulo e clic sulle azioni della riga. I campi del dialogo vivono in un overlay
 * del CDK, quindi le ricerche avvengono sul documento e non solo sull'albero del componente:
 * è il posto in cui l'utente li vede.
 */
describe('ExerciseList', () => {
  let fixture: ComponentFixture<ExerciseList>;
  let api: ApiStub;

  beforeEach(async () => {
    api = new ApiStub();

    await TestBed.configureTestingModule({
      imports: [ExerciseList, OverlayModule],
      providers: [{ provide: ApiService, useValue: api }, provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(ExerciseList);
  });

  it('carica gli esercizi non archiviati e li presenta in tabella', async () => {
    api.exercises = [
      {
        id: 'e1',
        exerciseType: 'GridPath',
        title: 'Somma',
        description: 'Due numeri',
        isArchived: false,
        rowVersion: '1',
      },
    ];

    await render();

    expect(api.queries).toEqual([{ name: 'ListExercises', payload: { includeArchived: false } }]);
    expect(rowCount()).toBe(1);
    expect(tableText()).toContain('Somma');
    expect(tableText()).toContain('Due numeri');
    // La tipologia si legge in italiano: il nome tecnico non è un'informazione per il docente.
    expect(tableText()).toContain('Percorso sulla griglia');
  });

  it('distingue le tipologie degli esercizi nella stessa griglia', async () => {
    api.exercises = [
      {
        id: 'e1',
        exerciseType: 'GridPath',
        title: 'Il percorso',
        description: '',
        isArchived: false,
        rowVersion: '1',
      },
      {
        id: 'e2',
        exerciseType: 'Quiz',
        title: 'La domanda',
        description: '',
        isArchived: false,
        rowVersion: '1',
      },
      {
        id: 'e3',
        exerciseType: 'ValueProgram',
        title: 'Le variabili',
        description: '',
        isArchived: false,
        rowVersion: '1',
      },
    ];

    await render();

    expect(tableText()).toContain('Percorso sulla griglia');
    expect(tableText()).toContain('Domanda con risposta');
    expect(tableText()).toContain('Programma con variabili');
  });

  it('mostra lo stato vuoto quando non ci sono esercizi', async () => {
    await render();

    expect(text()).toContain('Non ci sono esercizi configurati');
    expect(rowCount()).toBe(0);
  });

  it('pagina l’elenco: con più righe di una pagina ne mostra una sola pagina e offre le successive', async () => {
    api.exercises = Array.from({ length: 15 }, (_, index) => ({
      id: `e${String(index)}`,
      exerciseType: 'GridPath',
      title: `Esercizio ${String(index)}`,
      description: '',
      isArchived: false,
      rowVersion: '1',
    }));

    await render();

    // La pagina iniziale mostra 10 righe su 15 e la paginazione dichiara il totale.
    expect(rowCount()).toBe(10);
    expect(text()).toContain('15');

    clickLink('2');
    await render();

    expect(rowCount()).toBe(5);
    expect(tableText()).toContain('Esercizio 14');
  });

  it('crea un esercizio dal dialogo con i valori digitati e chiude il dialogo', async () => {
    await render();

    clickButton('Nuovo esercizio');
    await render();
    expect(dialogText()).toContain('Nuovo esercizio');

    setInput('#new-exercise-title', '  Ciclo  ');
    setInput('#new-exercise-description', '  Ripeti dieci volte  ');
    await render();

    clickButton('Crea esercizio');
    await render();

    expect(api.commands).toEqual([
      {
        name: 'CreateExercise',
        // Il tipo accompagna la creazione: è quello che decide quali campi mostrerà l'editor, e non
        // si cambia più dopo.
        payload: { exerciseType: 'GridPath', title: 'Ciclo', description: 'Ripeti dieci volte' },
      },
    ]);
    expect(api.queries.length).toBe(2);
    // Il dialogo è chiuso: il campo non è più nel documento.
    expect(document.querySelector('#new-exercise-title')).toBeNull();
  });

  it('modifica dal dialogo inviando la versione letta e usa quella riletta alla modifica successiva', async () => {
    api.exercises = [
      {
        id: 'e1',
        exerciseType: 'GridPath',
        title: 'Somma',
        description: '',
        isArchived: false,
        rowVersion: '3',
      },
    ];
    await render();

    clickButton('Modifica');
    await render();

    // Il dialogo parte dai valori della riga scelta.
    expect(input('#edit-exercise-title').value).toBe('Somma');

    setInput('#edit-exercise-title', 'Somma di due numeri');
    setInput('#edit-exercise-description', 'Con riporto');
    await render();

    // La rilettura restituisce la nuova versione autorevole, come dopo una scrittura riuscita.
    api.exercises = [
      {
        id: 'e1',
        exerciseType: 'GridPath',
        title: 'Somma di due numeri',
        description: 'Con riporto',
        isArchived: false,
        rowVersion: '4',
      },
    ];
    clickButton('Salva esercizio');
    await render();

    expect(api.commands).toEqual([
      {
        name: 'UpdateExercise',
        payload: {
          id: 'e1',
          title: 'Somma di due numeri',
          description: 'Con riporto',
          baseRowVersion: '3',
        },
      },
    ]);
    expect(document.querySelector('#edit-exercise-title')).toBeNull();

    // Una seconda modifica usa la versione ricaricata, evitando un conflitto artificiale.
    clickButton('Modifica');
    await render();
    setInput('#edit-exercise-title', 'Somma');
    await render();
    api.exercises = [
      {
        id: 'e1',
        exerciseType: 'GridPath',
        title: 'Somma',
        description: 'Con riporto',
        isArchived: false,
        rowVersion: '5',
      },
    ];
    clickButton('Salva esercizio');
    await render();

    expect(api.commands.at(-1)?.payload['baseRowVersion']).toBe('4');
    expect(tableText()).toContain('Somma');
  });

  it('archivia un esercizio solo dopo la conferma', async () => {
    api.exercises = [
      {
        id: 'e1',
        exerciseType: 'GridPath',
        title: 'Somma',
        description: '',
        isArchived: false,
        rowVersion: '2',
      },
    ];
    await render();

    clickButton('Archivia');
    await render();

    // Il primo clic chiede conferma e non scrive nulla.
    expect(api.commands).toEqual([]);
    expect(text()).toContain('Archiviare «Somma»?');

    clickButton('Conferma');
    await render();

    expect(api.commands).toEqual([
      { name: 'ArchiveExercise', payload: { id: 'e1', baseRowVersion: '2' } },
    ]);
  });

  it('rifiuta titoli non utilizzabili e descrizioni oltre il limite con un errore nel campo', async () => {
    await render();

    for (const title of ['', '   ', 'a'.repeat(161)]) {
      clickButton('Nuovo esercizio');
      await render();
      setInput('#new-exercise-title', title);
      await render();
      clickButton('Crea esercizio');
      await render();

      expect(api.commands).toEqual([]);
      expect(input('#new-exercise-title').getAttribute('aria-invalid')).toBe('true');
      expect(document.querySelector('hlm-field-error:not([hidden])')).not.toBeNull();

      clickButton('Annulla');
      await render();
    }

    clickButton('Nuovo esercizio');
    await render();
    setInput('#new-exercise-title', 'Ciclo');
    setInput('#new-exercise-description', 'a'.repeat(2001));
    await render();
    clickButton('Crea esercizio');
    await render();

    expect(api.commands).toEqual([]);
    expect(dialogText()).toContain('Descrizione: usa al massimo 2000 caratteri.');
  });

  it('mostra il rifiuto del server e non perde quello che è stato digitato', async () => {
    await render();
    api.failure = new ApiError(
      'ExerciseAlreadyExists',
      409,
      'Esiste già un esercizio con questo titolo.',
    );

    clickButton('Nuovo esercizio');
    await render();
    setInput('#new-exercise-title', 'Somma');
    await render();
    clickButton('Crea esercizio');
    await render();

    expect(api.commands.length).toBe(1);
    // Il dialogo resta aperto con la bozza: l'errore si legge nella pagina, il testo non si perde.
    expect(text()).toContain('Esiste già un esercizio con questo titolo.');
    expect(input('#new-exercise-title').value).toBe('Somma');
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

  it('riparte dalla riga riletta quando annulla e riapre la modifica', async () => {
    api.exercises = [
      {
        id: 'e1',
        exerciseType: 'GridPath',
        title: 'Somma',
        description: 'Due numeri',
        isArchived: false,
        rowVersion: '4',
      },
    ];
    await render();

    clickButton('Modifica');
    await render();
    setInput('#edit-exercise-title', '');
    await render();
    clickButton('Salva esercizio');
    await render();
    expect(dialogText()).toContain('Titolo: compila il campo.');

    clickButton('Annulla');
    await render();
    expect(document.querySelector('#edit-exercise-title')).toBeNull();

    clickButton('Modifica');
    await render();
    expect(input('#edit-exercise-title').value).toBe('Somma');
    expect(input('#edit-exercise-description').value).toBe('Due numeri');
    expect(
      document.querySelector('#edit-exercise-title')?.closest('form')?.textContent,
    ).not.toContain('compila il campo');
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
  function tableText(): string {
    return page().textContent ?? '';
  }

  function page(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  /** Testo del dialogo aperto, quando c'è. */
  function dialogText(): string {
    return document.querySelector('hlm-dialog-content')?.textContent ?? '';
  }

  function rowCount(): number {
    return document.querySelectorAll('app-data-table tbody tr').length;
  }

  function input(selector: string): HTMLInputElement | HTMLTextAreaElement {
    const element = document.querySelector<HTMLInputElement | HTMLTextAreaElement>(selector);
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

  /**
   * Clic su un pulsante per etichetta. La ricerca è sull'intero documento perché i pulsanti del
   * dialogo stanno in un overlay, fuori dall'albero del componente.
   */
  function clickButton(label: string): void {
    const button = [...document.querySelectorAll('button')].find((candidate) => {
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

  /** Clic su un collegamento numerico della paginazione. */
  function clickLink(label: string): void {
    const link = [...document.querySelectorAll('a')].find(
      (candidate) => (candidate.textContent ?? '').trim() === label,
    );
    if (link === undefined) {
      throw new Error(`Collegamento non trovato: ${label}`);
    }

    link.click();
  }
});
