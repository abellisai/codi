import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideIcons } from '@ng-icons/core';
import { lucidePlay, lucidePause, lucideEraser } from '@ng-icons/lucide';
import {
  ParticipantMessage,
  ParticipantPreviewView,
  SessionParticipantDetail,
  SessionParticipantOverview,
} from '@core/api';
import { ParticipantCard, type ParticipantCardAction } from './participant-card';
import type { PreviewLine } from './preview-lines';

/**
 * La card è un componente di presentazione: riceve la riga del partecipante e le azioni già
 * decise dalla pagina e le rende. Le prove fissano gli input e leggono il DOM e l'evento emesso,
 * senza montare la pagina intera.
 */
describe('ParticipantCard', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      providers: [provideIcons({ lucidePlay, lucidePause, lucideEraser })],
    }).compileComponents();
  });

  it('mostra nome, postazione, presenza, stato desiderato, lavoro e stato osservato', () => {
    const fixture = render({
      participant: participant({
        stationId: 'PC-3',
        view: 'Exercise',
        interactionEnabled: true,
        attemptNumber: '2',
        attemptStatus: 'Running',
        projectRevision: '5',
        observedState: {
          connectionEpoch: '3',
          effectiveRevision: '9',
          attemptId: 'a1',
          executionGeneration: '1',
          sequence: '4',
          runtimeStatus: 'Completed',
          reason: null,
          runId: 'r1',
          workspaceHash: 'h5',
          scene: null,
          receivedAtUtc: '2026-10-01T08:12:00Z',
        },
      }),
    });

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Luca');
    expect(text).toContain('postazione PC-3');
    expect(text).toContain('Connesso');
    expect(text).toContain('Exercise · controlli attivi');
    expect(text).toContain('Tentativo 2 · Running · progetto rev. 5');
    // Lo stato confermato dall'osservazione è dichiarato a parte da quello desiderato.
    expect(text).toContain('Completed');
  });

  it('dichiara l’esclusione quando l’ammissione non è «Admitted»', () => {
    const fixture = render({ participant: participant({ admissionStatus: 'Excluded' }) });

    expect(fixture.nativeElement.textContent as string).toContain('escluso');
  });

  it('mostra il blocco di recupero quando l’alunno è bloccato dopo il reload', () => {
    const fixture = render({
      participant: participant({ recoveryBlocked: true, interactionEnabled: false }),
    });

    // §12.3: il blocco deve vedersi a colpo d'occhio e dire che il lavoro è conservato.
    expect(fixture.nativeElement.textContent as string).toContain(
      'blocco di recupero: controlli chiusi, lavoro conservato',
    );
    expect((fixture.nativeElement as HTMLElement).querySelector('.flag.recovery')).not.toBeNull();
  });

  it('non mostra il blocco di recupero quando l’alunno non è bloccato', () => {
    const fixture = render({ participant: participant() });

    expect((fixture.nativeElement as HTMLElement).querySelector('.flag.recovery')).toBeNull();
  });

  it('rende le azioni come pulsanti con etichetta accessibile e le disabilita quando non ammesse', () => {
    const fixture = render({
      participant: participant(),
      actions: [
        { key: 'run', label: 'Avvia il progetto', icon: 'lucidePlay', enabled: false },
        { key: 'pause', label: 'Metti in pausa', icon: 'lucidePause', enabled: true },
      ],
    });

    const run = buttonByLabel(fixture, 'Avvia il progetto: Luca');
    const pause = buttonByLabel(fixture, 'Metti in pausa: Luca');
    expect(run.disabled).toBe(true);
    expect(pause.disabled).toBe(false);
  });

  it('rende le azioni come pulsanti con etichetta visibile accanto all’icona', () => {
    const fixture = render({
      participant: participant(),
      actions: [
        { key: 'run', label: 'Avvia il progetto', icon: 'lucidePlay', enabled: true },
        { key: 'pause', label: 'Metti in pausa', icon: 'lucidePause', enabled: true },
      ],
    });

    // QA/B4: l'etichetta non vive solo nell'aria-label, si legge sulla card.
    for (const label of ['Avvia il progetto', 'Metti in pausa']) {
      const button = buttonByLabel(fixture, `${label}: Luca`);
      const text = (button.textContent ?? '').trim();
      expect(text).toContain(label);
      // L'icona resta accanto al testo: l'attributo ARIA non sostituisce il testo visibile.
      expect(button.querySelector('ng-icon')).not.toBeNull();
    }
  });

  it('rende visibile l’etichetta anche nell’azione con conferma (Azzera)', () => {
    const fixture = render({
      participant: participant(),
      actions: [
        {
          key: 'reset',
          label: 'Azzera il lavoro',
          icon: 'lucideEraser',
          enabled: true,
          requiresConfirmation: true,
          confirmText: 'Il lavoro dell’alunno sarà archiviato.',
        },
      ],
    });

    const trigger = buttonByLabel(fixture, 'Azzera il lavoro: Luca');
    expect((trigger.textContent ?? '').trim()).toContain('Azzera il lavoro');
    // Il dialogo di conferma resta composto: il trigger vive dentro hlm-dialog.
    expect(trigger.closest('hlm-dialog')).not.toBeNull();
  });

  it('non offre alcuna azione mentre un comando è in corso', () => {
    const fixture = render({
      participant: participant(),
      busy: true,
      actions: [{ key: 'run', label: 'Avvia il progetto', icon: 'lucidePlay', enabled: true }],
    });

    expect(buttonByLabel(fixture, 'Avvia il progetto: Luca').disabled).toBe(true);
  });

  it('segnala alla pagina l’azione richiesta', () => {
    const fixture = render({
      participant: participant(),
      actions: [{ key: 'run', label: 'Avvia il progetto', icon: 'lucidePlay', enabled: true }],
    });

    const emitted: { participant: SessionParticipantOverview; key: string }[] = [];
    fixture.componentInstance.action.subscribe((event) => emitted.push(event));

    buttonByLabel(fixture, 'Avvia il progetto: Luca').click();

    expect(emitted).toEqual([{ participant: participant(), key: 'run' }]);
  });

  it('mostra dettaglio e blocchi solo per la card selezionata', () => {
    const fixture = render({
      participant: participant(),
      isSelected: false,
      detail: detail(),
      preview: previewView(),
      previewLines: lines(),
    });

    // Non selezionata: il dettaglio e i blocchi non si vedono.
    expect(fixture.nativeElement.querySelector('.detail')).toBeNull();

    fixture.componentRef.setInput('isSelected', true);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(fixture.nativeElement.querySelector('.detail')).not.toBeNull();
    expect(text).toContain('Percorso');
    expect(text).toContain('quando si avvia');
  });

  it('dichiara l’assenza di anteprima e distingue un problema di lettura', () => {
    // Il dettaglio e l'anteprima si vedono solo nella card selezionata.
    const fixture = render({ participant: participant(), isSelected: true, detail: detail() });

    // Senza anteprima pubblicata il docente vede che non c'è nulla, non un errore.
    expect(fixture.nativeElement.textContent as string).toContain('Nessuna anteprima ricevuta');

    fixture.componentRef.setInput('preview', previewView());
    fixture.componentRef.setInput('previewProblem', 'Anteprima non leggibile');
    fixture.detectChanges();

    const problem = fixture.nativeElement.querySelector('.preview-problem') as HTMLElement | null;
    expect(problem?.textContent).toContain('Anteprima non leggibile');
  });

  it('mostra i non letti e il thread aperto, con i due autori distinti', () => {
    const fixture = render({
      participant: participant({ unreadMessages: 2 }),
      chatOpen: true,
      chat: [
        message({ messageId: 'm1', text: 'Come sta andando?' }),
        message({
          messageId: 'm2',
          author: 'Participant',
          text: 'Ho finito il percorso',
        }),
      ],
    });

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('2 messaggi non letti');
    expect(text).toContain('Come sta andando?');
    expect(text).toContain('Ho finito il percorso');
    // L'autore del docente è «Tu», quello dell'alunno è il suo nome: due autori distinti.
    expect(text).toContain('Tu');
    expect(fixture.nativeElement.querySelectorAll('.chat-message').length).toBe(2);
  });

  it('non mostra la chat quando è chiusa', () => {
    const fixture = render({
      participant: participant({ unreadMessages: 3 }),
      chatOpen: false,
      chat: [message()],
    });

    // Il conteggio dei non letti resta visibile anche a chat chiusa: è il motivo per aprirla.
    expect(fixture.nativeElement.querySelector('.chat')).toBeNull();
    expect(fixture.nativeElement.textContent as string).toContain('3 messaggi non letti');
  });

  it('distingue il thread vuoto da un problema di lettura', async () => {
    const fixture = render({ participant: participant(), chatOpen: true, chat: [] });

    expect(fixture.nativeElement.textContent as string).toContain('ancora vuota');

    fixture.componentRef.setInput('chatProblem', 'Thread non leggibile');
    await settle(fixture);

    const problem = fixture.nativeElement.querySelector('.chat-problem') as HTMLElement | null;
    expect(problem?.textContent).toContain('Thread non leggibile');
  });

  it('invia il messaggio digitato e svuota la bozza solo alla conferma dal thread', async () => {
    const fixture = render({ participant: participant(), chatOpen: true, chat: [] });
    const emitted: { participant: SessionParticipantOverview; text: string }[] = [];
    fixture.componentInstance.chatSend.subscribe((event) => emitted.push(event));

    sendChat(fixture, '  Bravo, continua così  ');
    await settle(fixture);

    // Il testo è normalizzato dalla form, non dalla card: si invia senza spazi ai bordi.
    expect(emitted).toEqual([{ participant: participant(), text: 'Bravo, continua così' }]);
    // La bozza resta finché il server non la conferma: la conferma è il thread riletto.
    expect(chatTextarea(fixture).value).toBe('  Bravo, continua così  ');

    fixture.componentRef.setInput('chat', [message({ text: 'Bravo, continua così' })]);
    await settle(fixture);

    expect(chatTextarea(fixture).value).toBe('');
  });

  it('non invia un messaggio vuoto e dichiara l’errore sul campo', async () => {
    const fixture = render({ participant: participant(), chatOpen: true, chat: [] });
    const emitted: unknown[] = [];
    fixture.componentInstance.chatSend.subscribe((event) => emitted.push(event));

    sendChat(fixture, '   ');
    await settle(fixture);

    expect(emitted).toEqual([]);
    const error = fixture.nativeElement.querySelector('hlm-field-error') as HTMLElement | null;
    expect(error).not.toBeNull();
    expect(error?.getAttribute('role')).toBe('alert');
    expect(error?.textContent).toContain('Scrivi un messaggio da inviare all’alunno.');
  });

  /** Monta la card con gli input indicati: la pagina li deciderebbe, la prova li fissa. */
  function render(inputs: {
    participant: SessionParticipantOverview;
    actions?: readonly ParticipantCardAction[];
    isSelected?: boolean;
    busy?: boolean;
    detail?: SessionParticipantDetail | null;
    preview?: ParticipantPreviewView | null;
    previewLines?: readonly PreviewLine[];
    previewProblem?: string;
    chat?: readonly ParticipantMessage[];
    chatProblem?: string;
    chatBusy?: boolean;
    chatOpen?: boolean;
  }): ComponentFixture<ParticipantCard> {
    const fixture = TestBed.createComponent(ParticipantCard);
    fixture.componentRef.setInput('participant', inputs.participant);
    fixture.componentRef.setInput('actions', inputs.actions ?? []);
    fixture.componentRef.setInput('isSelected', inputs.isSelected ?? false);
    fixture.componentRef.setInput('busy', inputs.busy ?? false);
    fixture.componentRef.setInput('detail', inputs.detail ?? null);
    fixture.componentRef.setInput('preview', inputs.preview ?? null);
    fixture.componentRef.setInput('previewLines', inputs.previewLines ?? []);
    fixture.componentRef.setInput('previewProblem', inputs.previewProblem ?? '');
    fixture.componentRef.setInput('chat', inputs.chat ?? []);
    fixture.componentRef.setInput('chatProblem', inputs.chatProblem ?? '');
    fixture.componentRef.setInput('chatBusy', inputs.chatBusy ?? false);
    fixture.componentRef.setInput('chatOpen', inputs.chatOpen ?? false);
    fixture.detectChanges();

    return fixture;
  }

  /** Lascia stabilizzare la change detection senza zone, poi rilegge il DOM. */
  async function settle(fixture: ComponentFixture<ParticipantCard>): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  /** Campo di testo della chat: la form della card, non un controllo fuori dal modulo. */
  function chatTextarea(fixture: ComponentFixture<ParticipantCard>): HTMLTextAreaElement {
    const element = (fixture.nativeElement as HTMLElement).querySelector('.chat textarea');
    if (!(element instanceof HTMLTextAreaElement)) {
      throw new Error('Campo della chat non trovato.');
    }

    return element;
  }

  /** Digita il testo e invia il modulo come farebbe il docente, senza saltare la validazione. */
  function sendChat(fixture: ComponentFixture<ParticipantCard>, text: string): void {
    const textarea = chatTextarea(fixture);
    textarea.value = text;
    textarea.dispatchEvent(new Event('input'));
    const form = (fixture.nativeElement as HTMLElement).querySelector('.chat form');
    if (form === null) {
      throw new Error('Form della chat non trovata.');
    }

    form.dispatchEvent(new Event('submit'));
  }

  function buttonByLabel(
    fixture: ComponentFixture<ParticipantCard>,
    aria: string,
  ): HTMLButtonElement {
    const button = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (candidate) => candidate.getAttribute('aria-label') === aria,
    );
    if (button === undefined) {
      throw new Error(`Pulsante non trovato: ${aria}`);
    }

    return button;
  }
});

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
      hasCheckpoint: false,
      checkpointEngineVersion: null,
      lastSavedAtUtc: null,
      updatedAtUtc: '2026-10-01T08:09:00Z',
      rowVersion: '2',
    },
    attemptCount: 1,
  };
}

/** Blocchi già tradotti in italiano: la card non interpreta il progetto Blockly. */
function lines(): readonly PreviewLine[] {
  return [
    { depth: 0, label: 'quando si avvia' },
    { depth: 1, label: 'fai un passo' },
  ];
}

function previewView(): ParticipantPreviewView {
  return {
    participantId: 'p1',
    attemptId: 'a1',
    projectRevision: '1',
    workspaceHash: 'h1',
    workspaceJson: JSON.stringify({
      blocks: { languageVersion: 0, blocks: [{ type: 'aula_start' }] },
    }),
    receivedAtUtc: '2026-10-01T08:10:00Z',
    attemptWorkspaceHash: 'h1',
    partial: false,
  };
}

/** Messaggio della chat: lo stesso tipo arriva dalla query e dall'evento dell'hub (§15.1). */
function message(overrides: Partial<ParticipantMessage> = {}): ParticipantMessage {
  return {
    messageId: 'm1',
    sessionId: 's1',
    participantId: 'p1',
    author: 'Teacher',
    text: 'Bravo, continua così',
    writtenAtUtc: '2026-10-13T09:00:00Z',
    ...overrides,
  };
}
