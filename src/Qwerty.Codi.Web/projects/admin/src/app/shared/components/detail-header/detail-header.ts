import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { NgIcon } from '@ng-icons/core';
import { HlmBreadcrumbImports } from '@spartan-ng/helm/breadcrumb';

/** Livello del percorso mostrato dall'intestazione: l'ultimo è la pagina corrente. */
export interface DetailCrumb {
  /** Testo del livello: il nome dell'elenco o dell'elemento. */
  readonly label: string;
  /** Destinazione del livello; assente sull'ultimo, che è la pagina che si sta leggendo. */
  readonly link?: readonly string[];
}

/**
 * Intestazione di una pagina di dettaglio dell'area docente (§14.1, §14.4).
 *
 * L'area segue un solo modello di navigazione: ogni elenco porta alla pagina dell'elemento scelto,
 * che mostra i suoi elementi dipendenti. Questa intestazione rende quel modello riconoscibile con un
 * **percorso** che nomina tutti i livelli attraversati: si vede dove si è, da dove si arriva e come
 * risalire di un gradino, non solo come tornare indietro. Sostituisce il ritorno «← Scuole», che
 * mostrava un livello solo.
 *
 * Il titolo porta l'icona del livello, la stessa della voce di menu e del pulsante che porta qui. Le
 * azioni della pagina entrano per proiezione: il componente possiede la struttura, non i pulsanti.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmBreadcrumbImports],
  selector: 'app-detail-header',
  template: `
    <header class="mb-4 flex flex-wrap items-end justify-between gap-4">
      <div class="flex flex-col gap-1">
        <nav hlmBreadcrumb [aria-label]="'Percorso'">
          <ol hlmBreadcrumbList>
            @for (crumb of crumbs(); track $index; let last = $last) {
              <li hlmBreadcrumbItem>
                @if (crumb.link; as link) {
                  <a hlmBreadcrumbLink [link]="link">{{ crumb.label }}</a>
                } @else {
                  <span hlmBreadcrumbPage>{{ crumb.label }}</span>
                }
              </li>
              @if (!last) {
                <li hlmBreadcrumbSeparator></li>
              }
            }
          </ol>
        </nav>

        <!-- Il titolo dice dove si è; l'icona è la stessa che identifica il livello nella voce di
             menu e nel pulsante che porta qui, quindi non introduce un simbolo nuovo. -->
        <h1 class="page-title my-0">
          <ng-icon [name]="icon()" aria-hidden="true" />
          {{ title() }}
        </h1>

        @if (subtitle(); as text) {
          <p class="text-muted-foreground text-sm">{{ text }}</p>
        }
      </div>

      <div class="flex flex-wrap items-center gap-2">
        <ng-content />
      </div>
    </header>
  `,
})
export class DetailHeader {
  /** Livelli dalla radice alla pagina corrente: l'ultimo è il titolo di questa pagina. */
  readonly crumbs = input.required<readonly DetailCrumb[]>();

  /** Nome dell'elemento mostrato: è il titolo della pagina. */
  readonly title = input.required<string>();

  /**
   * Icona Lucide del livello mostrato. La registra la pagina che usa il componente: le icone sono
   * disponibili anche ai discendenti, quindi il titolo non duplica la registrazione.
   */
  readonly icon = input.required<string>();

  /** Riga di contesto sotto il titolo, quando serve dire che cosa contiene la pagina. */
  readonly subtitle = input('');
}
