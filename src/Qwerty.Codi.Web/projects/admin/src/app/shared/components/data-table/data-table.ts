import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  TemplateRef,
  computed,
  contentChildren,
  input,
  signal,
} from '@angular/core';
import { HlmNumberedPagination } from '@spartan-ng/helm/pagination';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { EmptyState } from '../empty-state/empty-state';
import { DataTableCell } from './data-table-cell';

/** Descrizione di una colonna: la tabella non deduce nulla dai dati delle righe. */
export interface DataTableColumn {
  /** Chiave della proprietà nella riga; identifica anche il modello di cella. */
  readonly key: string;
  /** Intestazione visibile. */
  readonly label: string;
  /** Allineamento del contenuto: i numeri e le azioni stanno a fine riga. */
  readonly align?: 'start' | 'end';
}

/**
 * Tabella dati dell'area docente (§14.1, §14.4): intestazioni, righe e paginazione.
 *
 * Una sola implementazione per tutte le liste dell'admin: le pagine dichiarano colonne e righe e
 * non ricostruiscono una tabella a mano. La paginazione è sempre presente — una classe ha 12
 * alunni ma una libreria di esercizi no — e il numero di righe per pagina resta scelto da chi
 * legge. Le celle che non sono testo passano da un modello `appDataTableCell`, così le azioni
 * restano nella pagina che le sa eseguire.
 *
 * L'elenco vuoto non è una tabella senza righe: diventa lo stato vuoto condiviso, perché
 * un'intestazione senza contenuto non dice che cosa manca.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmTableImports, HlmNumberedPagination, NgTemplateOutlet, EmptyState],
  selector: 'app-data-table',
  template: `
    @if (rows().length === 0) {
      <app-empty-state [title]="emptyMessage()" />
    } @else {
      <div hlmTableContainer>
        <!-- Righe compatte: le celle perdono il padding verticale pieno e l'intestazione la sua
             altezza fissa, così un elenco di configurazione resta leggibile senza occupare
             tutta la pagina. -->
        <table hlmTable class="[&_td]:py-1 [&_th]:h-9" [attr.aria-label]="label()">
          <thead hlmTHead>
            <tr hlmTr>
              @for (column of columns(); track column.key) {
                <th hlmTh [class.text-end]="column.align === 'end'">{{ column.label }}</th>
              }
            </tr>
          </thead>
          <tbody hlmTBody>
            @for (row of pageRows(); track trackKey($index, row)) {
              <tr hlmTr>
                @for (column of columns(); track column.key) {
                  <td hlmTd [class.text-end]="column.align === 'end'">
                    @if (cellTemplate(column.key); as cell) {
                      <ng-container
                        [ngTemplateOutlet]="cell"
                        [ngTemplateOutletContext]="{ $implicit: row }"
                      />
                    } @else {
                      {{ value(row, column.key) }}
                    }
                  </td>
                }
              </tr>
            }
          </tbody>
        </table>
      </div>

      <hlm-numbered-pagination
        [(currentPage)]="page"
        [(itemsPerPage)]="pageSize"
        [totalItems]="rows().length"
        [pageSizes]="pageSizes()"
      />
    }
  `,
})
export class DataTable<TRow extends object = Record<string, unknown>> implements OnInit {
  /** Colonne nell'ordine di visualizzazione. */
  readonly columns = input.required<readonly DataTableColumn[]>();

  /** Righe da mostrare: la tabella non filtra, mostra ciò che la pagina le consegna. */
  readonly rows = input.required<readonly TRow[]>();

  /** Etichetta accessibile della tabella: senza, un lettore annuncia solo «tabella». */
  readonly label = input.required<string>();

  /** Messaggio dello stato vuoto. */
  readonly emptyMessage = input('Nessun elemento.');

  /** Scelte di righe per pagina offerte all'utente. */
  readonly pageSizes = input<number[]>([10, 20, 50]);

  /** Identità stabile della riga: evita di ricostruire il DOM quando l'elenco si rilegge. */
  readonly rowIdentity = input<(row: TRow) => string>();

  /**
   * Righe per pagina all'avvio. Resta 10 per gli elenchi di configurazione; una griglia che sa
   * quante righe contiene al massimo (una classe ha 12 alunni) lo dichiara, così non paginare
   * nasconde parte del gruppo che il docente sta guardando.
   */
  readonly initialPageSize = input(10);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);

  ngOnInit(): void {
    const size = this.initialPageSize();
    if (size > 0) {
      this.pageSize.set(size);
    }
  }

  private readonly cells = contentChildren(DataTableCell);

  /** Riga corrente: la paginazione si applica qui e non nella pagina che ospita la tabella. */
  protected readonly pageRows = computed(() => {
    const size = this.pageSize();
    const start = (this.page() - 1) * size;

    return this.rows().slice(start, start + size);
  });

  /** Modello di cella dichiarato dalla pagina, se esiste per quella colonna. */
  protected cellTemplate(key: string): TemplateRef<unknown> | null {
    return this.cells().find((cell) => cell.column() === key)?.template ?? null;
  }

  /** Valore testuale della colonna, quando non c'è un modello. */
  protected value(row: TRow, key: string): unknown {
    return (row as Record<string, unknown>)[key];
  }

  /** Chiave di tracciamento: l'identità dichiarata, altrimenti la posizione nella pagina. */
  protected trackKey(index: number, row: TRow): string {
    const identity = this.rowIdentity();

    return identity ? identity(row) : String(index);
  }
}
