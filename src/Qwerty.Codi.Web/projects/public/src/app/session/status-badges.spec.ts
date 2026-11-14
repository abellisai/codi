import { describe, expect, it } from 'vitest';
import type { RealtimeStatus } from '../core/participant-realtime';
import { sessionStatusBadges, type SessionStatusInput } from './status-badges';

/** Stato di partenza: collegato, esercizio attivo, controlli aperti, programma pronto. */
function state(overrides: Partial<SessionStatusInput> = {}): SessionStatusInput {
  return {
    connection: 'connected',
    view: 'Exercise',
    interactionEnabled: true,
    teacherPaused: false,
    recoveryBlocked: false,
    runtimeStatus: 'Ready',
    trialRunning: false,
    ...overrides,
  };
}

/** Riquadro atteso: tono, icona e testo, nell'ordine in cui la pagina li mostra. */
function badges(overrides: Partial<SessionStatusInput> = {}) {
  return sessionStatusBadges(state(overrides));
}

describe('Riquadri di stato dell’alunno', () => {
  it('non mostra lo stato Blockly per una domanda, anche dopo un errore del motore', () => {
    const result = badges({ showsProgram: false, runtimeStatus: 'Error' });
    expect(result).toHaveLength(2);
    expect(result.map((badge) => badge.label)).toEqual([
      'Collegato alla lezione',
      'Puoi lavorare!',
    ]);
  });
  it('collegato e al lavoro: verde e blu, con il programma pronto', () => {
    expect(badges()).toEqual([
      { tone: 'ok', icon: 'link', label: 'Collegato alla lezione' },
      { tone: 'ok', icon: 'play', label: 'Puoi lavorare!' },
      { tone: 'idle', icon: 'hourglass', label: 'Programma pronto' },
    ]);
  });

  it.each([
    ['connecting', 'wait', 'Mi sto collegando…'],
    ['reconnecting', 'wait', 'Mi sto ricollegando…'],
    ['waiting', 'wait', 'Aspetto: c’è un’altra pagina aperta'],
    ['superseded', 'stop', 'Un’altra pagina ha preso il posto'],
    ['blocked', 'stop', 'Collegamento interrotto'],
    ['revoked', 'stop', 'Non sei più nella lezione'],
    ['ended', 'idle', 'La lezione è conclusa'],
  ] as const)('il collegamento «%s» è %s', (connection, tone, label) => {
    const badge = badges({ connection: connection as RealtimeStatus })[0];

    expect(badge).toEqual({ tone, icon: expect.any(String), label });
  });

  it('la pausa del maestro viene prima di ogni altra attesa', () => {
    const [, work] = badges({ teacherPaused: true, view: 'Waiting', interactionEnabled: false });

    expect(work).toEqual({ tone: 'stop', icon: 'pause', label: 'In pausa: guarda il maestro' });
  });

  it('il recupero in corso spiega perché il lavoro è fermo', () => {
    const [, work] = badges({
      recoveryBlocked: true,
      teacherPaused: true,
      interactionEnabled: false,
    });

    expect(work).toEqual({
      tone: 'wait',
      icon: 'refresh',
      label: 'Stiamo recuperando il tuo lavoro',
    });
  });

  it('una lezione conclusa non torna indietro, nemmeno con i controlli chiusi', () => {
    const [, work] = badges({ view: 'Goodbye', interactionEnabled: false, teacherPaused: true });

    expect(work).toEqual({
      tone: 'idle',
      icon: 'flag',
      label: 'La lezione è conclusa: puoi uscire',
    });
  });

  it('controlli chiusi e attesa dichiarano che cosa manca', () => {
    expect(badges({ interactionEnabled: false })[1]).toEqual({
      tone: 'wait',
      icon: 'lock',
      label: 'Controlli chiusi: aspetta il maestro',
    });
    expect(badges({ view: 'Waiting', interactionEnabled: false })[1]).toEqual({
      tone: 'wait',
      icon: 'hourglass',
      label: 'Aspetta: il maestro prepara l’attività',
    });
  });

  it.each([
    ['Running', 'work', 'Il tuo programma sta andando'],
    ['Paused', 'wait', 'Programma in pausa'],
    ['Completed', 'ok', 'Programma completato'],
    ['Stopped', 'idle', 'Programma fermato'],
    ['Error', 'stop', 'Il programma non parte: controlla i blocchi'],
    ['Ready', 'idle', 'Programma pronto'],
  ] as const)('il programma «%s» è %s', (runtimeStatus, tone, label) => {
    const [, , program] = badges({ runtimeStatus });

    expect(program).toEqual({ tone, icon: expect.any(String), label });
  });

  it('la prova locale dell’alunno viene prima dello stato comandato', () => {
    const [, , program] = badges({ trialRunning: true, runtimeStatus: 'Running' });

    expect(program).toEqual({
      tone: 'work',
      icon: 'play',
      label: 'Stai provando il tuo programma',
    });
  });

  it('ogni riquadro porta un testo: il colore non è l’unica informazione', () => {
    for (const badge of badges({ teacherPaused: true })) {
      expect(badge.label.trim()).not.toBe('');
    }
  });
});
