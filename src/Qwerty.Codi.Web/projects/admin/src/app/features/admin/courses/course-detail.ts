import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { disabled, form, FormField, FormRoot } from '@angular/forms/signals';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArchive,
  lucideBookOpen,
  lucideCheck,
  lucideListChecks,
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
import { ApiError, ApiService, Course, Lesson, toApiError } from '@core/api';
import { DataTable, type DataTableColumn } from '@shared/components/data-table/data-table';
import { DataTableCell } from '@shared/components/data-table/data-table-cell';
import { DetailHeader, type DetailCrumb } from '@shared/components/detail-header/detail-header';
import { textRules } from '@shared/forms';

/** Modo del dialogo: la riga scelta decide titolo e modulo da mostrare. */
type EditorMode = 'create' | 'edit';

/**
 * Lezioni di un corso (§6.1, §14.1, §14.4): secondo livello della navigazione della didattica.
 *
 * Il corso arriva dall'indirizzo (`/courses/:courseId`), quindi la pagina è indirizzabile e
 * ricaricabile. Da qui si scende ancora: il titolo di una lezione apre la sua sequenza di esercizi,
 * che è il terzo e ultimo livello. Il corso si rilegge dal server, non si passa dall'elenco.
 *
 * Qui si amministrano le lezioni — creazione, modifica, archiviazione — mentre titolo e descrizione
 * del corso si cambiano nell'elenco, che è la pagina dell'elemento. Un corso archiviato resta
 * leggibile e la riga di contesto lo dichiara.
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
    DetailHeader,
    FormField,
    FormRoot,
    RouterLink,
    NgIcon,
    HlmTooltipImports,
  ],
  providers: [
    provideIcons({
      lucidePencil,
      lucidePlus,
      lucideBookOpen,
      lucideListChecks,
      lucideArchive,
      lucideCheck,
      lucideX,
    }),
  ],
  selector: 'app-course-detail',
  templateUrl: './course-detail.html',
})
export class CourseDetail implements OnInit {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  private readonly editor = viewChild(HlmDialog);

  protected readonly courseId = signal<string>('');
  protected readonly course = signal<Course | null>(null);
  protected readonly lessons = signal<readonly Lesson[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<ApiError | null>(null);

  /** Modo del dialogo: decide titolo, descrizione e modulo da mostrare. */
  protected readonly mode = signal<EditorMode>('create');

  protected readonly dialogTitle = computed(() =>
    this.mode() === 'create' ? 'Nuova lezione' : 'Modifica lezione',
  );

  protected readonly dialogDescription = computed(() =>
    this.mode() === 'create'
      ? 'La lezione appartiene a questo corso; la descrizione è facoltativa.'
      : 'Il titolo e la descrizione sostituiscono quelli attuali; la sequenza degli esercizi resta.',
  );

  /** Titolo della pagina: il titolo autorevole del corso, letto dal server. */
  protected readonly title = computed(() => this.course()?.title ?? 'Corso');

  /** Percorso: l'elenco dei corsi e il corso che si sta leggendo. */
  protected readonly crumbs = computed<readonly DetailCrumb[]>(() => [
    { label: 'Corsi', link: ['/courses'] },
    { label: this.title() },
  ]);

  /** Riga di contesto: quante lezioni contiene, la descrizione e lo stato di archiviazione. */
  protected readonly subtitle = computed(() => {
    const count = this.lessons().length;
    const parts = [count === 1 ? '1 lezione' : `${count} lezioni`];
    const description = this.course()?.description ?? '';

    if (description !== '') {
      parts.push(description);
    }

    if (this.course()?.isArchived === true) {
      parts.push('corso archiviato');
    }

    return parts.join(' · ');
  });

  /** Colonne dell'elenco: il titolo è il collegamento alla lezione, le azioni stanno a fine riga. */
  protected readonly columns: readonly DataTableColumn[] = [
    { key: 'title', label: 'Titolo' },
    { key: 'description', label: 'Descrizione' },
    { key: 'actions', label: 'Azioni', align: 'end' },
  ];

  /** Identità stabile delle righe: evita di ricostruire il DOM quando l'elenco si rilegge. */
  protected readonly identity = (lesson: Lesson): string => lesson.id;

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
          await this.createLesson();
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

  ngOnInit(): void {
    // L'indirizzo è l'unica sorgente del corso mostrato: la sottoscrizione resta attiva per
    // rispondere anche ai movimenti avanti e indietro del browser.
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((parameters) => {
      this.courseId.set(parameters.get('courseId') ?? '');
      void this.reload();
    });
  }

  protected async reload(): Promise<void> {
    const id = this.courseId();
    if (id === '') {
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    try {
      this.course.set(await this.api.executeQuery<Course>('GetCourse', { id }));
      await this.loadLessons(id);
    } catch (failure) {
      // Un corso cancellato o un indirizzo sbagliato non lasciano la pagina a metà: si svuota
      // quello che si è letto e si mostra l'errore, con il ritorno all'elenco sempre disponibile.
      this.course.set(null);
      this.lessons.set([]);
      this.error.set(toApiError(failure));
    } finally {
      this.loading.set(false);
    }
  }

  /** Apre il dialogo in creazione di lezione, con il modulo vuoto. */
  protected startCreate(): void {
    this.pendingArchiveId.set(null);
    this.mode.set('create');
    this.createForm.title().value.set('');
    this.createForm.description().value.set('');
    this.createForm().reset();
  }

  /** Apre il dialogo in modifica di lezione, con i valori della riga scelta. */
  protected startEdit(lesson: Lesson): void {
    this.pendingArchiveId.set(null);
    this.mode.set('edit');
    this.editingId.set(lesson.id);
    this.editForm.title().value.set(lesson.title);
    this.editForm.description().value.set(lesson.description);
    this.editForm().reset();
  }

  protected async createLesson(): Promise<void> {
    const title = this.createForm.title().value().trim();
    if (this.courseId() === '' || title === '') {
      return;
    }

    await this.run(async () => {
      await this.api.executeCommand<Lesson>('CreateLesson', {
        courseId: this.courseId(),
        title,
        description: this.createForm.description().value().trim(),
      });
      this.closeEditor();
    });
  }

  protected askArchive(lesson: Lesson): void {
    this.pendingArchiveId.set(lesson.id);
  }

  protected cancelArchive(): void {
    this.pendingArchiveId.set(null);
  }

  protected async confirmArchive(lesson: Lesson): Promise<void> {
    await this.run(async () => {
      await this.api.executeCommand<Lesson>('ArchiveLesson', {
        id: lesson.id,
        baseRowVersion: lesson.rowVersion,
      });
      this.pendingArchiveId.set(null);
    });
  }

  private async submitEdit(): Promise<void> {
    const lesson = this.lessons().find((item) => item.id === this.editingId());
    const title = this.editForm.title().value().trim();
    if (lesson === undefined || title === '') {
      return;
    }

    await this.run(async () => {
      await this.api.executeCommand<Lesson>('UpdateLesson', {
        id: lesson.id,
        title,
        description: this.editForm.description().value().trim(),
        baseRowVersion: lesson.rowVersion,
      });
      this.closeEditor();
    });
  }

  private async loadLessons(courseId: string): Promise<void> {
    this.lessons.set(
      await this.api.executeQuery<readonly Lesson[]>('ListLessons', {
        courseId,
        includeArchived: false,
      }),
    );
  }

  /** Chiude il dialogo e dimentica la riga in lavorazione: il modulo non sopravvive alla chiusura. */
  private closeEditor(): void {
    this.editingId.set(null);
    this.editor()?.close();
  }

  /** Esegue una mutazione e rilegge quello che la pagina mostra, che è la fonte autorevole. */
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
