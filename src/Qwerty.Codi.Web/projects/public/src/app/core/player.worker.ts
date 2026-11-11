import { GridPathPlayerRuntime } from '@exercise-engine/grid-path-player';
import type { PlayerEvent, PlayerRequest } from '@exercise-engine/player-protocol';

/**
 * Worker del player (§13.6). È un adattatore sottile: costruisce la macchina del player e la collega
 * al canale del worker. Tutta la logica — compilazione, budget, permesso, checkpoint — vive nella
 * macchina, che è la stessa provata dai test; qui restano soltanto il ciclo dei passi e i due
 * `postMessage`.
 *
 * Il worker non fa chiamate di rete e non tocca le credenziali: riceve il progetto, la definizione e
 * i permessi già verificati dal lato pagina. La pagina, dal canto suo, ignora i messaggi di un
 * worker sostituito grazie all'identificativo dell'istanza.
 */

/** Distanza fra due passi didattici: i passi restano osservabili (§13.6). */
const STEP_DELAY_MS = 300;

interface WorkerScope {
  onmessage: ((event: MessageEvent<PlayerRequest>) => void) | null;
  postMessage: (message: PlayerEvent) => void;
}

/**
 * Il modulo esegue solo dentro un worker vero. Se venisse importato altrove — per esempio da un test
 * che risolve l'URL del worker senza avviarlo — non deve toccare `onmessage` della pagina. In un
 * worker `window` non esiste: è la differenza che distingue i due ambienti senza toccare il DOM.
 */
const scope = self as unknown as WorkerScope;
const inWorker = typeof window === 'undefined';

if (inWorker) {
  let timer: ReturnType<typeof setTimeout> | null = null;

  // L'identità dell'istanza arriva con il primo `LoadProgram` della pagina: il worker la adotta,
  // così i suoi eventi la ripetono e la pagina riconosce soltanto i messaggi della propria corsa.
  const runtime = new GridPathPlayerRuntime((event) => scope.postMessage(event), {});

  /** Il ciclo dei passi gira solo mentre la corsa è in esecuzione: da fermo non consuma un timer. */
  const schedule = (): void => {
    if (runtime.runtimeStatus !== 'Running') {
      if (timer !== null) clearTimeout(timer);
      timer = null;

      return;
    }

    if (timer !== null) return;
    timer = setTimeout(() => {
      timer = null;
      runtime.pump();
      schedule();
    }, STEP_DELAY_MS);
  };

  scope.onmessage = (event: MessageEvent<PlayerRequest>) => {
    runtime.handle(event.data);
    schedule();
  };
}
