import { computed, Injectable, signal } from '@angular/core';

/**
 * Conteggio centralizzato delle richieste HTTP in corso (§14.2).
 *
 * Il contatore è un numero e non un booleano: due richieste sovrapposte non devono spegnere
 * l'indicatore quando finisce la prima. Si incrementa all'avvio di ogni richiesta e si decrementa
 * alla sua conclusione, esito compreso. Nessun componente lo aggiorna a mano: lo fa l'interceptor
 * `loadingInterceptor`, così una chiamata dimenticata non può restare invisibile.
 */
@Injectable({ providedIn: 'root' })
export class LoadingService {
  private readonly pending = signal(0);

  /** Vero quando almeno una richiesta è in corso. */
  readonly isLoading = computed(() => this.pending() > 0);

  /** Numero di richieste in corso: usato dai test per non dedurre lo stato dal solo booleano. */
  readonly pendingCount = computed(() => this.pending());

  begin(): void {
    this.pending.update((count) => count + 1);
  }

  end(): void {
    // La difesa non nasconde un difetto: un `end` in più non deve rendere negativo il contatore
    // e lasciare l'indicatore acceso per sempre.
    this.pending.update((count) => Math.max(0, count - 1));
  }
}
