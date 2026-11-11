import { Directive, TemplateRef, inject, input } from '@angular/core';

/**
 * Cella personalizzata di una colonna di `app-data-table` (§14.4).
 *
 * Si applica a un `<ng-template>` e dichiara per quale colonna vale. La tabella rende il
 * contenuto del modello al posto del valore testuale, passando la riga come `$implicit`:
 *
 * ```html
 * <ng-template appDataTableCell="actions" let-row>
 *   <button hlmBtn size="sm" (click)="edit(row)">Modifica</button>
 * </ng-template>
 * ```
 *
 * Serve per le celle che non sono testo — azioni, indicatori, date formattate — senza che la
 * tabella debba conoscere il dominio di chi la usa.
 */
@Directive({ selector: '[appDataTableCell]' })
export class DataTableCell {
  /** Chiave della colonna a cui il modello appartiene. */
  readonly column = input.required<string>({ alias: 'appDataTableCell' });

  /** Il modello su cui è applicata la direttiva: è il contenuto che la tabella rende. */
  readonly template = inject(TemplateRef) as TemplateRef<unknown>;
}
