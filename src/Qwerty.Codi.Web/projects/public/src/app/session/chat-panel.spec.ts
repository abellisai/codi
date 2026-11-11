import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ParticipantMessage } from '@codi/api';
import { vi } from 'vitest';
import { ParticipantChat } from '../core/participant-chat';
import { ChatPanel } from './chat-panel';

/**
 * Il pannello è presentazione e gesto: riceve il thread dal servizio e gli consegna il testo
 * scritto. Le prove fissano il servizio e leggono il DOM, senza montare la pagina intera.
 */
describe('ChatPanel', () => {
  const chat = {
    messages: signal<readonly ParticipantMessage[]>([]),
    problem: signal(''),
    busy: signal(false),
    canWrite: signal(true),
    send: vi.fn(),
  };
  let fixture: ComponentFixture<ChatPanel>;

  beforeEach(() => {
    chat.messages.set([]);
    chat.problem.set('');
    chat.busy.set(false);
    chat.canWrite.set(true);
    chat.send.mockReset().mockResolvedValue(null);
    TestBed.configureTestingModule({
      providers: [{ provide: ParticipantChat, useValue: chat }],
    });
    fixture = TestBed.createComponent(ChatPanel);
  });

  it('senza messaggi resta una sola riga, e il pannello si apre dal pulsante', async () => {
    await settle();

    expect(fixture.nativeElement.querySelector('.chat')).toBeNull();
    expect(text()).toContain('Scrivi al maestro');

    button('Scrivi al maestro').click();
    await settle();

    expect(fixture.nativeElement.querySelector('.chat')).not.toBeNull();
    expect(text()).toContain('Qui non c’è ancora niente');
  });

  it('un messaggio del maestro apre il pannello da sé e si legge con il suo autore', async () => {
    // Nessuno ha aperto la chat: il pannello compare perché c'è qualcosa da leggere.
    chat.messages.set([teacher('m1', 'Bravo, continua così')]);
    await settle();

    expect(fixture.nativeElement.querySelector('.chat')).not.toBeNull();
    expect(text()).toContain('Il maestro');
    expect(text()).toContain('Bravo, continua così');
    expect(fixture.nativeElement.querySelectorAll('.message')).toHaveLength(1);
  });

  it('invia il testo digitato e svuota la bozza quando il messaggio è accettato', async () => {
    chat.send.mockResolvedValue(participant('m2', 'Ho finito'));
    chat.messages.set([teacher('m1', 'Come va?')]);
    await settle();

    // Con la bozza vuota non si invia: il pulsante è spento.
    expect(button('Invia').disabled).toBe(true);

    type('Ho finito');
    await settle();
    expect(button('Invia').disabled).toBe(false);

    button('Invia').click();
    await settle();

    expect(chat.send).toHaveBeenCalledWith('Ho finito');
    expect(textarea().value).toBe('');
  });

  it('conserva la bozza e dichiara il rifiuto del server', async () => {
    chat.messages.set([teacher('m1', 'Come va?')]);
    chat.send.mockImplementation(() => {
      chat.problem.set('La lezione è terminata: non si può più scrivere alla chat.');

      return Promise.resolve(null);
    });
    await settle();

    type('Maestro, non capisco');
    await settle();
    button('Invia').click();
    await settle();

    // Il testo resta: il bambino non deve riscriverlo.
    expect(textarea().value).toBe('Maestro, non capisco');
    expect(text()).toContain('La lezione è terminata');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
  });

  it('quando la pagina è ferma il campo non c’è e il pannello lo spiega', async () => {
    chat.canWrite.set(false);
    chat.messages.set([teacher('m1', 'Aspetta un momento')]);
    await settle();

    // Il messaggio si legge comunque: ciò che manca è la risposta, e il perché è scritto.
    expect(text()).toContain('Aspetta un momento');
    expect(fixture.nativeElement.querySelector('textarea')).toBeNull();
    expect(text()).toContain('Puoi rispondere quando il maestro apre l’attività');
  });

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function textarea(): HTMLTextAreaElement {
    const element = (fixture.nativeElement as HTMLElement).querySelector('textarea');
    if (!(element instanceof HTMLTextAreaElement)) {
      throw new Error('Campo della risposta non trovato.');
    }

    return element;
  }

  function button(label: string): HTMLButtonElement {
    const found = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (candidate) => candidate.textContent?.trim() === label,
    );
    if (found === undefined) {
      throw new Error(`Pulsante non trovato: ${label}`);
    }

    return found;
  }

  function type(value: string): void {
    const field = textarea();
    field.value = value;
    field.dispatchEvent(new Event('input'));
  }

  /** Rende e attende la catena di promesse avviata dall'interazione, senza `zone.js`. */
  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }
});

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
