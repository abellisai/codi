import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { LoadingService } from '@core/http';

/**
 * Indicatore unico delle chiamate al server in corso (§14.2, §14.4).
 *
 * È montato una volta sola, nel guscio dell'applicazione: le pagine non disegnano il proprio
 * indicatore globale, perché lo stato lo conosce soltanto il conteggio dell'interceptor. Locale
 * è invece lo stato di attesa di un singolo pulsante, che riguarda quell'azione e non il sistema.
 *
 * Con una richiesta in corso la superficie copre la pagina e intercetta il puntatore: evita che
 * un secondo clic parta su una schermata che sta già cambiando. Il testo per i lettori di schermo
 * annuncia lo stato invece di lasciare un'icona muta.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSpinner],
  selector: 'app-global-spinner',
  template: `
    @if (loading.isLoading()) {
      <div
        class="bg-background/60 fixed inset-0 z-50 flex items-center justify-center backdrop-blur-[1px]"
        role="status"
        aria-live="polite"
      >
        <hlm-spinner aria-label="Chiamata al server in corso" class="size-6" />
        <span class="sr-only">Chiamata al server in corso</span>
      </div>
    }
  `,
})
export class GlobalSpinner {
  protected readonly loading = inject(LoadingService);
}
