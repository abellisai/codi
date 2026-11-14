import { signal } from '@angular/core';
import { OverlayModule } from '@angular/cdk/overlay';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TeacherRealtime } from '@core/teacher-realtime';
import {
  ApiError,
  ApiService,
  ParticipantPreviewView,
  SessionParticipantDetail,
  SessionParticipantOverview,
  SessionStatus,
  SessionSummary,
  WorkspacePreviewReceipt,
} from '@core/api';
import { SessionDetail } from './session-detail';
import { SessionParticipants } from './session-participants';
import { SessionRegiaService } from './session-regia-service';

/** Sostituto deterministico del dispatcher: registra le richieste e restituisce quello che serve. */
class ApiStub {
  readonly queries: { name: string; payload: Record<string, unknown> }[] = [];
  readonly commands: { name: string; payload: Record<string, unknown> }[] = [];

  session: SessionSummary = summary('Active');

  participants: readonly SessionParticipantOverview[] = [];

  detail: SessionParticipantDetail | null = null;

  preview: ParticipantPreviewView | null = null;

  /** Rifiuto dei comandi, come un conflitto di stato o una revisione superata. */
  commandFailure: ApiError | null = null;

  async executeQuery<TResponse>(
    name: string,
    payload: Record<string, unknown> = {},
  ): Promise<TResponse> {
    this.queries.push({ name, payload });

    switch (name) {
      case 'ListSessionParticipants':
        return this.participants as TResponse;
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

    if (name === 'UpdateParticipant' || name === 'ExcludeParticipant') {
      this.participants = this.participants.map((participant) =>
        participant.participantId !== payload['participantId']
          ? participant
          : {
              ...participant,
              rowVersion: String(BigInt(participant.rowVersion!) + 1n),
              ...(name === 'UpdateParticipant'
                ? { displayName: String(payload['displayName']) }
                : { admissionStatus: 'Excluded' }),
            },
      );
    }

    return {
      commandId: 'c1',
      sessionId: 's1',
      sessionRevision: '8',
      deliverySummary: { pending: 1, applied: 0, failed: 0 },
    } as TResponse;
  }
}

/**
 * La pagina dei partecipanti si monta da una route vera — la sessione arriva dall'indirizzo — e si
 * prova dalle interazioni sui pulsanti delle card. Il dialogo di conferma vive in un overlay del
 * CDK, quindi le ricerche dei suoi pulsanti avvengono sul documento.
 */
describe('SessionParticipants', () => {
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
          // La rotta dei partecipanti è più specifica: precede quella della regia, che altrimenti
          // catturerebbe il prefisso con `pathMatch` di default.
          { path: 'sessions/:sessionId/participants', component: SessionParticipants },
          { path: 'sessions/:sessionId', component: SessionDetail },
        ]),
      ],
    }).compileComponents();
  });

  it('mostra una card per alunno con presenza, stato desiderato e lavoro corrente', async () => {
    api.participants = [
      participant(),
      participant({
        participantId: 'p2',
        displayName: 'Sara',
        presence: 'Disconnected',
        view: 'Waiting',
        interactionEnabled: false,
        attemptId: null,
        attemptNumber: null,
        attemptRowVersion: null,
        attemptStatus: null,
        projectRevision: null,
      }),
    ];

    await mount();

    expect(page().querySelectorAll('app-participant-card').length).toBe(2);
    expect(pageText()).toContain('Luca');
    expect(pageText()).toContain('Connesso');
    expect(pageText()).toContain('Exercise · controlli attivi');
    expect(pageText()).toContain('Tentativo 1 · Ready · progetto rev. 0');
    expect(pageText()).toContain('Sara');
    expect(pageText()).toContain('Non connesso');
    expect(pageText()).toContain('Nessun esercizio assegnato');
  });

  it('offre lo stato vuoto quando nessun alunno è entrato', async () => {
    api.participants = [];

    await mount();

    expect(page().querySelector('app-participant-card')).toBeNull();
    expect(pageText()).toContain('Nessun alunno è ancora entrato');
  });

  it('valida il nome, conserva la bozza su rifiuto e rilegge nome e versione dopo il salvataggio', async () => {
    api.participants = [participant()];
    await mount();
    actionButton('Rinomina l’alunno').click();
    await flush();
    const input = page().querySelector<HTMLInputElement>('input')!;
    input.value = ' ';
    input.dispatchEvent(new Event('input'));
    page()
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await flush();
    expect(pageText()).toContain('Inserisci un nome da 1 a 30');
    expect(api.commands).toHaveLength(0);
    input.value = 'Lucia';
    input.dispatchEvent(new Event('input'));
    api.commandFailure = new ApiError('RowVersionMismatch', 412, 'Versione superata');
    page()
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await flush();
    expect(input.value).toBe('Lucia');
    expect(pageText()).toContain('Versione superata');
    api.commandFailure = null;
    page()
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await flush();
    expect(api.commands.at(-1)).toEqual({
      name: 'UpdateParticipant',
      payload: {
        sessionId: 's1',
        participantId: 'p1',
        displayName: 'Lucia',
        stationId: null,
        baseRowVersion: '5',
      },
    });
    expect(pageText()).toContain('Lucia');
    expect(page().querySelector('form')).toBeNull();
    actionButton('Escludi l’alunno', 'Lucia').click();
    await flush();
    expect(api.commands.at(-1)?.name).toBe('UpdateParticipant');
    dialogButton('Escludi').click();
    await flush();
    expect(api.commands.at(-1)).toEqual({
      name: 'ExcludeParticipant',
      payload: { sessionId: 's1', participantId: 'p1', baseRowVersion: '6' },
    });
    expect(pageText()).toContain('escluso');
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    expect(actionButton('Rinomina l’alunno', 'Lucia').disabled).toBe(true);
    expect(actionButton('Metti in pausa l’alunno', 'Lucia').disabled).toBe(true);
  });

  it('apre il dettaglio di un alunno e ne mostra i blocchi, rileggendoli all’annuncio dell’hub', async () => {
    api.participants = [participant()];
    api.detail = detail();
    api.preview = previewView('1', true);

    await mount();

    actionButton('Dettaglio dell’alunno').click();
    await flush();

    // Il dettaglio e l'anteprima si leggono per un solo alunno, non per tutte le card (§14.3).
    expect(api.queries.filter((query) => query.name === 'GetParticipantDetail')).toEqual([
      { name: 'GetParticipantDetail', payload: { sessionId: 's1', participantId: 'p1' } },
    ]);
    expect(api.queries.filter((query) => query.name === 'GetParticipantPreview')).toEqual([
      { name: 'GetParticipantPreview', payload: { sessionId: 's1', participantId: 'p1' } },
    ]);
    expect(page().querySelector('app-participant-card .selected')).not.toBeNull();
    expect(pageText()).toContain('Percorso');
    expect(pageText()).toContain('quando si avvia');
    expect(pageText()).toContain('parziale');

    // L'hub annuncia una revisione nuova: si rilegge solo per l'alunno aperto.
    api.preview = previewView('2', false);
    previews.set([
      {
        participantId: 'p1',
        sessionId: 's1',
        attemptId: 'a1',
        projectRevision: '2',
        workspaceHash: 'h2',
        receivedAtUtc: '2026-10-01T08:11:00Z',
      },
    ]);
    await flush();

    expect(pageText()).toContain('rev. 2');
    expect(pageText()).not.toContain('parziale');
  });

  it('esegue l’azione del singolo alunno con la revisione letta e rilegge il server', async () => {
    api.participants = [participant()];

    await mount();

    const enable = actionButton('Abilita i controlli');
    expect(enable.disabled).toBe(false);
    enable.click();
    await flush();

    expect(api.commands).toEqual([
      {
        name: 'EnableActivity',
        payload: { sessionId: 's1', expectedSessionRevision: '7', participantId: 'p1' },
      },
    ]);
    expect(api.queries.map((query) => query.name)).toEqual([
      'GetSession',
      'ListSessionParticipants',
      'GetSession',
      'ListSessionParticipants',
    ]);
  });

  it('mostra un rifiuto e rilegge comunque la sessione', async () => {
    api.participants = [participant()];
    api.commandFailure = new ApiError(
      'SessionStateConflict',
      409,
      'La sessione non è nello stato richiesto.',
    );

    await mount();

    actionButton('Abilita i controlli').click();
    await flush();

    expect(api.commands.length).toBe(1);
    expect(pageText()).toContain('La sessione non è nello stato richiesto.');
    // Dopo il rifiuto la pagina rilegge sessione e partecipanti: il comando successivo parte dallo
    // stato confermato dal server, non da quello ipotizzato.
    expect(api.queries.map((query) => query.name)).toEqual([
      'GetSession',
      'ListSessionParticipants',
      'GetSession',
      'ListSessionParticipants',
    ]);
  });

  it('sulla pausa di recupero offre lo sblocco non distruttivo e mostra il blocco sulla card', async () => {
    api.participants = [participant({ recoveryBlocked: true, interactionEnabled: false })];

    await mount();

    // §12.3: dopo il reload il blocco deve vedersi sulla card, non solo nel testo dello stato.
    expect(pageText()).toContain('blocco di recupero: controlli chiusi, lavoro conservato');

    const unlock = actionButton('Sblocca il recupero (conserva il lavoro)');
    expect(unlock.disabled).toBe(false);
    // L'abilitazione dei controlli non è un'alternativa allo sblocco: sul blocco è spenta.
    expect(actionButton('Abilita i controlli').disabled).toBe(true);

    unlock.click();
    await flush();

    // Lo sblocco è la Resume per alunno: nessun tentativo dichiarato, nessun ResetWork (§10.6).
    expect(api.commands).toEqual([
      {
        name: 'Resume',
        payload: { sessionId: 's1', expectedSessionRevision: '7', participantId: 'p1' },
      },
    ]);
  });

  it('senza blocco di recupero l’azione dedicata non compare al posto della ripresa abituale', async () => {
    api.participants = [participant()];

    await mount();

    expect(pageText()).not.toContain('Sblocca il recupero');
    expect(actionButton('Riprendi l’alunno').disabled).toBe(true);
  });

  it('azzera il lavoro solo dopo la conferma, dichiarando il tentativo mostrato', async () => {
    api.participants = [participant()];

    await mount();

    actionButton('Azzera il lavoro').click();
    await flush();
    expect(dialogText()).toContain('Azzerare il lavoro di Luca?');
    expect(api.commands).toEqual([]);

    dialogButton('Azzera').click();
    await flush();

    // Il comando dichiara il tentativo della conferma e la sua versione: il server lo verifica di
    // nuovo prima di archiviarlo (§10.6).
    expect(api.commands).toEqual([
      {
        name: 'ResetWork',
        payload: {
          sessionId: 's1',
          expectedSessionRevision: '7',
          participantId: 'p1',
          attemptId: 'a1',
          attemptRowVersion: '2',
        },
      },
    ]);
  });

  it('rilegge il dettaglio aperto dopo un comando, per non mostrare un tentativo superato', async () => {
    api.participants = [participant()];
    api.detail = detail();
    await mount();

    actionButton('Dettaglio dell’alunno').click();
    await flush();
    expect(api.queries.filter((query) => query.name === 'GetParticipantDetail').length).toBe(1);

    // L'azzeramento cambia il tentativo: la griglia e il dettaglio aperto si rileggono insieme,
    // altrimenti la card direbbe «Tentativo 2» e il dettaglio resterebbe su «Tentativi 1».
    api.participants = [
      participant({ attemptId: 'a2', attemptNumber: '2', attemptRowVersion: '1' }),
    ];
    api.detail = { ...detail(), attemptCount: 2 };

    actionButton('Azzera il lavoro').click();
    await flush();
    dialogButton('Azzera').click();
    await flush();

    expect(api.queries.filter((query) => query.name === 'GetParticipantDetail').length).toBe(2);
  });

  it('condivide lo stato della sessione fra la regia e i partecipanti', async () => {
    api.participants = [participant()];
    harness = await RouterTestingHarness.create('/sessions/s1');
    await flush();

    // Lo stato appartiene al servizio, non alla pagina: la regia lo possiede dopo la lettura.
    const service = TestBed.inject(SessionRegiaService);
    expect(service.participants().length).toBe(1);

    await harness.navigateByUrl('/sessions/s1/participants');
    await flush();

    // La pagina dei partecipanti è la stessa istanza del servizio, quindi non ha una copia propria
    // che possa divergere da quella della regia (§14.1).
    expect(TestBed.inject(SessionRegiaService)).toBe(service);
    expect(pageText()).toContain('Luca');
  });

  /** Monta la pagina dei partecipanti su un indirizzo valido. */
  async function mount(): Promise<void> {
    harness = await RouterTestingHarness.create('/sessions/s1/participants');
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

  /** Pulsante di una card, riconosciuto dall'etichetta accessibile che porta anche l'alunno. */
  function actionButton(label: string, name = 'Luca'): HTMLButtonElement {
    const aria = `${label}: ${name}`;
    const button = [...page().querySelectorAll('button')].find(
      (candidate) => candidate.getAttribute('aria-label') === aria,
    );
    if (button === undefined) {
      throw new Error(`Pulsante della card non trovato: ${aria}`);
    }

    return button;
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
});

function summary(
  status: SessionStatus,
  admissionsOpen = status === 'Active',
  sessionRevision = '7',
): SessionSummary {
  return {
    id: 's1',
    status,
    admissionsOpen,
    joinCode: '12345678',
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
    rowVersion: '5',
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

/** Dettaglio dell'alunno come lo restituisce `GetParticipantDetail` (§10.5). */
function detail(): SessionParticipantDetail {
  return {
    participant: participant(),
    assignment: {
      assignmentId: 'as1',
      scope: 'Class',
      lessonExerciseId: 'b1',
      exerciseVersionId: 'v1',
      exerciseId: 'e1',
      exerciseTitle: 'Percorso',
      engineVersion: 'gridpath-1',
    },
    attempt: {
      attemptId: 'a1',
      attemptNumber: '1',
      status: 'Ready',
      workspaceJson: '{}',
      workspaceHash: 'h1',
      projectRevision: '1',
      hasCheckpoint: true,
      checkpointEngineVersion: 'gridpath-1',
      lastSavedAtUtc: '2026-10-01T08:09:00Z',
      updatedAtUtc: '2026-10-01T08:09:00Z',
      rowVersion: '2',
    },
    attemptCount: 1,
  };
}

/** Anteprima dei blocchi come la legge il docente: due blocchi e l'indicazione di parzialità. */
function previewView(projectRevision: string, partial: boolean): ParticipantPreviewView {
  return {
    participantId: 'p1',
    attemptId: 'a1',
    projectRevision,
    workspaceHash: `h${projectRevision}`,
    workspaceJson: JSON.stringify({
      blocks: { languageVersion: 0, blocks: [{ type: 'aula_start' }, { type: 'aula_move' }] },
    }),
    receivedAtUtc: '2026-10-01T08:10:00Z',
    attemptWorkspaceHash: partial ? 'altra-impronta' : `h${projectRevision}`,
    partial,
  };
}
