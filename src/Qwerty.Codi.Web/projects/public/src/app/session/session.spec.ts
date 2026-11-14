import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, input, output, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import {
  ApiError,
  ApiService,
  AssignedExercise,
  Attempt,
  DesiredState,
  ParticipantContext,
  ParticipantMessage,
  ParticipantSnapshot,
} from '@codi/api';
import {
  ExerciseRuntime,
  PLAYER_WORKER_FACTORY,
  type PlayerWorker,
} from '../core/exercise-runtime';
import type { PlayerEvent, PlayerRequest } from '@exercise-engine/player-protocol';
import { ParticipantRealtime, RealtimeStatus } from '../core/participant-realtime';
import { ParticipantChat } from '../core/participant-chat';
import { ParticipantService } from '../core/participant-service';
import { ParticipantSession } from './session';
import { ParticipantWorkspace } from './participant-workspace';

/**
 * Definizione valida del motore: serve a disegnare la griglia e ad alimentare la prova locale.
 * Una definizione incompleta non è un caso di prova utile qui: il comportamento di una definizione
 * scritta male si prova nel motore, non nella pagina.
 */
const definitionJson = JSON.stringify({
  schemaVersion: 1,
  exerciseType: 'GridPath',
  engineVersion: 'gridpath-1',
  title: 'Disegno con i blocchi',
  instructions: 'Porta il personaggio alla stella.',
  scene: {
    width: 5,
    height: 5,
    start: { x: 0, y: 0, direction: 'East' },
    obstacles: [],
    goal: { x: 4, y: 0 },
  },
  toolbox: { allowedBlocks: ['aula_start', 'aula_move', 'aula_turn', 'aula_repeat'] },
  limits: {
    maxBlocks: 30,
    maxNestingDepth: 8,
    maxExecutionSteps: 1000,
    maxActiveDurationMs: 120000,
  },
  completion: { type: 'ReachCell', x: 4, y: 0 },
  feedback: { success: 'Bravo!', collision: 'Attento!' },
});

/** Progetto dell'alunno: quattro passi verso il traguardo, uno per volta. */
const workspaceJson = JSON.stringify({
  blocks: {
    languageVersion: 0,
    blocks: [
      {
        type: 'aula_start',
        id: 'start',
        next: {
          block: {
            type: 'aula_repeat',
            id: 'repeat-1',
            fields: { COUNT: 4 },
            inputs: { DO: { block: { type: 'aula_move', id: 'move-1' } } },
          },
        },
      },
    ],
  },
});

/** Definizione di un esercizio che conserva valori: serve a provare il pannello, non il motore. */
const valueProgramDefinitionJson = JSON.stringify({
  schemaVersion: 1,
  exerciseType: 'ValueProgram',
  engineVersion: 'valueprogram-1',
  title: 'Somma uno',
  instructions: 'Leggi un numero, somma uno e mostra il risultato.',
  variables: [
    { name: 'Numero', initialValue: 0 },
    { name: 'Punti', initialValue: 0 },
  ],
  testCases: [{ inputs: [6], expectedOutput: ['7'] }],
  toolbox: {
    allowedBlocks: [
      'val_start',
      'val_set',
      'val_show',
      'val_ask',
      'val_answer',
      'val_add',
      'val_number',
      'val_variable',
    ],
  },
  limits: {
    maxBlocks: 40,
    maxNestingDepth: 8,
    maxExecutionSteps: 1000,
    maxActiveDurationMs: 120000,
  },
  feedback: { success: 'Bravo!', collision: 'Riprova.' },
});

const emptyValueProgramWorkspace = JSON.stringify({ blocks: { languageVersion: 0, blocks: [] } });

/** Definizione di un esercizio dichiarativo: legge, risponde, riceve un esito. */
const quizDefinitionJson = JSON.stringify({
  schemaVersion: 1,
  exerciseType: 'Quiz',
  engineVersion: 'quiz-1',
  title: 'Quanto fa 2 + 2?',
  instructions: 'Leggi la domanda e scrivi il risultato.',
  quiz: { stimulus: '2 + 2 = ?', prompt: 'Scrivi il risultato', match: 'Integer' },
  toolbox: { allowedBlocks: [] },
  limits: {
    maxBlocks: 10,
    maxNestingDepth: 2,
    maxExecutionSteps: 10,
    maxActiveDurationMs: 60000,
  },
  feedback: { success: 'Esatto!', collision: 'Riprova.' },
});

const emptyQuizAnswer = JSON.stringify({ answer: '' });
@Component({ selector: 'app-participant-workspace', template: '' })
class WorkspaceStub {
  readonly workspaceJson = input.required<string>();
  readonly allowedBlocks = input.required<readonly string[]>();
  readonly engineVersion = input.required<string>();
  readonly readOnly = input(true);
  readonly workspaceChanged = output<string>();
}

/** Doppio del worker del player: registra i messaggi inviati e non risponde. */
class PlayerWorkerStub implements PlayerWorker {
  onmessage: ((event: MessageEvent<PlayerEvent>) => void) | null = null;
  readonly sent: PlayerRequest[] = [];
  postMessage(message: PlayerRequest): void {
    this.sent.push(message);
  }
  terminate(): void {
    /* Nessuna risorsa da smontare in un doppio. */
  }
}

/**
 * Sostituto del collegamento realtime: le prove dichiarano lo stato come se arrivasse dall'hub,
 * così la pagina si prova sui suoi confini — eventi in ingresso, invocazioni in uscita — senza una
 * connessione vera. Il comportamento del collegamento si prova dove vive, non qui.
 */
class RealtimeStub {
  runtime!: ExerciseRuntime;
  applier: ((state: DesiredState) => Promise<void>) | null = null;
  setStateApplier(callback: (state: DesiredState) => Promise<void>): void {
    this.applier = callback;
  }
  workspaceHash: string | null = null;
  setWorkspaceHash(hash: string | null): void {
    this.workspaceHash = hash;
  }
  readonly authorized = signal(false);
  currentSnapshot(): ParticipantSnapshot | null {
    return null;
  }
  setPersistedRevision(): void {
    /* La ricevuta ha test nel servizio di salvataggio. */
  }
  /** Qui il contesto dell'anteprima non esiste: il comportamento si prova nel pubblicatore. */
  previewContext(): { connectionEpoch: string; executionGeneration: string } | null {
    return null;
  }
  sendWorkspacePreview(): Promise<boolean> {
    return Promise.resolve(false);
  }
  readonly status = signal<RealtimeStatus>('connected');
  readonly desiredState = signal<DesiredState | null>(null);
  readonly leaseValidUntilUtc = signal<string | null>('2026-10-02T08:00:10Z');
  readonly notice = signal('');
  readonly lastCommand = signal<{ commandName: string; outcome: string } | null>(null);

  started = false;
  stopped = false;

  async start(): Promise<void> {
    this.started = true;
  }

  async stop(): Promise<void> {
    this.stopped = true;
  }
}

/** Pagina di comodo per l'approdo dopo l'uscita. */
@Component({ selector: 'app-join-again', template: '<p>ingresso</p>' })
class JoinAgain {}

/**
 * Sostituto della chat: la pagina la monta e le passa il turno, ma il thread, l'invio e la regola di
 * scrittura si provano dove vivono, cioè in `ParticipantChat` e in `ChatPanel`.
 */
class ChatStub {
  readonly messages = signal<readonly ParticipantMessage[]>([]);
  readonly problem = signal('');
  readonly busy = signal(false);
  readonly canWrite = signal(true);

  send(): Promise<ParticipantMessage | null> {
    return Promise.resolve(null);
  }
}

/** Sostituto del trasporto pubblico: il contesto arriva dalla credenziale, o manca. */
class ParticipantStub {
  contextFailure: ApiError | null = null;

  left = false;

  contextResult: ParticipantContext = {
    participantId: 'p1',
    sessionId: 'sess1',
    displayName: 'Luca',
    admissionStatus: 'Admitted',
    view: 'Exercise',
    sessionStatus: 'Active',
    schoolName: 'Scuola Manzoni',
    classroomName: 'Terza A',
    lessonTitle: 'Prima lezione',
  };

  async context(): Promise<ParticipantContext> {
    if (this.contextFailure !== null) {
      throw this.contextFailure;
    }

    return this.contextResult;
  }

  async leave(): Promise<void> {
    this.left = true;
  }

  bootstrap(): Promise<void> {
    throw new Error('bootstrap non richiesto dalla pagina della lezione');
  }

  schools(): Promise<never> {
    throw new Error('scuole non richieste dalla pagina della lezione');
  }

  classrooms(): Promise<never> {
    throw new Error('classi non richieste dalla pagina della lezione');
  }

  playSession(): Promise<never> {
    throw new Error('sessione non richiesta dalla pagina della lezione');
  }

  join(): Promise<never> {
    throw new Error('ingresso non richiesto dalla pagina della lezione');
  }
}

/** Sostituto del dispatcher: registra le letture e restituisce lo stato dichiarato dal caso. */
class ApiStub {
  readonly queries: string[] = [];

  snapshot: ParticipantSnapshot = snapshot('Exercise', true);

  exercise: AssignedExercise = {
    sessionId: 'sess1',
    assignmentId: 'as1',
    exerciseVersionId: 'v1',
    lessonExerciseId: 'b1',
    scope: 'Class',
    attemptId: 'a1',
    attemptNumber: '1',
    exerciseId: 'e1',
    exerciseTitle: 'Disegno con i blocchi',
    exerciseType: 'GridPath',
    definitionJson,
    engineVersion: 'gridpath-1',
    schemaVersion: '1',
    contentHash: 'impronta',
  };

  attempt: Attempt = {
    attemptId: 'a1',
    assignmentId: 'as1',
    exerciseVersionId: 'v1',
    attemptNumber: '1',
    status: 'Ready',
    workspaceJson: workspaceJson,
    workspaceHash: 'impronta-progetto',
    projectRevision: '0',
    checkpointJson: null,
    checkpointEngineVersion: null,
    lastSavedAtUtc: null,
    updatedAtUtc: '2026-10-02T08:00:00Z',
  };

  async executeQuery<TResponse>(name: string): Promise<TResponse> {
    this.queries.push(name);

    switch (name) {
      case 'GetAssignedExercise':
        return this.exercise as TResponse;
      case 'GetAttempt':
        return this.attempt as TResponse;
      default:
        return this.snapshot as TResponse;
    }
  }
}

describe('Pagina dell’alunno', () => {
  let harness!: RouterTestingHarness;
  let participants: ParticipantStub;
  let api: ApiStub;
  let realtime: RealtimeStub;
  let worker: PlayerWorkerStub;

  beforeEach(async () => {
    participants = new ParticipantStub();
    api = new ApiStub();
    realtime = new RealtimeStub();
    worker = new PlayerWorkerStub();

    TestBed.overrideComponent(ParticipantSession, {
      remove: { imports: [ParticipantWorkspace] },
      add: { imports: [WorkspaceStub] },
    });
    await TestBed.configureTestingModule({
      providers: [
        { provide: ParticipantService, useValue: participants },
        { provide: ParticipantRealtime, useValue: realtime },
        { provide: ParticipantChat, useValue: new ChatStub() },
        { provide: ApiService, useValue: api },
        { provide: PLAYER_WORKER_FACTORY, useValue: () => worker },
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'lezione', component: ParticipantSession },
          { path: 'join', component: JoinAgain },
        ]),
      ],
    }).compileComponents();
    realtime.runtime = TestBed.inject(ExerciseRuntime);
  });

  it('mostra il nome e che cosa l’alunno può fare adesso', async () => {
    await mount();

    expect(pageText()).toContain('Luca');
    expect(pageText()).toContain('Controlli: attivi');
    expect(pageText()).toContain('Pausa dell’insegnante: no');
    expect(api.queries).toEqual(['GetParticipantSnapshot', 'GetAssignedExercise', 'GetAttempt']);
  });

  it('mostra il percorso scuola, classe, lezione ed esercizio, con il nome sulla riga sotto', async () => {
    await mount();

    const levels = [...page().querySelectorAll('.breadcrumb li')].map((item) =>
      item.textContent?.trim(),
    );
    expect(levels).toEqual(['Scuola Manzoni', 'Terza A', 'Prima lezione', 'Disegno con i blocchi']);

    // Il nome è suo, non un livello del percorso: sta sotto, e il percorso lo precede.
    const name = page().querySelector('h1');
    expect(name?.textContent?.trim()).toBe('Luca');
    expect(
      page().querySelector('.breadcrumb')!.compareDocumentPosition(name!) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });

  it('in attesa il percorso si ferma alla lezione: l’esercizio non c’è ancora', async () => {
    api.snapshot = snapshot('Waiting', false);

    await mount();

    expect(
      [...page().querySelectorAll('.breadcrumb li')].map((item) => item.textContent?.trim()),
    ).toEqual(['Scuola Manzoni', 'Terza A', 'Prima lezione']);
  });

  it('un livello senza nome si salta, invece di lasciare un separatore vuoto', async () => {
    participants.contextResult = { ...participants.contextResult, schoolName: '' };
    api.snapshot = snapshot('Waiting', false);

    await mount();

    expect(
      [...page().querySelectorAll('.breadcrumb li')].map((item) => item.textContent?.trim()),
    ).toEqual(['Terza A', 'Prima lezione']);
  });

  it('mostra la consegna e il progetto quando la vista è quella dell’esercizio', async () => {
    await mount();

    expect(pageText()).toContain('Disegno con i blocchi');
    expect(pageText()).toContain('Porta il personaggio alla stella.');
    expect(pageText()).toContain('aula_start');
    expect(pageText()).toContain('Lavoro salvato');
  });
  it('mostra la conclusione live e rimuove il progetto dopo Goodbye', async () => {
    await mount();
    realtime.desiredState.set({
      ...snapshot('Exercise', true).desiredState,
      view: 'Goodbye',
      interactionEnabled: false,
    });
    await flush();
    expect(pageText()).toContain('La lezione è conclusa. Puoi uscire.');
    expect(pageText()).toContain('Lavoro: concluso');
    expect(pageText()).not.toContain('Il mio progetto');
    expect(pageText()).toContain('Vista: Goodbye');
  });

  it('in attesa non chiede il lavoro: non c’è un esercizio da mostrare', async () => {
    api.snapshot = snapshot('Waiting', false);

    await mount();

    expect(pageText()).toContain('Aspetta');
    expect(api.queries).toEqual(['GetParticipantSnapshot']);
  });

  it('condivide il caricamento concorrente e attende il progetto prima della conferma', async () => {
    api.snapshot = snapshot('Waiting', false);
    await mount();
    let release!: () => void;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const query = api.executeQuery.bind(api);
    api.executeQuery = async <TResponse>(name: string): Promise<TResponse> => {
      await gate;
      return query<TResponse>(name);
    };
    const desired = snapshot('Exercise', true).desiredState;
    const first = realtime.applier!(desired);
    const second = realtime.applier!(desired);
    expect(second).toBe(first);
    let confirmed = false;
    void second.then(() => {
      confirmed = true;
    });
    await Promise.resolve();
    expect(confirmed).toBe(false);
    release();
    await second;
    await flush();
    expect(api.queries.filter((name) => name === 'GetAttempt')).toHaveLength(1);
    expect(realtime.workspaceHash).toBe('impronta-progetto');
  });

  it('dichiara l’istruzione ricevuta senza dedurne l’esecuzione', async () => {
    api.snapshot = snapshot('Exercise', true, {
      directiveId: 'd1',
      kind: 'Run',
      executionGeneration: null,
      issuedAtUtc: '2026-10-02T08:05:00Z',
    });

    await mount();

    expect(pageText()).toContain('Istruzione ricevuta: Run');
  });

  it('invita a entrare di nuovo quando la credenziale non è più valida', async () => {
    participants.contextFailure = new ApiError('AuthenticationRequired', 401, 'Serve un accesso.');

    await mount();

    expect(pageText()).toContain('Non sei più dentro la lezione');
    expect(api.queries).toEqual([]);
  });

  it('esce revocando la credenziale e torna all’ingresso', async () => {
    await mount();

    button('Esci').click();
    await flush();

    expect(participants.left).toBe(true);
    expect(TestBed.inject(Router).url).toBe('/join');
  });

  it('apre il collegamento realtime e mostra lo stato dell’operatività', async () => {
    await mount();

    expect(realtime.started).toBe(true);
    expect(pageText()).toContain('Collegamento: collegato');
    expect(pageText()).toContain('autorizzazione fino alle 2026-10-02T08:00:10Z');
  });

  it('un comando consegnato cambia i permessi e fa rileggere il lavoro', async () => {
    api.snapshot = snapshot('Waiting', false);
    await mount();
    expect(api.queries).toEqual(['GetParticipantSnapshot']);

    // Il docente assegna e abilita: il comando arriva dal collegamento, non da una ricarica.
    realtime.desiredState.set(desiredState('Exercise', true, 'a1'));
    realtime.lastCommand.set({ commandName: 'EnableActivity', outcome: 'Applied' });
    await flush();

    expect(pageText()).toContain('Controlli: attivi');
    expect(pageText()).toContain('Ultimo comando ricevuto: EnableActivity (Applied)');
    expect(api.queries).toContain('GetAssignedExercise');
    expect(pageText()).toContain('Disegno con i blocchi');
  });

  it('torna all’attesa togliendo il lavoro quando il docente mostra l’attesa', async () => {
    await mount();
    expect(pageText()).toContain('Disegno con i blocchi');

    realtime.desiredState.set(desiredState('Waiting', false, null));
    await flush();

    expect(pageText()).not.toContain('Disegno con i blocchi');
    expect(pageText()).toContain('Aspetta');
  });

  it('dichiara quando un’altra pagina ha preso il posto', async () => {
    await mount();

    realtime.status.set('superseded');
    realtime.notice.set('Un’altra scheda ha preso il posto di questa.');
    await flush();

    expect(pageText()).toContain('Collegamento: sostituito da un’altra pagina');
    expect(pageText()).toContain('Un’altra scheda ha preso il posto di questa.');
  });

  it('chiude il collegamento quando l’alunno esce', async () => {
    await mount();

    button('Esci').click();
    await flush();

    expect(realtime.stopped).toBe(true);
  });

  it('mostra lo stato con riquadri a icona, non solo con parole', async () => {
    await mount();

    const badges = [...page().querySelectorAll('app-status-badge')];
    expect(badges.map((badge) => badge.textContent?.trim())).toEqual([
      'Collegato alla lezione',
      'Puoi lavorare!',
      'Programma pronto',
    ]);
    // Colore e icona accompagnano sempre il testo: la classe del tono e il disegno sono verificabili.
    expect(badges.map((badge) => badge.querySelector('.badge')?.className)).toEqual([
      'badge badge-ok',
      'badge badge-ok',
      'badge badge-idle',
    ]);
    expect(page().querySelectorAll('app-status-badge svg').length).toBe(3);
  });

  it('dice di aspettare quando i controlli sono chiusi e quando il maestro è in pausa', async () => {
    api.snapshot = snapshot('Exercise', false);
    await mount();

    expect(pageText()).toContain('Controlli chiusi: aspetta il maestro');
    expect(page().querySelector('.badge-wait')).not.toBeNull();

    realtime.desiredState.set({ ...desiredState('Exercise', true, 'a1'), teacherPaused: true });
    await flush();

    expect(pageText()).toContain('In pausa: guarda il maestro');
    expect(page().querySelector('.badge-stop')?.textContent).toContain('In pausa');
  });

  it('un esercizio di un motore che questa build non conosce viene spiegato, non disegnato a vuoto', async () => {
    // Il server distribuisce la versione fissata dall'assegnazione: un client non aggiornato non
    // deve interpretare quella definizione come se fosse una griglia, né restare muto.
    api.snapshot = snapshot('Exercise', true);
    api.exercise = { ...api.exercise, engineVersion: 'gridpath-9' };
    realtime.authorized.set(true);

    await mount();

    expect(pageText()).toContain('Questo esercizio non si apre con questa versione del programma');
    expect(button('Prova').disabled).toBe(true);
    expect(page().querySelector('.board')).toBeNull();
  });

  it('la prova è disponibile solo quando il maestro lascia lavorare', async () => {
    api.snapshot = snapshot('Exercise', false);
    await mount();
    expect(button('Prova').disabled).toBe(true);

    realtime.authorized.set(true);
    realtime.desiredState.set(desiredState('Exercise', true, 'a1'));
    await flush();

    expect(button('Prova').disabled).toBe(false);
    expect(button('Un passo').disabled).toBe(false);
  });

  it('la prova muove il personaggio sulla griglia senza passare dal worker', async () => {
    realtime.authorized.set(true);
    await mount();
    const sentBeforeTrial = worker.sent.length;

    button('Un passo').click();
    await flush();

    // La prova vive nella pagina: il worker del player non riceve nulla (§13.1).
    expect(worker.sent.length).toBe(sentBeforeTrial);
    expect(pageText()).toContain('Stai provando: 1 passi.');
    expect(pageText()).toContain('La prova è solo per te');
    const character = page().querySelector('.cell-character');
    expect(character?.getAttribute('aria-label')).toContain('Riga 1, colonna 2');
  });

  it('la prova si ferma e la scena resta quando il maestro mette in pausa', async () => {
    realtime.authorized.set(true);
    await mount();
    button('Un passo').click();
    await flush();
    expect(page().querySelector('.cell-character')).not.toBeNull();

    realtime.desiredState.set({ ...desiredState('Exercise', true, 'a1'), teacherPaused: true });
    await flush();

    expect(pageText()).toContain('In pausa: guarda il maestro');
    expect(button('Prova').disabled).toBe(true);
    // La posizione raggiunta resta: si è smesso di muoverla, non si è cancellata.
    expect(page().querySelector('.cell-character')).not.toBeNull();
  });

  it('affianca il progetto e il programma nella stessa area di lavoro, con il progetto a sinistra', async () => {
    realtime.authorized.set(true);
    await mount();

    const area = page().querySelector('.work-area');
    expect(area).not.toBeNull();
    expect(
      [...area!.querySelectorAll('section')].map((section) => section.getAttribute('aria-label')),
    ).toEqual(['Il mio progetto', 'Il mio programma']);

    // I comandi della prova stanno con la scena, non con l'editor.
    const program = area!.querySelector('section[aria-label="Il mio programma"]')!;
    expect(
      [...program.querySelectorAll('button')].map((control) => control.textContent?.trim()),
    ).toEqual(['Prova', 'Un passo', 'Da capo']);
  });

  it('un esercizio che conserva valori affianca il progetto e le variabili, senza la griglia', async () => {
    // Il difetto trovato dal collaudo: la pagina montava solo il pannello delle variabili, quindi il
    // bambino non poteva comporre il programma che il server gli chiedeva di correggere.
    api.exercise = {
      ...api.exercise,
      exerciseTitle: 'Somma uno',
      exerciseType: 'ValueProgram',
      engineVersion: 'valueprogram-1',
      definitionJson: valueProgramDefinitionJson,
    };
    api.attempt = {
      ...api.attempt,
      workspaceJson: emptyValueProgramWorkspace,
      workspaceHash: 'impronta-vp',
      projectRevision: '1',
    };
    realtime.authorized.set(true);

    await mount();

    const area = page().querySelector('.work-area');
    expect(area).not.toBeNull();
    expect(
      [...area!.querySelectorAll('section')].map((section) => section.getAttribute('aria-label')),
    ).toEqual(['Il mio progetto', 'Le variabili e l’uscita']);
    expect(
      area!.querySelector('section[aria-label="Il mio progetto"] app-participant-workspace'),
    ).not.toBeNull();
    expect(area!.querySelector('section[aria-label="Il mio programma"]')).toBeNull();
    expect(area!.classList.contains('one-panel')).toBe(false);
    expect(pageText()).toContain('Somma uno');
    expect(pageText()).toContain('Le variabili e l’uscita');
    expect(pageText()).toContain('Il progetto conserva i tuoi blocchi.');
  });

  it('un esercizio che conserva valori non mostra il riquadro del programma del player', async () => {
    api.exercise = {
      ...api.exercise,
      exerciseTitle: 'Somma uno',
      exerciseType: 'ValueProgram',
      engineVersion: 'valueprogram-1',
      definitionJson: valueProgramDefinitionJson,
    };
    api.attempt = {
      ...api.attempt,
      workspaceJson: emptyValueProgramWorkspace,
      workspaceHash: 'impronta-vp',
    };
    realtime.authorized.set(true);

    await mount();

    const badges = [...page().querySelectorAll('app-status-badge')];
    expect(badges.map((badge) => badge.textContent?.trim())).toEqual([
      'Collegato alla lezione',
      'Puoi lavorare!',
    ]);
  });

  it('un esercizio dichiarativo occupa una riga sola e non monta il progetto', async () => {
    api.exercise = {
      ...api.exercise,
      exerciseTitle: 'Quanto fa 2 + 2?',
      exerciseType: 'Quiz',
      engineVersion: 'quiz-1',
      definitionJson: quizDefinitionJson,
    };
    api.attempt = {
      ...api.attempt,
      workspaceJson: emptyQuizAnswer,
      workspaceHash: 'impronta-quiz',
    };
    realtime.authorized.set(true);

    await mount();

    const area = page().querySelector('.work-area');
    expect(area).not.toBeNull();
    expect(
      [...area!.querySelectorAll('section')].map((section) => section.getAttribute('aria-label')),
    ).toEqual(['La mia risposta']);
    expect(area!.querySelector('app-participant-workspace')).toBeNull();
    expect(area!.classList.contains('one-panel')).toBe(true);

    const badges = [...page().querySelectorAll('app-status-badge')];
    expect(badges.map((badge) => badge.textContent?.trim())).toEqual([
      'Collegato alla lezione',
      'Puoi lavorare!',
    ]);
  });

  it('con un solo pannello l’area di lavoro non lascia una colonna vuota', async () => {
    api.snapshot = snapshot('Waiting', false);

    await mount();

    // In attesa non c'è ancora un progetto: resta il programma, ed è l'unico pannello dell'area.
    const area = page().querySelector('.work-area');
    expect(area).not.toBeNull();
    expect(
      [...area!.querySelectorAll('section')].map((section) => section.getAttribute('aria-label')),
    ).toEqual(['Il mio programma']);
  });

  async function mount(): Promise<void> {
    harness = await RouterTestingHarness.create('/lezione');
    await flush();
  }

  function page(): HTMLElement {
    const element = harness.routeNativeElement;
    if (element === null) {
      throw new Error('La pagina della lezione non è montata.');
    }

    return element;
  }

  function pageText(): string {
    return page().textContent ?? '';
  }

  function button(label: string): HTMLButtonElement {
    const found = [...page().querySelectorAll('button')].find(
      (candidate) => (candidate.textContent ?? '').trim() === label,
    );
    if (found === undefined) {
      throw new Error(`Pulsante non trovato: ${label}`);
    }

    return found;
  }

  async function flush(): Promise<void> {
    harness.detectChanges();
    await harness.fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    harness.detectChanges();
  }
});

/** Stato desiderato di prova: la vista è quella dell'esercizio, con i controlli accesi. */
function snapshot(
  view: string,
  interactionEnabled: boolean,
  directive: ParticipantSnapshot['desiredState']['executionDirective'] = null,
): ParticipantSnapshot {
  return {
    protocolVersion: 1,
    serverInstanceId: '11111111-1111-1111-1111-111111111111',
    sessionId: 'sess1',
    participantId: 'p1',
    effectiveRevision: '9',
    connectionEpoch: '2',
    issuedAtUtc: '2026-10-02T08:00:00Z',
    desiredState: desiredState(
      view,
      interactionEnabled,
      view === 'Exercise' ? 'a1' : null,
      directive,
    ),
  };
}

/** Stato desiderato come lo consegna un comando o lo snapshot. */
function desiredState(
  view: string,
  interactionEnabled: boolean,
  attemptId: string | null,
  directive: DesiredState['executionDirective'] = null,
): DesiredState {
  return {
    view,
    interactionEnabled,
    assignmentId: attemptId === null ? null : 'as1',
    attemptId,
    teacherPaused: false,
    recoveryBlocked: false,
    executionGeneration: '0',
    executionDirective: directive,
  };
}
