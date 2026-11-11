import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';

/**
 * Stato vuoto uniforme delle liste dell'area docente (§14.4, §20.7).
 *
 * Un solo posto decide come si presenta un elenco senza voci, così le pagine dichiarano il testo
 * e non la struttura: prima erano paragrafi diversi per forma in ogni sezione.
 *
 * Il messaggio resta un annuncio (`role="status"`): chi usa un lettore di schermo sente che
 * l'elenco è vuoto quando il caricamento finisce, come facevano i paragrafi che questo componente
 * sostituisce.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmEmptyImports],
  selector: 'app-empty-state',
  template: `
    <div hlmEmpty role="status">
      <div hlmEmptyHeader>
        <p hlmEmptyTitle>{{ title() }}</p>
      </div>
    </div>
  `,
})
export class EmptyState {
  /** Messaggio principale: dice che cosa manca e, quando serve, come procedere. */
  readonly title = input.required<string>();
}
