import { OverlayModule } from '@angular/cdk/overlay';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import {
  ApiError,
  ApiService,
  Classroom,
  Course,
  Lesson,
  School,
  SessionSummary,
  SessionStatus,
} from '@core/api';
import { SessionList } from './session-list';

/** Pagina di comodo: la creazione di una sessione naviga alla regia, che qui basta che esista. */
@Component({ selector: 'app-regia-placeholder', template: '<p>regia</p>' })
class Placeholder {}

/** Sostituto deterministico del dispatcher: registra le richieste e restituisce quello che serve. */
class ApiStub {
  readonly queries: { name: string; payload: Record<string, unknown> }[] = [];
  readonly commands: { name: string; payload: Record<string, unknown> }[] = [];

  sessions: readonly SessionSummary[] = [];
  schools: readonly School[] = [];
  classrooms: readonly Classroom[] = [];
  courses: readonly Course[] = [];
  lessons: readonly Lesson[] = [];

  /** Rifiuto della lettura, come una sessione scaduta su una pagina ricaricata. */
  loadFailure: ApiError | null = null;

  /** Rifiuto della creazione, come una classe che ha già una sessione aperta. */
  createFailure: ApiError | null = null;

  /** Rifiuto dell'eliminazione, come una sessione cambiata dopo la lettura. */
  deleteFailure: ApiError | null = null;

  async executeQuery<TResponse>(
    name: string,
    payload: Record<string, unknown> = {},
  ): Promise<TResponse> {
    this.queries.push({ name, payload });

    if (this.loadFailure !== null) {
      throw this.loadFailure;
    }

    switch (name) {
      case 'ListSessions':
        return this.sessions as TResponse;
      case 'ListSchools':
        return this.schools as TResponse;
      case 'ListClassrooms':
        return this.classrooms as TResponse;
      case 'ListCourses':
        return this.courses as TResponse;
      case 'ListLessons':
        return this.lessons as TResponse;
      default:
        return [] as unknown as TResponse;
    }
  }

  async executeCommand<TResponse>(
    name: string,
    payload: Record<string, unknown> = {},
  ): Promise<TResponse> {
    this.commands.push({ name, payload });

    if (name === 'DeleteSession' && this.deleteFailure !== null) {
      throw this.deleteFailure;
    }

    if (this.createFailure !== null) {
      throw this.createFailure;
    }

    // La regia è una pagina vera: la creazione restituisce almeno l'identificativo da aprire.
    return { id: 's1' } as TResponse;
  }
}

/**
 * La pagina si prova dalle interazioni reali: apertura del dialogo, scelta nei quattro livelli
 * della cascata e invio. I campi del dialogo vivono in un overlay del CDK, quindi le ricerche
 * avvengono sul documento e non solo sull'albero del componente.
 */
describe('SessionList', () => {
  let fixture: ComponentFixture<SessionList>;
  let api: ApiStub;

  beforeEach(async () => {
    api = new ApiStub();

    await TestBed.configureTestingModule({
      imports: [SessionList, OverlayModule],
      providers: [
        { provide: ApiService, useValue: api },
        provideRouter([{ path: 'sessions/:sessionId', component: Placeholder }]),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SessionList);
  });

  it('carica le sessioni e le presenta con stato e ingressi in italiano', async () => {
    api.sessions = [summary('Lobby', false)];

    await render();

    expect(api.queries).toEqual([{ name: 'ListSessions', payload: {} }]);
    expect(rowCount()).toBe(1);
    expect(tableText()).toContain('Lezione uno');
    expect(tableText()).toContain('Classe 1A');
    expect(tableText()).toContain('In attesa');
    expect(tableText()).toContain('Chiusi');
  });

  it('mostra lo stato vuoto quando non ci sono sessioni', async () => {
    await render();

    expect(text()).toContain('Non ci sono sessioni');
    expect(rowCount()).toBe(0);
  });

  it('collega ogni riga alla regia della sessione', async () => {
    api.sessions = [summary('Active', true)];

    await render();

    const link = page().querySelector<HTMLAnchorElement>('a[hlmBtn]');
    expect(link?.getAttribute('href')).toBe('/sessions/s1');
    expect((link?.textContent ?? '').trim()).toBe('Regia');
  });

  it('non offre di eliminare una sessione in corso', async () => {
    api.sessions = [summary('Active', true)];

    await render();

    // La chiusura è un atto distinto: una lezione in corso si conclude con Stop, non si cancella.
    expect(deleteButton()).toBeUndefined();
  });

  it('elimina la sessione conclusa dopo la conferma sulla riga e rilegge l’elenco', async () => {
    api.sessions = [summary('Ended', false)];

    await render();
    deleteButton()?.click();
    await render();

    // La conferma vive nella riga e nomina la sessione che sta per sparire: nessun dialogo in più.
    expect(tableText()).toContain('Eliminare «Lezione uno»?');
    expect(api.commands).toEqual([]);

    // Annullare non scrive nulla e riporta le azioni della riga.
    confirmButton('Annulla eliminazione')?.click();
    await render();
    expect(tableText()).not.toContain('Eliminare «Lezione uno»?');
    expect(api.commands).toEqual([]);

    // La conferma esplicita invia la revisione letta e, a esito positivo, rilegge l'elenco.
    deleteButton()?.click();
    await render();
    api.sessions = [];
    confirmButton('Conferma eliminazione')?.click();
    await render();
    await render();

    expect(api.commands).toEqual([
      { name: 'DeleteSession', payload: { sessionId: 's1', expectedSessionRevision: '7' } },
    ]);
    expect(api.queries).toEqual([
      { name: 'ListSessions', payload: {} },
      { name: 'ListSessions', payload: {} },
    ]);
    expect(rowCount()).toBe(0);
    expect(tableText()).toContain('Non ci sono sessioni');
  });

  it('mostra il rifiuto dell’eliminazione e lascia la conferma aperta', async () => {
    api.sessions = [summary('Ended', false)];
    api.deleteFailure = new ApiError(
      'SessionRevisionConflict',
      409,
      'La sessione è cambiata dopo la lettura.',
    );

    await render();
    deleteButton()?.click();
    await render();
    confirmButton('Conferma eliminazione')?.click();
    await render();

    expect(api.commands.length).toBe(1);
    // Il rifiuto si legge nella pagina, dove le altre mutazioni dell'elenco mostrano i loro errori,
    // e la conferma resta aperta: la riga non è sparita e l'elenco non si è riletto.
    expect(text()).toContain('La sessione è cambiata dopo la lettura.');
    expect(tableText()).toContain('Eliminare «Lezione uno»?');
    expect(api.queries.length).toBe(1);
    expect(rowCount()).toBe(1);
  });

  it('abilita i livelli della cascata uno alla volta', async () => {
    api.schools = [school('sc1', 'Scuola Uno')];

    await render();
    clickButton('Nuova sessione');
    await render();

    expect(api.queries).toEqual([
      { name: 'ListSessions', payload: {} },
      { name: 'ListSchools', payload: { includeArchived: false } },
    ]);
    // Solo la scuola è scelta: i livelli inferiori restano bloccati.
    expect(select('#new-session-classroom').disabled).toBe(true);
    expect(select('#new-session-course').disabled).toBe(true);
    expect(select('#new-session-lesson').disabled).toBe(true);

    api.classrooms = [classroom('cl1', 'sc1', '1A')];
    choose('#new-session-school', 'sc1');
    await render();

    expect(api.queries.at(-1)).toEqual({
      name: 'ListClassrooms',
      payload: { schoolId: 'sc1', includeArchived: false },
    });
    expect(select('#new-session-classroom').disabled).toBe(false);
    expect(select('#new-session-course').disabled).toBe(true);

    api.courses = [course('co1', 'Informatica')];
    choose('#new-session-classroom', 'cl1');
    await render();

    expect(api.queries.at(-1)).toEqual({
      name: 'ListCourses',
      payload: { includeArchived: false },
    });
    expect(select('#new-session-course').disabled).toBe(false);

    api.lessons = [lesson('le1', 'co1', 'Lezione uno')];
    choose('#new-session-course', 'co1');
    await render();

    expect(api.queries.at(-1)).toEqual({
      name: 'ListLessons',
      payload: { courseId: 'co1', includeArchived: false },
    });
    expect(select('#new-session-lesson').disabled).toBe(false);
  });

  it('ripulisce i livelli inferiori quando cambia una scelta superiore', async () => {
    api.schools = [school('sc1', 'Scuola Uno'), school('sc2', 'Scuola Due')];
    api.classrooms = [classroom('cl1', 'sc1', '1A')];
    api.courses = [course('co1', 'Informatica')];
    api.lessons = [lesson('le1', 'co1', 'Lezione uno')];

    await render();
    clickButton('Nuova sessione');
    await render();

    choose('#new-session-school', 'sc1');
    await render();
    choose('#new-session-classroom', 'cl1');
    await render();
    choose('#new-session-course', 'co1');
    await render();
    expect(select('#new-session-lesson').disabled).toBe(false);

    // Cambiando scuola, classi, corsi e lezioni si svuotano e i livelli tornano bloccati: non si
    // invia la lezione di una scuola con la classe di un'altra.
    choose('#new-session-school', 'sc2');
    await render();

    expect(select('#new-session-classroom').disabled).toBe(false);
    expect(select('#new-session-course').disabled).toBe(true);
    expect(select('#new-session-lesson').disabled).toBe(true);
    // Resta solo l'opzione segnaposto.
    expect(options('#new-session-course').length).toBe(1);
    expect(options('#new-session-lesson').length).toBe(1);
  });

  it('crea la sessione con la lezione e la classe scelte e apre la regia', async () => {
    await fillCascade();

    clickButton('Crea sessione');
    await render();
    await render();

    expect(api.commands).toEqual([
      { name: 'CreateSession', payload: { lessonId: 'le1', classroomId: 'cl1' } },
    ]);
    expect(TestBed.inject(Router).url).toBe('/sessions/s1');
  });

  it('mostra il rifiuto alla creazione e conserva la cascata scelta', async () => {
    api.createFailure = new ApiError(
      'SessionStateConflict',
      409,
      'La classe ha già una sessione aperta.',
    );

    await fillCascade();

    clickButton('Crea sessione');
    await render();

    expect(api.commands.length).toBe(1);
    expect(text()).toContain('La classe ha già una sessione aperta.');
    // Il dialogo resta aperto con la bozza: la scelta non si perde e si può correggere.
    expect(select('#new-session-school').value).toBe('sc1');
    expect(select('#new-session-lesson').value).toBe('le1');
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

  /** Percorso completo della cascata, pronto per l'invio: è il preludio di due prove. */
  async function fillCascade(): Promise<void> {
    api.schools = [school('sc1', 'Scuola Uno')];
    api.classrooms = [classroom('cl1', 'sc1', '1A')];
    api.courses = [course('co1', 'Informatica')];
    api.lessons = [lesson('le1', 'co1', 'Lezione uno')];

    await render();
    clickButton('Nuova sessione');
    await render();

    choose('#new-session-school', 'sc1');
    await render();
    choose('#new-session-classroom', 'cl1');
    await render();
    choose('#new-session-course', 'co1');
    await render();
    choose('#new-session-lesson', 'le1');
    await render();
  }

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

  function rowCount(): number {
    return document.querySelectorAll('app-data-table tbody tr').length;
  }

  function select(selector: string): HTMLSelectElement {
    const element = document.querySelector<HTMLSelectElement>(selector);
    if (element === null) {
      throw new Error(`Select non trovata: ${selector}`);
    }

    return element;
  }

  function options(selector: string): HTMLOptionElement[] {
    return [...select(selector).options];
  }

  /**
   * Sceglie un valore nel selettore nativo. L'evento parte dal `<select>` interno e risale al
   * componente ospite, dove la pagina ascolta il `change`: è lo stesso percorso del browser.
   */
  function choose(selector: string, value: string): void {
    const element = select(selector);
    element.value = value;
    element.dispatchEvent(new Event('change', { bubbles: true }));
  }

  /** Clic su un pulsante per etichetta: la ricerca è sul documento per i pulsanti del dialogo. */
  function clickButton(label: string): void {
    const button = [...document.querySelectorAll('button')].find(
      (candidate) => (candidate.textContent ?? '').trim() === label,
    );
    if (button === undefined) {
      throw new Error(`Pulsante non trovato: ${label}`);
    }

    button.click();
  }

  /** Pulsante di eliminazione della riga: icona sola, nome accessibile nell'etichetta. */
  function deleteButton(): HTMLButtonElement | undefined {
    return [...document.querySelectorAll('button')].find((candidate) =>
      (candidate.getAttribute('aria-label') ?? '').startsWith('Elimina la sessione'),
    );
  }

  /** Pulsante di conferma o di annullamento della riga, per nome accessibile. */
  function confirmButton(label: string): HTMLButtonElement | undefined {
    return [...document.querySelectorAll('button')].find((candidate) =>
      (candidate.getAttribute('aria-label') ?? '').startsWith(label),
    );
  }
});

function summary(status: SessionStatus, admissionsOpen: boolean): SessionSummary {
  return {
    id: 's1',
    status,
    admissionsOpen,
    joinCode: '12345678',
    sessionRevision: '7',
    startedAtUtc: status === 'Lobby' ? null : '2026-10-01T08:00:00Z',
    endedAtUtc: status === 'Ended' ? '2026-10-01T09:00:00Z' : null,
    lessonId: 'lz1',
    lessonTitle: 'Lezione uno',
    courseTitle: 'Informatica',
    classroomName: 'Classe 1A',
    schoolName: 'Scuola Uno',
  };
}

function school(id: string, name: string): School {
  return { id, name, isArchived: false, rowVersion: '1' };
}

function classroom(id: string, schoolId: string, name: string): Classroom {
  return { id, schoolId, name, schoolYear: '2026/2027', isArchived: false, rowVersion: '1' };
}

function course(id: string, title: string): Course {
  return { id, title, description: '', isArchived: false, rowVersion: '1' };
}

function lesson(id: string, courseId: string, title: string): Lesson {
  return { id, courseId, title, description: '', isArchived: false, rowVersion: '1' };
}
