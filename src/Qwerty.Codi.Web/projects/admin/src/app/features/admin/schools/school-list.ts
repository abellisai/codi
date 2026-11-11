import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { disabled, form, FormField, FormRoot } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArchive,
  lucideCheck,
  lucidePencil,
  lucidePlus,
  lucideUsers,
  lucideX,
} from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialog, HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { ApiError, ApiService, School, toApiError } from '@core/api';
import { DataTable, type DataTableColumn } from '@shared/components/data-table/data-table';
import { DataTableCell } from '@shared/components/data-table/data-table-cell';
import { textRules } from '@shared/forms';

/** Modo del dialogo: la riga scelta decide titolo e modulo da mostrare. */
type EditorMode = 'create' | 'edit';

/**
 * Elenco delle scuole (§6.1, §14.1, §14.4): il primo livello della navigazione dell'area.
 *
 * La pagina mostra le scuole e **porta** a quella scelta: il nome della riga è il collegamento a
 * `/schools/:schoolId`, dove si gestiscono le classi. Il modello è lo stesso in tutta l'area —
 * l'elenco amministra l'elemento (creazione, rinomina, archiviazione), la pagina di dettaglio
 * amministra i suoi dipendenti — così ogni pagina ha un solo insieme di azioni.
 *
 * Dopo ogni modifica rilegge l'elenco dal server, che è l'unica fonte autorevole; l'archiviazione
 * resta un'azione esplicita con conferma, perché conserva lo storico. Lo stato di attesa e l'esito
 * delle chiamate non sono gestiti qui: li governano gli interceptor (§14.2).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmButtonImports,
    HlmInputImports,
    HlmFieldImports,
    HlmDialogImports,
    DataTable,
    DataTableCell,
    FormField,
    FormRoot,
    RouterLink,
    NgIcon,
    HlmTooltipImports,
  ],
  providers: [
    provideIcons({ lucidePencil, lucidePlus, lucideArchive, lucideCheck, lucideUsers, lucideX }),
  ],
  selector: 'app-school-list',
  styleUrl: './school-list.css',
  templateUrl: './school-list.html',
})
export class SchoolList implements OnInit {
  private readonly api = inject(ApiService);

  private readonly editor = viewChild(HlmDialog);

  protected readonly schools = signal<readonly School[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<ApiError | null>(null);

  /** Modo del dialogo: decide titolo, descrizione e modulo da mostrare. */
  protected readonly mode = signal<EditorMode>('create');

  protected readonly dialogTitle = computed(() =>
    this.mode() === 'create' ? 'Nuova scuola' : 'Rinomina scuola',
  );

  protected readonly dialogDescription = computed(() =>
    this.mode() === 'create'
      ? 'Il nome distingue la scuola nell’elenco.'
      : 'Il nuovo nome sostituisce quello attuale; le classi collegate restano invariate.',
  );

  /** Colonne dell'elenco: il nome è il collegamento alla scuola, le azioni stanno a fine riga. */
  protected readonly columns: readonly DataTableColumn[] = [
    { key: 'name', label: 'Scuola' },
    { key: 'actions', label: 'Azioni', align: 'end' },
  ];

  /** Identità stabile delle righe: evita di ricostruire il DOM quando l'elenco si rilegge. */
  protected readonly identity = (school: School): string => school.id;

  private readonly editingId = signal<string | null>(null);
  protected readonly pendingArchiveId = signal<string | null>(null);

  private readonly createFormModel = signal({ name: '' });
  protected readonly createForm = form(
    this.createFormModel,
    (path) => {
      disabled(path, () => this.busy());
      textRules(path.name, 'Nome della scuola', 120);
    },
    {
      submission: {
        action: async () => {
          await this.createSchool();
        },
        ignoreValidators: 'none',
        onInvalid: (field) => field().errorSummary()[0]?.fieldTree().focusBoundControl(),
      },
    },
  );

  private readonly editFormModel = signal({ name: '' });
  protected readonly editForm = form(
    this.editFormModel,
    (path) => {
      disabled(path, () => this.busy());
      textRules(path.name, 'Nome della scuola', 120);
    },
    {
      submission: {
        action: async () => {
          await this.submitRename();
        },
        ignoreValidators: 'none',
        onInvalid: (field) => field().errorSummary()[0]?.fieldTree().focusBoundControl(),
      },
    },
  );

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      this.schools.set(
        await this.api.executeQuery<readonly School[]>('ListSchools', { includeArchived: false }),
      );
    } catch (failure) {
      this.error.set(toApiError(failure));
    } finally {
      this.loading.set(false);
    }
  }

  /** Apre il dialogo in creazione, con il modulo vuoto. */
  protected startCreate(): void {
    this.pendingArchiveId.set(null);
    this.mode.set('create');
    this.createForm.name().value.set('');
    this.createForm().reset();
  }

  /** Apre il dialogo in rinomina, con il nome della riga scelta. */
  protected startEdit(school: School): void {
    this.pendingArchiveId.set(null);
    this.mode.set('edit');
    this.editingId.set(school.id);
    this.editForm.name().value.set(school.name);
    this.editForm().reset();
  }

  protected async createSchool(): Promise<void> {
    const name = this.createForm.name().value().trim();
    if (name === '') {
      return;
    }

    await this.run(async () => {
      await this.api.executeCommand<School>('CreateSchool', { name });
      this.closeEditor();
    });
  }

  protected askArchive(school: School): void {
    this.pendingArchiveId.set(school.id);
  }

  protected cancelArchive(): void {
    this.pendingArchiveId.set(null);
  }

  protected async confirmArchive(school: School): Promise<void> {
    await this.run(async () => {
      await this.api.executeCommand<School>('ArchiveSchool', {
        id: school.id,
        baseRowVersion: school.rowVersion,
      });
      this.pendingArchiveId.set(null);
    });
  }

  private async submitRename(): Promise<void> {
    const school = this.schools().find((item) => item.id === this.editingId());
    const name = this.editForm.name().value().trim();
    if (school === undefined || name === '') {
      return;
    }

    await this.run(async () => {
      await this.api.executeCommand<School>('UpdateSchool', {
        id: school.id,
        name,
        // La versione letta accompagna la modifica: il server rifiuta la scrittura su una riga
        // cambiata nel frattempo, invece di sovrascriverla (§10.2).
        baseRowVersion: school.rowVersion,
      });
      this.closeEditor();
    });
  }

  /** Chiude il dialogo e dimentica la riga in lavorazione: il modulo non sopravvive alla chiusura. */
  private closeEditor(): void {
    this.editingId.set(null);
    this.editor()?.close();
  }

  /** Esegue una mutazione e rilegge l'elenco, che è la fonte autorevole. */
  private async run(action: () => Promise<void>): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      await action();
      await this.reload();
    } catch (failure) {
      this.error.set(toApiError(failure));
    } finally {
      this.busy.set(false);
    }
  }
}
