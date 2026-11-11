import { signal } from '@angular/core';
import { TeacherRealtime } from '@core/teacher-realtime';
import { OverlayModule } from '@angular/cdk/overlay';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import {
  ApiError,
  ApiService,
  Exercise,
  LessonExercise,
  ParticipantPreviewView,
  SessionParticipantDetail,
  SessionParticipantOverview,
  SessionStatus,
  SessionSummary,
  WorkspacePreviewReceipt,
} from '@core/api';
import { SessionDetail } from './session-detail';

/** Sostituto deterministico del dispatcher: registra le richieste e restituisce quello che serve. */
class ApiStub {
  readonly queries: { name: string; payload: Record<string, unknown> }[] = [];
  readonly commands: { name: string; payload: Record<string, unknown> }[] = [];

  session: SessionSummary = summary('Lobby');

  participants: readonly SessionParticipantOverview[] = [];

  boxes: readonly LessonExercise[] = [];

  exercises: readonly Exercise[] = [];

  detail: SessionParticipantDetail | null = null;

  /** Anteprima dei blocchi dell'alunno: assente finché il browser non ne pubblica una. */
  preview: ParticipantPreviewView | null = null;

  /** Rifiuto della lettura, come un indirizzo di una sessione che non esiste più. */
  loadFailure: ApiError | null = null;

  /** Rifiuto dei comandi, come un conflitto di stato o una revisione superata. */
  commandFailure: ApiError | null = null;

  async executeQuery<TResponse>(
    name: string,
    payload: Record<string, unknown> = {},
  ): Promise<TResponse> {
    this.queries.push({ name, payload });

    if (this.loadFailure !== null) {
      throw this.loadFailure;
    }

    switch (name) {
      case 'ListSessionParticipants':
        return this.participants as TResponse;
      case 'ListLessonExercises':
        return this.boxes as TResponse;
      case 'ListExercises':
        return this.exercises as TResponse;
      case 'GetParticipantDetail':
        return this.detail as TResponse;
      case 'GetParticipantPreview':
        return this.preview as TResponse;
      default:
        return this.session as TResponse;
    }
  }

  async executeCommand<TResponse>(
    name: string,
    payload: Record<string, unknown> = {},
  ): Promise<TResponse> {
    this.commands.push({ name, payload });

    if (this.commandFailure !== null) {
      throw this.commandFailure;
    }

    // L'esito di un comando di regia: il comando registrato con il riepilogo delle consegne.
    return {
      commandId: 'c1',
      sessionId: 's1',
      sessionRevision: '8',
      deliverySummary: { pending: 2, applied: 0, failed: 0 },
    } as TResponse;
  }
}

/**
 * La regia si monta da una route vera — la sessione arriva dall'indirizzo — e si prova dalle
 * interazioni reali sui pulsanti. Il dialogo di conferma vive in un overlay del CDK, quindi le
 * ricerche dei suoi pulsanti avvengono sul documento.
 */
describe('SessionDetail', () => {
  let harness!: RouterTestingHarness;
  let api: ApiStub;

  /** Anteprime annunciate dall'hub: le prove ne dichiarano l'arrivo. */
  const previews = signal<readonly WorkspacePreviewReceipt[]>([]);

  beforeEach(async () => {
    api = new ApiStub();
    previews.set([]);

    await TestBed.configureTestingModule({
      imports: [OverlayModule],
      providers: [
        {
          provide: TeacherRealtime,
          useValue: {
            online: signal(true),
            snapshot: signal(null),
            previews,
            observe: () => Promise.resolve(),
            stop: () => Promise.resolve(),
          },
        },
        { provide: ApiService, useValue: api },
        provideRouter([
          { path: 'sessions/:sessionId', component: SessionDetail },
          { path: 'sessions', component: SessionDetail },
        ]),
      ],
    }).compileComponents();
  });

  it('legge la sessione dall’indirizzo e ne mostra contesto, stato e ingressi', async () => {
    await mount('Active');

    expect(api.queries).toEqual([
      { name: 'GetSession', payload: { sessionId: 's1' } },
      { name: 'ListSessionParticipants', payload: { sessionId: 's1' } },
    ]);
    expect(pageText()).toContain('Lezione uno');
    expect(pageText()).toContain('Informatica');
    expect(pageText()).toContain('Classe 1A');
    expect(pageText()).toContain('Scuola Uno');
    expect(pageText()).toContain('In corso');
    expect(pageText()).toContain('Aperti');
    // La revisione resta un token del contratto: la pagina la invia nei comandi, non la mostra.
    expect(pageText()).not.toContain('Revisione');
  });

  it.each([
    ['Lobby', true, false, false, true],
    ['Active', false, true, false, true],
    ['Paused', false, false, true, true],
    ['Ended', false, false, false, false],
  ])('abilita i comandi secondo lo stato %s', async (status, start, pause, resume, end) => {
    await mount(status as SessionStatus);

    expect(commandButton('Avvia').disabled).toBe(!start);
    expect(commandButton('Pausa').disabled).toBe(!pause);
    expect(commandButton('Riprendi').disabled).toBe(!resume);
    expect(commandButton('Stop').disabled).toBe(!end);
  });

  it('mostra il codice di ingresso solo mentre la lezione è in play', async () => {
    // In attesa non c'è nulla da dettare: il codice nasce con l'avvio.
    await mount('Lobby');
    expect(pageText()).not.toContain('Codice da dettare agli alunni');

    // All'avvio compare, con il pulsante di copia: il docente lo legge e lo detta alla classe.
    api.session = summary('Active', true, '8', '24681357');
    commandButton('Avvia').click();
    await flush();

    expect(pageText()).toContain('Codice da dettare agli alunni');
    expect(pageText()).toContain('24681357');
    expect(pageText()).toContain('Copia il codice');
  });

  it('conserva il codice in pausa: la pausa non invalida ciò che è stato detto a voce', async () => {
    await mount('Paused');

    expect(pageText()).toContain('Codice da dettare agli alunni');
    expect(pageText()).toContain('12345678');
  });

  it('non mostra il codice su una sessione conclusa', async () => {
    // La riga conserva il codice, ma la lezione non è più raggiungibile: non c'è nulla da dettare.
    await mount('Ended');

    expect(pageText()).not.toContain('Codice da dettare agli alunni');
    expect(pageText()).not.toContain('12345678');
  });

  it('avvia la sessione con la revisione letta e rilegge il server prima di riabilitare', async () => {
    await mount('Lobby');
    // La rilettura dopo il comando restituisce lo stato aggiornato: la sessione è ora in corso.
    api.session = summary('Active', true, '8');

    commandButton('Avvia').click();
    await flush();

    expect(api.commands).toEqual([
      {
        name: 'StartSession',
        payload: { sessionId: 's1', expectedSessionRevision: '7' },
      },
    ]);
    expect(api.queries.map((query) => query.name)).toEqual([
      'GetSession',
      'ListSessionParticipants',
      'GetSession',
      'ListSessionParticipants',
    ]);
    expect(pageText()).toContain('In corso');
    expect(commandButton('Avvia').disabled).toBe(true);
    expect(commandButton('Pausa').disabled).toBe(false);
  });

  it('mette in pausa e riprende con la revisione dello stato letto', async () => {
    await mount('Active');

    commandButton('Pausa').click();
    // La rilettura dopo il comando restituisce lo stato aggiornato: la sessione è ora in pausa,
    // quindi «Riprendi» diventa disponibile e il suo clic invia il comando.
    api.session = summary('Paused');
    await flush();
    expect(api.commands.at(-1)).toEqual({
      name: 'Pause',
      payload: { sessionId: 's1', expectedSessionRevision: '7' },
    });

    commandButton('Riprendi').click();
    await flush();
    expect(api.commands.at(-1)).toEqual({
      name: 'Resume',
      payload: { sessionId: 's1', expectedSessionRevision: '7' },
    });
  });

  it('conclude la sessione solo dopo la conferma del dialogo', async () => {
    api.participants = [participant()];
    await mount('Active');

    commandButton('Stop').click();
    await flush();
    expect(dialogText()).toContain('Concludere la sessione?');
    // La conferma non è ancora arrivata: nessun comando è stato inviato.
    expect(api.commands).toEqual([]);

    api.session = summary('Ended', false, '9');
    api.participants = [participant({ teacherPaused: true, recoveryBlocked: true })];
    clickButton('Concludi');
    await flush();

    expect(api.commands).toEqual([
      { name: 'EndSession', payload: { sessionId: 's1', expectedSessionRevision: '7' } },
    ]);
    expect(document.querySelector('hlm-dialog-content')).toBeNull();
    expect(commandButton('Stop').disabled).toBe(true);
    // Conclusa la sessione, i comandi di classe non sono più offerti: il server non li ammette.
    expect(commandButton('Abilita la classe').disabled).toBe(true);
  });

  it('mostra un rifiuto e rilegge la sessione, come un conflitto di stato', async () => {
    await mount('Lobby');
    api.commandFailure = new ApiError(
      'SessionStateConflict',
      409,
      'La sessione non è nello stato richiesto.',
    );

    commandButton('Avvia').click();
    await flush();

    expect(api.commands.length).toBe(1);
    expect(pageText()).toContain('La sessione non è nello stato richiesto.');
    // Dopo il rifiuto la pagina rilegge comunque la sessione: il comando successivo parte dallo
    // stato confermato dal server, non da quello ipotizzato.
    expect(api.queries.map((query) => query.name)).toEqual([
      'GetSession',
      'ListSessionParticipants',
      'GetSession',
      'ListSessionParticipants',
    ]);
  });

  it('riepiloga gli alunni e porta alla loro pagina, senza mostrarli nella regia', async () => {
    api.participants = [participant(), participant({ participantId: 'p2', displayName: 'Sara' })];

    await mount('Active');

    // La regia comanda la classe: gli alunni non sono una griglia qui, hanno una pagina con le
    // card che dà il colpo d'occhio sulla classe (§14.1, §14.3).
    expect(page().querySelector('app-participant-card')).toBeNull();
    expect(pageText()).toContain('2 alunni entrati.');
    expect(pageText()).not.toContain('Luca');
    expect(participantsLink()?.getAttribute('href')).toBe('/sessions/s1/participants');
  });

  it('dice che nessun alunno è entrato quando la sessione non ha partecipanti', async () => {
    api.participants = [];

    await mount('Active');

    expect(pageText()).toContain('Nessun alunno è ancora entrato');
    expect(participantsLink()?.getAttribute('href')).toBe('/sessions/s1/participants');
  });

  it('abilita la classe con la revisione letta e dichiara quante consegne sono in coda', async () => {
    api.participants = [participant()];

    await mount('Active');
    api.session = summary('Active', true, '8');

    commandButton('Abilita la classe').click();
    await flush();

    expect(api.commands).toEqual([
      { name: 'EnableActivity', payload: { sessionId: 's1', expectedSessionRevision: '7' } },
    ]);
    // «Accettato» non è «applicato»: la pagina dichiara le consegne messe in coda.
    expect(pageText()).toContain('EnableActivity: 2 consegne registrate.');
  });

  it('assegna la casella scelta dalla sequenza della lezione', async () => {
    api.boxes = [
      { id: 'b1', lessonId: 'lz1', exerciseId: 'e1', position: 1, rowVersion: '1' },
      { id: 'b2', lessonId: 'lz1', exerciseId: 'e2', position: 2, rowVersion: '1' },
    ];
    api.exercises = [
      { id: 'e1', title: 'Disegno', description: '', isArchived: false } as Exercise,
      { id: 'e2', title: 'Percorso', description: '', isArchived: false } as Exercise,
    ];
    api.participants = [participant()];

    await mount('Active');

    commandButton('Assegna esercizio').click();
    await flush();
    expect(dialogText()).toContain('2. Percorso');

    dialogButton('Assegna').click();
    await flush();

    expect(api.commands).toEqual([
      {
        name: 'AssignExercise',
        payload: {
          sessionId: 's1',
          expectedSessionRevision: '7',
          lessonExerciseId: 'b1',
        },
      },
    ]);
  });

  it('mostra l’errore e offre il ritorno all’elenco quando la sessione non è leggibile', async () => {
    api.loadFailure = new ApiError('ResourceNotFound', 404, 'Risorsa non trovata.');

    await mount('Lobby');

    expect(pageText()).toContain('Risorsa non trovata.');
    expect(returnLink()?.getAttribute('href')).toBe('/sessions');
  });

  it('dice che l’indirizzo è incompleto quando manca l’identificativo', async () => {
    harness = await RouterTestingHarness.create('/sessions');
    await flush();

    expect(pageText()).toContain('manca l’identificativo');
    expect(returnLink()?.getAttribute('href')).toBe('/sessions');
  });

  /** Monta la regia su un indirizzo valido, con lo stato indicato. */
  async function mount(status: SessionStatus): Promise<void> {
    api.session = summary(status);
    harness = await RouterTestingHarness.create('/sessions/s1');
    await flush();
  }

  /**
   * Aggiorna la vista e attende che la catena di promesse avviata dall'interazione sia conclusa.
   * `whenStable` da solo non basta: con la change detection senza `zone.js` la promessa di un
   * gestore non è un'attività registrata, quindi il test deve cedere il turno all'event loop.
   */
  async function flush(): Promise<void> {
    harness.detectChanges();
    await harness.fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    harness.detectChanges();
  }

  function page(): HTMLElement {
    const element = harness.routeNativeElement;
    if (element === null) {
      throw new Error('La pagina della route non è montata.');
    }

    return element;
  }

  /** Testo della sola pagina, senza il contenuto dei dialoghi. */
  function pageText(): string {
    return page().textContent ?? '';
  }

  function dialogText(): string {
    return document.querySelector('hlm-dialog-content')?.textContent ?? '';
  }

  /** Pulsante della barra comandi, riconosciuto dal testo (icona a parte). */
  function commandButton(label: string): HTMLButtonElement {
    const button = [...page().querySelectorAll('button')].find(
      (candidate) => (candidate.textContent ?? '').trim() === label,
    );
    if (button === undefined) {
      throw new Error(`Pulsante non trovato: ${label}`);
    }

    return button;
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

  /** Collegamento alla pagina dei partecipanti: la regia li riepiloga, non li mostra. */
  function participantsLink(): HTMLAnchorElement | null {
    return page().querySelector<HTMLAnchorElement>('a[href="/sessions/s1/participants"]');
  }

  /** Clic su un pulsante del dialogo aperto: il contenuto vive in un portale del CDK. */
  function dialogButton(label: string): HTMLButtonElement {
    const content = document.querySelector('hlm-dialog-content');
    const button = [...(content?.querySelectorAll('button') ?? [])].find(
      (candidate) => (candidate.textContent ?? '').trim() === label,
    );
    if (content === null || button === undefined) {
      throw new Error(`Pulsante del dialogo non trovato: ${label}`);
    }

    return button;
  }

  /** Collegamento di ritorno all'elenco, nell'errore o nel percorso. */
  function returnLink(): HTMLAnchorElement | null {
    return page().querySelector<HTMLAnchorElement>('a[href="/sessions"]');
  }
});

function summary(
  status: SessionStatus,
  admissionsOpen = status === 'Active',
  sessionRevision = '7',
  joinCode: string | null = '12345678',
): SessionSummary {
  return {
    id: 's1',
    status,
    admissionsOpen,
    joinCode,
    sessionRevision,
    startedAtUtc: status === 'Lobby' ? null : '2026-10-01T08:00:00Z',
    endedAtUtc: status === 'Ended' ? '2026-10-01T09:00:00Z' : null,
    lessonId: 'lz1',
    lessonTitle: 'Lezione uno',
    courseTitle: 'Informatica',
    classroomName: 'Classe 1A',
    schoolName: 'Scuola Uno',
  };
}

/** Riga dei partecipanti: un alunno connesso, con l'attività abilitata e il primo tentativo pronto. */
function participant(
  overrides: Partial<SessionParticipantOverview> = {},
): SessionParticipantOverview {
  return {
    participantId: 'p1',
    displayName: 'Luca',
    stationId: null,
    admissionStatus: 'Admitted',
    connectionEpoch: '3',
    joinedAtUtc: '2026-10-01T08:05:00Z',
    excludedAtUtc: null,
    presence: 'Fresh',
    lastHeartbeatUtc: '2026-10-01T08:10:00Z',
    view: 'Exercise',
    interactionEnabled: true,
    teacherPaused: false,
    recoveryBlocked: false,
    executionGeneration: '0',
    hasDirective: false,
    attemptId: 'a1',
    attemptNumber: '1',
    attemptRowVersion: '2',
    attemptStatus: 'Ready',
    projectRevision: '0',
    lastSavedAtUtc: null,
    unreadMessages: 0,
    ...overrides,
  };
}
