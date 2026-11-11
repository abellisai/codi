import { DestroyRef, Injectable, effect, inject, signal, untracked } from '@angular/core';
import { ApiService, ParticipantMessage, ParticipantThread, toApiError } from '@core/api';
import { TeacherRealtime } from '@core/teacher-realtime';

/**
 * Chat fra il docente e il singolo alunno (§8, §15.1). Il thread vive in memoria sul server e viene
 * eliminato quando la lezione finisce: il servizio è l'unico punto che lo possiede, perché la lettura
 * ha un effetto osservabile sulla card — il numero dei non letti si azzera solo quando il docente
 * legge il thread.
 *
 * La lettura iniziale usa la query; i messaggi nuovi arrivano dall'hub. Un messaggio già visto non
 * compare due volte: dopo una riconnessione il client non riceve un replay del thread, quindi la
 * deduplica per identificativo è la difesa contro una consegna ripetuta.
 */
@Injectable({ providedIn: 'root' })
export class SessionChatService {
  private readonly api = inject(ApiService);
  private readonly realtime = inject(TeacherRealtime);
  private readonly destroyRef = inject(DestroyRef);

  /** Sessione mostrata dalla pagina: la chat appartiene a lei. */
  readonly sessionId = signal('');

  /** Alunno cui il docente sta leggendo il thread: uno alla volta, come il dettaglio (§14.3). */
  readonly openFor = signal('');

  /** Thread dell'alunno aperto, in ordine di scrittura. */
  readonly thread = signal<readonly ParticipantMessage[]>([]);

  readonly busy = signal(false);
  readonly problem = signal('');

  /** Identificativi già mostrati: una riconnessione non deve duplicare un messaggio. */
  private readonly seen = new Set<string>();

  constructor() {
    // I messaggi annunciati dall'hub si aggiungono solo al thread aperto.
    effect(() => {
      const open = this.openFor();
      if (open === '') return;
      for (const message of this.realtime.chat()) {
        if (message.participantId !== open || this.seen.has(message.messageId)) continue;
        untracked(() => {
          this.seen.add(message.messageId);
          this.thread.update((rows) => [...rows, message]);
        });
      }
    });

    this.destroyRef.onDestroy(() => this.close());
  }

  /** true quando il thread mostrato è quello di questo alunno. */
  isOpen(participantId: string): boolean {
    return this.openFor() === participantId;
  }

  /**
   * Apre il thread di un alunno e lo legge: la lettura azzera i non letti, quindi la card cambia.
   * Un thread vuoto è un esito normale e non un errore.
   */
  async open(participantId: string): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.openFor.set(participantId);
    this.thread.set([]);
    this.seen.clear();
    this.problem.set('');
    this.busy.set(true);

    try {
      const read = await this.api.executeQuery<ParticipantThread>('GetParticipantMessages', {
        sessionId: this.sessionId(),
        participantId,
      });
      if (this.openFor() !== participantId) return;
      this.thread.set(read.messages);
      for (const message of read.messages) this.seen.add(message.messageId);
    } catch (failure) {
      if (this.openFor() !== participantId) return;
      this.problem.set(toApiError(failure).message);
    } finally {
      this.busy.set(false);
    }
  }

  /** Apre il thread, o lo chiude se è già quello aperto. */
  async toggle(participantId: string): Promise<void> {
    if (this.isOpen(participantId)) {
      this.close();

      return;
    }

    await this.open(participantId);
  }

  /**
   * Scrive un messaggio verso l'alunno. La chiave del client è un identificativo nuovo per invio:
   * serve a non duplicare la stessa richiesta se il trasporto la ripete. Il server decide se la
   * scrittura è ammessa; un rifiuto conserva la bozza del docente.
   */
  async send(participantId: string, text: string): Promise<ParticipantMessage | null> {
    if (this.busy()) {
      return null;
    }

    this.busy.set(true);
    this.problem.set('');

    try {
      const stored = await this.api.executeCommand<ParticipantMessage>('PostTeacherMessage', {
        sessionId: this.sessionId(),
        participantId,
        text,
        clientMessageKey: crypto.randomUUID(),
      });
      if (!this.seen.has(stored.messageId)) {
        this.seen.add(stored.messageId);
        this.thread.update((rows) => [...rows, stored]);
      }

      return stored;
    } catch (failure) {
      this.problem.set(toApiError(failure).message);

      return null;
    } finally {
      this.busy.set(false);
    }
  }

  /** Chiude la lettura: il thread sul server resta, qui si chiude solo la vista del docente. */
  close(): void {
    this.openFor.set('');
    this.thread.set([]);
    this.seen.clear();
    this.problem.set('');
  }

  /** Sessione mostrata: cambiare sessione chiude il thread aperto, come fa il dettaglio. */
  show(sessionId: string): void {
    if (this.sessionId() === sessionId) return;
    this.sessionId.set(sessionId);
    this.close();
  }
}
