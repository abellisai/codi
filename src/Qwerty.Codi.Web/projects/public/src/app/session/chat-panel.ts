import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ParticipantMessage } from '@codi/api';
import { ParticipantChat } from '../core/participant-chat';

/**
 * Chat del bambino con l’insegnante (§8, §15.1): legge ciò che l’insegnante ha scritto e risponde.
 *
 * Il pannello **si apre da sé** quando c'è un messaggio dell’insegnante: a otto anni non si cerca una
 * chat, e un messaggio che resta chiuso dietro un pulsante è un messaggio non letto. Quando non c'è
 * nulla, resta una sola riga con il pulsante per scrivere: un riquadro vuoto permanente sarebbe
 * rumore su una pagina che serve a lavorare.
 *
 * I messaggi si mostrano come testo, mai come HTML. Il thread è fra l’insegnante e questo bambino: non
 * esiste una chat fra bambini, e l'autore dell'altro lato è sempre l’insegnante.
 */
@Component({
  selector: 'app-chat-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (!visible()) {
      <p class="actions">
        <button type="button" (click)="opened.set(true)">
          <span aria-hidden="true">✉</span> Scrivi all’insegnante
        </button>
      </p>
    } @else {
      <section class="chat" aria-label="Messaggi con l’insegnante">
        <h2>Messaggi con l’insegnante</h2>

        <ol class="thread" aria-live="polite">
          @for (message of chat.messages(); track message.messageId) {
            <li class="message" [class.mine]="message.author === 'Participant'">
              <span class="who">{{ who(message) }}</span>
              <span class="text">{{ message.text }}</span>
            </li>
          }
          @if (chat.messages().length === 0) {
            <li class="empty">Qui non c’è ancora niente: puoi scrivere tu.</li>
          }
        </ol>

        @if (chat.problem(); as problem) {
          <p role="alert">{{ problem }}</p>
        }

        @if (chat.canWrite()) {
          <label for="chat-text">La tua risposta</label>
          <textarea
            id="chat-text"
            rows="3"
            [value]="draft()"
            [disabled]="chat.busy()"
            (input)="typed($event)"
          ></textarea>
          <p class="actions">
            <button
              type="button"
              [disabled]="chat.busy() || draft().trim() === ''"
              (click)="send()"
            >
              Invia
            </button>
            <button type="button" (click)="opened.set(false)">Chiudi</button>
          </p>
        } @else {
          <!-- La chat sta con la pagina: in attesa o in pausa il server rifiuta la scrittura, e il
               bambino deve sapere perché il campo non c'è invece di cercarlo. -->
          <p role="status">Puoi rispondere quando l’insegnante apre l’attività.</p>
          <p class="actions">
            <button type="button" (click)="opened.set(false)">Chiudi</button>
          </p>
        }
      </section>
    }
  `,
  styles: `
    .chat {
      margin: 0.5rem 0 1rem;
      padding: 1.5rem;
      border: 2px solid #d6cbed;
      border-radius: 1.5rem;
      background: #f8fafc;
      max-width: 48rem;
      margin-top: 1.5rem;
    }
    .chat h2 {
      margin: 0 0 0.5rem;
      font-size: 1.25rem;
    }
    .thread {
      display: grid;
      gap: 0.4rem;
      margin: 0 0 0.75rem;
      padding: 0;
      list-style: none;
    }
    .message {
      display: grid;
      gap: 0.1rem;
      padding: 0.5rem 0.75rem;
      border-radius: 0.5rem;
      background: #fff;
      border: 1px solid #e2e8f0;
    }
    .message.mine {
      background: #eef6ff;
      border-color: #bfdbfe;
    }
    .who {
      font-size: 1rem;
      font-weight: 700;
      color: #3d5a73;
    }
    .text {
      font-size: 1.25rem;
      overflow-wrap: anywhere;
    }
    .empty {
      color: #444;
      font-style: italic;
    }
    textarea {
      display: block;
      width: 100%;
      max-width: 40rem;
      font-size: 1.25rem;
      padding: 0.4rem 0.6rem;
    }
    .actions {
      display: flex;
      gap: 0.5rem;
      margin-top: 0.75rem;
    }
    .actions button {
      padding: 0.75rem 1.5rem;
      font-size: 1rem;
    }
  `,
})
export class ChatPanel {
  protected readonly chat = inject(ParticipantChat);

  /** Il pannello aperto a mano, quando non c'è ancora un messaggio da mostrare. */
  protected readonly opened = signal(false);

  /** Testo in scrittura: la bozza resta se il server rifiuta, così non si riscrive. */
  protected readonly draft = signal('');

  /** Visibile quando c'è qualcosa da leggere, o quando il bambino lo ha aperto. */
  protected readonly visible = computed(() => this.chat.messages().length > 0 || this.opened());

  /** Autore del messaggio in parole: il contratto dice `Teacher` o `Participant`. */
  protected who(message: ParticipantMessage): string {
    return message.author === 'Participant' ? 'Tu' : 'L’insegnante';
  }

  protected typed(event: Event): void {
    this.draft.set((event.target as HTMLTextAreaElement).value);
  }

  /** Invia la risposta: la bozza si svuota solo quando il messaggio è stato accettato. */
  protected async send(): Promise<void> {
    const stored = await this.chat.send(this.draft());
    if (stored !== null) this.draft.set('');
  }
}
