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
  lucideBookOpen,
  lucideCheck,
  lucidePencil,
  lucidePlus,
  lucideX,
} from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialog, HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { ApiError, ApiService, Course, toApiError } from '@core/api';
import { DataTable, type DataTableColumn } from '@shared/components/data-table/data-table';
import { DataTableCell } from '@shared/components/data-table/data-table-cell';
import { textRules } from '@shared/forms';

/** Modo del dialogo: la riga scelta decide titolo e modulo da mostrare. */
type EditorMode = 'create' | 'edit';

/**
 * Elenco dei corsi (§6.1, §14.1, §14.4): primo livello della navigazione della didattica.
 *
 * La pagina mostra i corsi e **porta** a quello scelto: il titolo della riga è il collegamento a
 * `/courses/:courseId`, dove si gestiscono le lezioni, e da lì si scende alla sequenza degli
 * esercizi di ogni lezione. Lo stesso modello vale per le scuole e le loro classi: l'elenco
 * amministra l'elemento, la pagina di dettaglio amministra i suoi dipendenti.
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
    HlmTextareaImports,
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
    provideIcons({ lucidePencil, lucidePlus, lucideBookOpen, lucideArchive, lucideCheck, lucideX }),
  ],
  selector: 'app-course-list',
  styleUrl: './course-list.css',
  templateUrl: './course-list.html',
})
export class CourseList implements OnInit {
  private readonly api = inject(ApiService);

  private readonly editor = viewChild(HlmDialog);

  protected readonly courses = signal<readonly Course[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<ApiError | null>(null);

  /** Modo del dialogo: decide titolo, descrizione e modulo da mostrare. */
  protected readonly mode = signal<EditorMode>('create');

  protected readonly dialogTitle = computed(() =>
    this.mode() === 'create' ? 'Nuovo corso' : 'Modifica corso',
  );

  protected readonly dialogDescription = computed(() =>
    this.mode() === 'create'
      ? 'Il titolo distingue il corso nell’elenco; la descrizione è facoltativa.'
      : 'Il titolo e la descrizione sostituiscono quelli attuali; le lezioni collegate restano invariate.',
  );

  /** Colonne dell'elenco: il titolo è il collegamento al corso, le azioni stanno a fine riga. */
  protected readonly columns: readonly DataTableColumn[] = [
    { key: 'title', label: 'Titolo' },
    { key: 'description', label: 'Descrizione' },
    { key: 'actions', label: 'Azioni', align: 'end' },
  ];

  /** Identità stabile delle righe: evita di ricostruire il DOM quando l'elenco si rilegge. */
  protected readonly identity = (course: Course): string => course.id;

  private readonly editingId = signal<string | null>(null);
  protected readonly pendingArchiveId = signal<string | null>(null);

  private readonly createFormModel = signal({ title: '', description: '' });
  protected readonly createForm = form(
    this.createFormModel,
    (path) => {
      disabled(path, () => this.busy());
      textRules(path.title, 'Titolo', 160);
      textRules(path.description, 'Descrizione', 2000, false);
    },
    {
      submission: {
        action: async () => {
          await this.createCourse();
        },
        ignoreValidators: 'none',
        onInvalid: (field) => field().errorSummary()[0]?.fieldTree().focusBoundControl(),
      },
    },
  );

  private readonly editFormModel = signal({ title: '', description: '' });
  protected readonly editForm = form(
    this.editFormModel,
    (path) => {
      disabled(path, () => this.busy());
      textRules(path.title, 'Titolo', 160);
      textRules(path.description, 'Descrizione', 2000, false);
    },
    {
      submission: {
        action: async () => {
          await this.submitEdit();
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
      this.courses.set(
        await this.api.executeQuery<readonly Course[]>('ListCourses', { includeArchived: false }),
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
    this.createForm.title().value.set('');
    this.createForm.description().value.set('');
    this.createForm().reset();
  }

  /** Apre il dialogo in modifica, con i valori della riga scelta. */
  protected startEdit(course: Course): void {
    this.pendingArchiveId.set(null);
    this.mode.set('edit');
    this.editingId.set(course.id);
    this.editForm.title().value.set(course.title);
    this.editForm.description().value.set(course.description);
    this.editForm().reset();
  }

  protected async createCourse(): Promise<void> {
    const title = this.createForm.title().value().trim();
    if (title === '') {
      return;
    }

    await this.run(async () => {
      await this.api.executeCommand<Course>('CreateCourse', {
        title,
        description: this.createForm.description().value().trim(),
      });
      this.closeEditor();
    });
  }

  protected askArchive(course: Course): void {
    this.pendingArchiveId.set(course.id);
  }

  protected cancelArchive(): void {
    this.pendingArchiveId.set(null);
  }

  protected async confirmArchive(course: Course): Promise<void> {
    await this.run(async () => {
      await this.api.executeCommand<Course>('ArchiveCourse', {
        id: course.id,
        baseRowVersion: course.rowVersion,
      });
      this.pendingArchiveId.set(null);
    });
  }

  private async submitEdit(): Promise<void> {
    const course = this.courses().find((item) => item.id === this.editingId());
    const title = this.editForm.title().value().trim();
    if (course === undefined || title === '') {
      return;
    }

    await this.run(async () => {
      await this.api.executeCommand<Course>('UpdateCourse', {
        id: course.id,
        title,
        description: this.editForm.description().value().trim(),
        // La versione letta accompagna la modifica: il server rifiuta la scrittura su una riga
        // cambiata nel frattempo, invece di sovrascriverla (§10.2).
        baseRowVersion: course.rowVersion,
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
