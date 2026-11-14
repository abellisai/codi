import { DestroyRef, Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { ApiService, ParticipantMessage, ParticipantThread, toApiError } from '@codi/api';
import { ParticipantRealtime } from './participant-realtime';

/** Limite in grafemi del messaggio: lo stesso che il dominio applica alla scrittura (§7.4, §8). */
const MaxMessageGraphemes = 300;

/**
 * Conta i grafemi come li conta una persona. Un emoji è un grafema per il bambino e per il dominio,
 * mentre `length` conta le unità UTF-16 e rifiuterebbe un testo che il server accetterebbe.
 */
const segments = new Intl.Segmenter('it', { granularity: 'grapheme' });

function graphemeCount(text: string): number {
  return [...segments.segment(text)].length;
}

/**
 * Chat fra l’insegnante e il singolo alunno (§8, §15.1). Il bambino legge ciò che l’insegnante gli
 * scrive e risponde; non esiste una chat fra bambini, perché il thread è sempre fra l’insegnante e un
 * alunno solo, e l'identità arriva dalla credenziale.
 *
 * Il thread vive in memoria sul server e non si persiste: l'hub annuncia i messaggi nuovi ma non
 * ripete il thread, quindi la lettura si rifà all'apertura e a ogni riconnessione, e i due flussi si
 * ricompongono per identificativo senza duplicare un messaggio già visto.
 *
 * La chat sta con la pagina: si scrive quando l’insegnante lascia la pagina al lavoro, e il server
 * rifiuta comunque la scrittura mentre la pagina è in attesa o in pausa. Leggere il thread si può
 * sempre: un messaggio dell’insegnante non si perde perché i controlli sono chiusi.
 */
@Injectable({ providedIn: 'root' })
export class ParticipantChat {
  private readonly api = inject(ApiService);
  private readonly realtime = inject(ParticipantRealtime);

  /** Thread mostrato, in ordine di scrittura. */
  readonly messages = signal<readonly ParticipantMessage[]>([]);

  /** Vero mentre una lettura o un invio è in corso: la pagina non offre un secondo invio. */
  readonly busy = signal(false);

  /** Problema dichiarato all'alunno: distinto dal thread vuoto, che è un esito normale. */
  readonly problem = signal('');

  /** Identificativi già mostrati: una consegna ripetuta o una rilettura non duplicano il messaggio. */
  private seen = new Set<string>();

  /** La prima lettura l'ha già fatta il costruttore: l'effetto si occupa dei ritorni. */
  private initialReadDone = false;

  /**
   * Vero quando il bambino può rispondere. È la stessa condizione che il server applica al comando:
   * pagina al lavoro, cioè né in attesa né in pausa dell’insegnante. La connessione deve essere
   * operativa, perché il caso d'uso ricava la sessione dal registro delle connessioni.
   */
  readonly canWrite = computed(() => {
    const state = this.realtime.desiredState();

    return (
      this.realtime.status() === 'connected' &&
      state !== null &&
      state.view !== 'Waiting' &&
      state.view !== 'Goodbye' &&
      !state.teacherPaused
    );
  });

  constructor() {
    // La prima lettura è immediata — chi ricarica la pagina deve vedere subito ciò che l’insegnante
    // gli ha scritto — e poi si rilegge a ogni ritorno del collegamento: l'hub non ripete ciò che è
    // arrivato mentre la pagina non c'era, e un messaggio perso non deve sparire per sempre.
    effect(() => {
      if (this.realtime.status() !== 'connected') return;
      if (!this.initialReadDone) {
        this.initialReadDone = true;

        return;
      }
      untracked(() => void this.load());
    });

    // I messaggi annunciati dall'hub si aggiungono al thread, una volta sola ciascuno.
    effect(() => {
      for (const message of this.realtime.chatIncoming()) {
        if (this.seen.has(message.messageId)) continue;
        untracked(() => this.append(message));
      }
    });

    inject(DestroyRef).onDestroy(() => this.reset());

    void this.load();
  }

  /**
   * Legge il proprio thread. Il conteggio dei non letti è quello dell’insegnante e non si mostra: il
   * server non misura se un bambino ha letto, e un numero inventato sarebbe peggio di nessun numero.
   */
  async load(): Promise<void> {
    try {
      const thread = await this.api.executeQuery<ParticipantThread>('GetOwnMessages', {});
      this.merge(thread);
      this.problem.set('');
    } catch (failure) {
      const error = toApiError(failure);
      // Una credenziale scaduta non è un problema della chat: la pagina riporta all'ingresso.
      this.problem.set(error.requiresSignIn ? '' : error.message);
    }
  }

  /**
   * Scrive all’insegnante. Il testo è verificato qui per la forma e la lunghezza, così il bambino ha
   * subito una risposta comprensibile invece di un rifiuto dopo il viaggio; il server resta comunque
   * l'unico che decide se la scrittura è ammessa.
   */
  async send(text: string): Promise<ParticipantMessage | null> {
    const message = text.trim();
    if (!this.canWrite() || this.busy()) return null;
    if (message === '') {
      this.problem.set('Scrivi qualcosa all’insegnante prima di inviare.');

      return null;
    }
    if (graphemeCount(message) > MaxMessageGraphemes) {
      this.problem.set(`Il messaggio è troppo lungo: al massimo ${MaxMessageGraphemes} caratteri.`);

      return null;
    }

    this.busy.set(true);
    this.problem.set('');

    try {
      // La chiave è nuova a ogni invio: serve a non duplicare il messaggio se il trasporto ripete la
      // richiesta, ed è l'unica idempotenza della chat, che non si persiste (§12.2).
      const stored = await this.api.executeCommand<ParticipantMessage>('PostParticipantMessage', {
        text: message,
        clientMessageKey: crypto.randomUUID(),
      });
      if (!this.seen.has(stored.messageId)) this.append(stored);

      return stored;
    } catch (failure) {
      this.problem.set(toApiError(failure).message);

      return null;
    } finally {
      this.busy.set(false);
    }
  }

  /** Dimentica il thread: la chat di una lezione non vale per la successiva. */
  reset(): void {
    this.messages.set([]);
    this.seen = new Set<string>();
    this.problem.set('');
  }

  private append(message: ParticipantMessage): void {
    this.seen.add(message.messageId);
    this.messages.update((rows) => [...rows, message]);
  }

  /**
   * Ricompone il thread con quello letto dal server. I messaggi arrivati dall'hub **dopo** la lettura
   * non sono nel thread letto: si accodano invece di essere persi, perché la lettura è partita prima
   * che arrivassero e il server non li ripeterà.
   */
  private merge(thread: ParticipantThread): void {
    const read = new Set(thread.messages.map((message) => message.messageId));
    const arrived = this.messages().filter((message) => !read.has(message.messageId));
    this.seen = new Set([...read, ...arrived.map((message) => message.messageId)]);
    this.messages.set([...thread.messages, ...arrived]);
  }
}
