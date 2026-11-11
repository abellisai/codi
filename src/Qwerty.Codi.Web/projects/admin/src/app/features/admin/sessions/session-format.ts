import type { ParticipantPresence, SessionStatus } from '@core/api';

/**
 * Presentazione dei valori di una sessione (§6.1, §14.1): elenco e regia mostrano le stesse
 * etichette, quindi la traduzione dal contratto all'italiano sta in un posto solo e non può
 * divergere fra le due pagine. Sono funzioni pure, senza stato: le pagine restano l'unico punto
 * che decide *dove* mostrarle.
 */

/** Etichetta italiana dello stato: il valore del contratto è per il codice, non per il docente. */
const StatusLabels: Record<SessionStatus, string> = {
  Lobby: 'In attesa',
  Active: 'In corso',
  Paused: 'In pausa',
  Ended: 'Conclusa',
};

/** Traduce lo stato della sessione nell'etichetta che si legge a schermo. */
export function sessionStatusLabel(status: SessionStatus): string {
  return StatusLabels[status];
}

/** Dice se gli ingressi sono aperti: testo esplicito, non solo un colore o un simbolo. */
export function admissionsLabel(open: boolean): string {
  return open ? 'Aperti' : 'Chiusi';
}

/**
 * Presenza misurata dal server (§11.6). Il docente vede in parole se il PC dell'alunno sta
 * rispondendo, perché «connesso» e «operativo» non sono la stessa cosa.
 */
const PresenceLabels: Record<ParticipantPresence, string> = {
  Fresh: 'Connesso',
  Degraded: 'Presenza debole',
  NotOperational: 'Non operativo',
  Disconnected: 'Non connesso',
};

/** Traduce la presenza nell'etichetta che si legge a schermo. */
export function presenceLabel(presence: ParticipantPresence): string {
  return PresenceLabels[presence];
}

/**
 * Formato degli istanti ISO 8601 UTC in ora locale. `undefined` non è ammesso: il contratto invia
 * `null` quando l'istante non esiste ancora, e in quel caso la cella resta vuota.
 */
const instantFormat = new Intl.DateTimeFormat('it-IT', {
  dateStyle: 'short',
  timeStyle: 'short',
});

/** Mostra un istante del server, o `—` quando la sessione non lo ha ancora raggiunto. */
export function formatInstant(instant: string | null): string {
  if (instant === null || instant === '') {
    return '—';
  }

  const date = new Date(instant);

  return Number.isNaN(date.getTime()) ? '—' : instantFormat.format(date);
}
