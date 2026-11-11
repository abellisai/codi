import type { RealtimeStatus } from '../core/participant-realtime';
import type { StatusIcon, StatusTone } from './status-badge';

/** Riquadro di stato come lo mostra la pagina: tono, icona e testo in parole. */
export interface StatusBadgeSpec {
  readonly tone: StatusTone;
  readonly icon: StatusIcon;
  readonly label: string;
}

/**
 * Tutto ciò che serve a dire all'alunno che cosa sta succedendo: il collegamento (§11.6), che cosa
 * può fare adesso (§6.2) e che cosa sta facendo il suo programma (§13.6, §13.7).
 */
export interface SessionStatusInput {
  readonly connection: RealtimeStatus;
  readonly view: string | null;
  readonly interactionEnabled: boolean;
  readonly teacherPaused: boolean;
  readonly recoveryBlocked: boolean;
  readonly runtimeStatus: string;
  /** Vero mentre l'alunno sta provando il proprio programma in locale. */
  readonly trialRunning: boolean;
}

/**
 * Traduce lo stato in riquadri leggibili a colpo d'occhio (§14.4): ogni riquadro ha un colore, una
 * icona e un testo, e i colori non sono l'unica informazione.
 *
 * L'ordine è quello dell'importanza per il bambino: prima «sono collegato?», poi «posso lavorare?»,
 * infine «che cosa fa il mio programma?». La funzione è pura, così le precedenze — pausa del
 * maestro, recupero, controlli chiusi — si verificano senza montare la pagina.
 */
export function sessionStatusBadges(input: SessionStatusInput): readonly StatusBadgeSpec[] {
  return [connectionBadge(input.connection), workBadge(input), programBadge(input)];
}

/** Collegamento al server: il trasporto aperto non basta, conta l'operatività (§11.6). */
function connectionBadge(status: RealtimeStatus): StatusBadgeSpec {
  switch (status) {
    case 'connected':
      return { tone: 'ok', icon: 'link', label: 'Collegato alla lezione' };
    case 'connecting':
      return { tone: 'wait', icon: 'refresh', label: 'Mi sto collegando…' };
    case 'reconnecting':
      return { tone: 'wait', icon: 'refresh', label: 'Mi sto ricollegando…' };
    case 'waiting':
      return { tone: 'wait', icon: 'hourglass', label: 'Aspetto: c’è un’altra pagina aperta' };
    case 'superseded':
      return { tone: 'stop', icon: 'unlink', label: 'Un’altra pagina ha preso il posto' };
    case 'blocked':
      return { tone: 'stop', icon: 'unlink', label: 'Collegamento interrotto' };
    case 'revoked':
      return { tone: 'stop', icon: 'alert', label: 'Non sei più nella lezione' };
    case 'ended':
      return { tone: 'idle', icon: 'flag', label: 'La lezione è conclusa' };
    default:
      return { tone: 'idle', icon: 'hourglass', label: 'Collegamento non ancora avviato' };
  }
}

/**
 * Che cosa può fare adesso. La precedenza è deliberata: una lezione conclusa non torna indietro, un
 * recupero in corso spiega perché il lavoro è fermo, e la pausa del maestro viene prima dei
 * controlli chiusi — altrimenti il bambino leggerebbe due motivi diversi per la stessa attesa.
 */
function workBadge(input: SessionStatusInput): StatusBadgeSpec {
  if (input.view === 'Goodbye') {
    return { tone: 'idle', icon: 'flag', label: 'La lezione è conclusa: puoi uscire' };
  }

  if (input.recoveryBlocked) {
    return { tone: 'wait', icon: 'refresh', label: 'Stiamo recuperando il tuo lavoro' };
  }

  if (input.teacherPaused) {
    return { tone: 'stop', icon: 'pause', label: 'In pausa: guarda il maestro' };
  }

  if (input.view === 'Waiting') {
    return { tone: 'wait', icon: 'hourglass', label: 'Aspetta: il maestro prepara l’attività' };
  }

  if (!input.interactionEnabled) {
    return { tone: 'wait', icon: 'lock', label: 'Controlli chiusi: aspetta il maestro' };
  }

  return { tone: 'ok', icon: 'play', label: 'Puoi lavorare!' };
}

/** Che cosa sta facendo il programma: la prova locale dell'alunno viene prima dello stato comandato. */
function programBadge(input: SessionStatusInput): StatusBadgeSpec {
  if (input.trialRunning) {
    return { tone: 'work', icon: 'play', label: 'Stai provando il tuo programma' };
  }

  switch (input.runtimeStatus) {
    case 'Running':
      return { tone: 'work', icon: 'play', label: 'Il tuo programma sta andando' };
    case 'Paused':
      return { tone: 'wait', icon: 'pause', label: 'Programma in pausa' };
    case 'Completed':
      return { tone: 'ok', icon: 'star', label: 'Programma completato' };
    case 'Stopped':
      return { tone: 'idle', icon: 'flag', label: 'Programma fermato' };
    case 'Error':
      return { tone: 'stop', icon: 'alert', label: 'Il programma non parte: controlla i blocchi' };
    default:
      return { tone: 'idle', icon: 'hourglass', label: 'Programma pronto' };
  }
}
