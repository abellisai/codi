import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  ApiError,
  ApiService,
  DesiredState,
  ParticipantMessage,
  ParticipantThread,
} from '@codi/api';
import { vi } from 'vitest';
import { ParticipantChat } from './participant-chat';
import { ParticipantRealtime, RealtimeStatus } from './participant-realtime';

/**
 * Il servizio si prova dentro un contesto di iniezione: gli `effect` che compongono il thread hanno
 * bisogno di un ciclo di change detection, e questo è il modo più economico per averlo.
 */
@Component({
  selector: 'app-chat-host',
  template: '',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class ChatHost {
  readonly chat = inject(ParticipantChat);
}

const ready: DesiredState = {
  view: 'Exercise',
  interactionEnabled: true,
  assignmentId: 'a',
  attemptId: 't',
  teacherPaused: false,
  recoveryBlocked: false,
  executionGeneration: '0',
  executionDirective: null,
};

describe('ParticipantChat', () => {
  const api = { executeQuery: vi.fn(), executeCommand: vi.fn() };
  const realtime = {
    status: signal<RealtimeStatus>('connected'),
    desiredState: signal<DesiredState | null>(ready),
    chatIncoming: signal<readonly ParticipantMessage[]>([]),
  };
  let fixture: ComponentFixture<ChatHost>;

  beforeEach(() => {
    api.executeQuery.mockReset().mockResolvedValue(thread());
    api.executeCommand.mockReset();
    realtime.status.set('connected');
    realtime.desiredState.set(ready);
    realtime.chatIncoming.set([]);
    TestBed.configureTestingModule({
      providers: [
        { provide: ApiService, useValue: api },
        { provide: ParticipantRealtime, useValue: realtime },
      ],
    });
  });

  it('legge il proprio thread all’apertura, senza i non letti del maestro', async () => {
    api.executeQuery.mockResolvedValue(thread([teacher('m1', 'Come sta andando?')], 3));

    await render();

    expect(api.executeQuery).toHaveBeenCalledWith('GetOwnMessages', {});
    expect(chat().messages()).toHaveLength(1);
    expect(chat().messages()[0]?.text).toBe('Come sta andando?');
    // Il conteggio dei non letti è del maestro: la vista del bambino non lo porta con sé.
    expect('unreadByTeacher' in chat()).toBe(false);
  });

  it('aggiunge i messaggi annunciati dall’hub una volta sola', async () => {
    await render();
    expect(chat().messages()).toHaveLength(0);

    realtime.chatIncoming.set([teacher('h1', 'Bravo!')]);
    await settle();
    expect(
      chat()
        .messages()
        .map((message) => message.messageId),
    ).toEqual(['h1']);

    // La consegna ripetuta dello stesso messaggio non lo mostra due volte.
    realtime.chatIncoming.set([teacher('h1', 'Bravo!'), participant('h2', 'Grazie')]);
    await settle();
    expect(
      chat()
        .messages()
        .map((message) => message.messageId),
    ).toEqual(['h1', 'h2']);
  });

  it('rilegge il thread quando il collegamento torna operativo', async () => {
    await render();
    expect(api.executeQuery).toHaveBeenCalledTimes(1);

    // L'hub non ripete ciò che è arrivato mentre la pagina non c'era: la lettura si rifà.
    realtime.status.set('reconnecting');
    await settle();
    realtime.status.set('connected');
    await settle();

    expect(api.executeQuery).toHaveBeenCalledTimes(2);
  });

  it('invia la risposta con una chiave nuova e mostra il messaggio confermato', async () => {
    api.executeCommand.mockResolvedValue(participant('m2', 'Ho finito il percorso'));
    await render();

    const stored = await chat().send('  Ho finito il percorso  ');

    expect(stored?.messageId).toBe('m2');
    const [name, body] = api.executeCommand.mock.calls[0] as [string, Record<string, unknown>];
    expect(name).toBe('PostParticipantMessage');
    // Il testo arriva senza spazi ai bordi, con la chiave che rende la ripetizione innocua.
    expect(body['text']).toBe('Ho finito il percorso');
    expect(String(body['clientMessageKey'])).not.toBe('');
    expect(
      chat()
        .messages()
        .map((message) => message.messageId),
    ).toEqual(['m2']);
  });

  it('non scrive quando la pagina è in attesa e quando è in pausa', async () => {
    await render();

    realtime.desiredState.set({ ...ready, view: 'Waiting' });
    await settle();
    expect(await chat().send('Ci sono')).toBeNull();

    realtime.desiredState.set({ ...ready, teacherPaused: true });
    await settle();
    expect(await chat().send('Ci sono')).toBeNull();

    expect(api.executeCommand).not.toHaveBeenCalled();
  });

  it('rifiuta un messaggio troppo lungo senza chiamare il server', async () => {
    await render();

    const tooLong = 'a'.repeat(301);
    expect(await chat().send(tooLong)).toBeNull();

    expect(chat().problem()).toContain('troppo lungo');
    expect(api.executeCommand).not.toHaveBeenCalled();
  });

  it('non lascia che una credenziale scaduta diventi un problema della chat', async () => {
    api.executeQuery.mockRejectedValue(
      new ApiError('AuthenticationRequired', 401, 'Non sei dentro.'),
    );

    await render();

    // La pagina riporta all'ingresso: alla chat non tocca dirlo una seconda volta.
    expect(chat().problem()).toBe('');
    expect(chat().messages()).toHaveLength(0);
  });

  it('non perde un messaggio arrivato mentre la lettura era in corso', async () => {
    let complete!: (value: ParticipantThread) => void;
    api.executeQuery.mockReturnValue(
      new Promise<ParticipantThread>((resolve) => {
        complete = resolve;
      }),
    );

    await render();
    // Il messaggio arriva dall'hub mentre la query è ancora in volo: la lettura non lo conterrà.
    realtime.chatIncoming.set([teacher('h1', 'Sei pronto?')]);
    await settle();
    complete(thread([teacher('m1', 'Iniziamo')]));
    await settle();

    expect(
      chat()
        .messages()
        .map((message) => message.messageId),
    ).toEqual(['m1', 'h1']);
  });

  /** Monta il servizio nel contesto di iniezione: da qui in poi gli `effect` hanno un ciclo. */
  async function render(): Promise<void> {
    fixture = TestBed.createComponent(ChatHost);
    await settle();
  }

  /**
   * Cede il turno all'event loop e rilegge: senza `zone.js` la promessa di un gestore non è
   * un'attività registrata, quindi `whenStable` da solo non basta (§20.3).
   */
  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  function chat(): ParticipantChat {
    return fixture.componentInstance.chat;
  }
});

/** Thread come lo restituisce `GetOwnMessages`: i non letti sono del maestro. */
function thread(
  messages: readonly ParticipantMessage[] = [],
  unreadByTeacher = 0,
): ParticipantThread {
  return { participantId: 'p', messages, unreadByTeacher };
}

function teacher(messageId: string, text: string): ParticipantMessage {
  return message(messageId, 'Teacher', text);
}

function participant(messageId: string, text: string): ParticipantMessage {
  return message(messageId, 'Participant', text);
}

function message(
  messageId: string,
  author: ParticipantMessage['author'],
  text: string,
): ParticipantMessage {
  return {
    messageId,
    sessionId: 's',
    participantId: 'p',
    author,
    text,
    writtenAtUtc: '2026-10-13T09:00:00Z',
  };
}
