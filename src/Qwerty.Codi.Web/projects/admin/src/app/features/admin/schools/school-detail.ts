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
import { ApiError, ApiService, Classroom, School, toApiError } from '@core/api';
import { DataTable, type DataTableColumn } from '@shared/components/data-table/data-table';
import { DataTableCell } from '@shared/components/data-table/data-table-cell';
import { DetailHeader, type DetailCrumb } from '@shared/components/detail-header/detail-header';
import { schoolYearRules, textRules } from '@shared/forms';

/** Modo del dialogo: la riga scelta decide titolo e modulo da mostrare. */
type EditorMode = 'create' | 'edit';

/**
 * Classi di una scuola (§6.1, §14.1, §14.4): secondo livello della navigazione dell'area.
 *
 * La scuola arriva dall'indirizzo (`/schools/:schoolId`), quindi la pagina è indirizzabile e
 * ricaricabile: la stessa destinazione si ottiene dal collegamento nell'elenco, da un segnalibro o
 * dal tasto indietro del browser. La scuola si rilegge dal server — non si passa dall'elenco — così
 * il nome e la versione mostrati sono sempre quelli autorevoli.
 *
 * Qui si amministrano soltanto i dipendenti: creazione, modifica e archiviazione delle classi.
 * Creazione, rinomina e archiviazione della scuola restano nell'elenco, che è la pagina
 * dell'elemento. Una scuola archiviata resta leggibile: esce dagli elenchi ordinari, e la riga di
 * contesto lo dichiara invece di far credere che sia ancora attiva.
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
      lucideUsers,
      lucideArchive,
      lucideCheck,
      lucideX,
    }),
  ],
  selector: 'app-school-detail',
  templateUrl: './school-detail.html',
})
export class SchoolDetail implements OnInit {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  private readonly editor = viewChild(HlmDialog);

  private readonly schoolId = signal<string>('');
  protected readonly school = signal<School | null>(null);
  protected readonly classrooms = signal<readonly Classroom[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<ApiError | null>(null);

  /** Modo del dialogo: decide titolo, descrizione e modulo da mostrare. */
  protected readonly mode = signal<EditorMode>('create');

  protected readonly dialogTitle = computed(() =>
    this.mode() === 'create' ? 'Nuova classe' : 'Modifica classe',
  );

  protected readonly dialogDescription = computed(() =>
    this.mode() === 'create'
      ? 'La classe appartiene a questa scuola e a un anno scolastico.'
      : 'Correggi il nome o l’anno scolastico della classe.',
  );

  /** Titolo della pagina: il nome autorevole della scuola, letto dal server. */
  protected readonly title = computed(() => this.school()?.name ?? 'Scuola');

  /** Percorso: l'elenco delle scuole e la scuola che si sta leggendo. */
  protected readonly crumbs = computed<readonly DetailCrumb[]>(() => [
    { label: 'Scuole', link: ['/schools'] },
    { label: this.title() },
  ]);

  /** Riga di contesto: quante classi contiene e se la scuola è archiviata. */
  protected readonly subtitle = computed(() => {
    const count = this.classrooms().length;
    const classes = count === 1 ? '1 classe' : `${count} classi`;

    return this.school()?.isArchived === true ? `${classes} · scuola archiviata` : classes;
  });

  /** Colonne dell'elenco delle classi: le azioni stanno a fine riga. */
  protected readonly columns: readonly DataTableColumn[] = [
    { key: 'name', label: 'Classe' },
    { key: 'schoolYear', label: 'Anno scolastico' },
    { key: 'actions', label: 'Azioni', align: 'end' },
  ];

  /** Identità stabile delle righe: evita di ricostruire il DOM quando l'elenco si rilegge. */
  protected readonly identity = (classroom: Classroom): string => classroom.id;

  private readonly editingId = signal<string | null>(null);
  protected readonly pendingArchiveId = signal<string | null>(null);

  private readonly createFormModel = signal({ name: '', schoolYear: DefaultSchoolYear() });
  protected readonly createForm = form(
    this.createFormModel,
    (path) => {
      disabled(path, () => this.busy());
      textRules(path.name, 'Nome della classe', 120);
      schoolYearRules(path.schoolYear);
    },
    {
      submission: {
        action: async () => {
          await this.createClassroom();
        },
        ignoreValidators: 'none',
        onInvalid: (field) => field().errorSummary()[0]?.fieldTree().focusBoundControl(),
      },
    },
  );

  private readonly editFormModel = signal({ name: '', schoolYear: '' });
  protected readonly editForm = form(
    this.editFormModel,
    (path) => {
      disabled(path, () => this.busy());
      textRules(path.name, 'Nome della classe', 120);
      schoolYearRules(path.schoolYear);
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
    // L'indirizzo è l'unica sorgente della scuola mostrata: la sottoscrizione resta attiva per
    // rispondere anche ai movimenti avanti e indietro del browser.
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((parameters) => {
      this.schoolId.set(parameters.get('schoolId') ?? '');
      void this.reload();
    });
  }

  protected async reload(): Promise<void> {
    const id = this.schoolId();
    if (id === '') {
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    try {
      this.school.set(await this.api.executeQuery<School>('GetSchool', { id }));
      await this.loadClassrooms(id);
    } catch (failure) {
      // Una scuola cancellata o un indirizzo sbagliato non lasciano la pagina a metà: si svuota
      // quello che si è letto e si mostra l'errore, con il ritorno all'elenco sempre disponibile.
      this.school.set(null);
      this.classrooms.set([]);
      this.error.set(toApiError(failure));
    } finally {
      this.loading.set(false);
    }
  }

  /** Apre il dialogo in creazione di classe, con il modulo vuoto. */
  protected startCreate(): void {
    this.pendingArchiveId.set(null);
    this.mode.set('create');
    this.createForm.name().value.set('');
    this.createForm.schoolYear().value.set(DefaultSchoolYear());
    this.createForm().reset();
  }

  /** Apre il dialogo in modifica di classe, con i valori della riga scelta. */
  protected startEdit(classroom: Classroom): void {
    this.pendingArchiveId.set(null);
    this.mode.set('edit');
    this.editingId.set(classroom.id);
    this.editForm.name().value.set(classroom.name);
    this.editForm.schoolYear().value.set(classroom.schoolYear);
    this.editForm().reset();
  }

  protected async createClassroom(): Promise<void> {
    const name = this.createForm.name().value().trim();
    const schoolYear = this.createForm.schoolYear().value().trim();
    if (this.schoolId() === '' || name === '' || schoolYear === '') {
      return;
    }

    await this.run(async () => {
      await this.api.executeCommand<Classroom>('CreateClassroom', {
        schoolId: this.schoolId(),
        name,
        schoolYear,
      });
      this.closeEditor();
    });
  }

  protected askArchive(classroom: Classroom): void {
    this.pendingArchiveId.set(classroom.id);
  }

  protected cancelArchive(): void {
    this.pendingArchiveId.set(null);
  }

  protected async confirmArchive(classroom: Classroom): Promise<void> {
    await this.run(async () => {
      await this.api.executeCommand<Classroom>('ArchiveClassroom', {
        id: classroom.id,
        baseRowVersion: classroom.rowVersion,
      });
      this.pendingArchiveId.set(null);
    });
  }

  private async submitEdit(): Promise<void> {
    const classroom = this.classrooms().find((item) => item.id === this.editingId());
    const name = this.editForm.name().value().trim();
    const schoolYear = this.editForm.schoolYear().value().trim();
    if (classroom === undefined || name === '' || schoolYear === '') {
      return;
    }

    await this.run(async () => {
      await this.api.executeCommand<Classroom>('UpdateClassroom', {
        id: classroom.id,
        name,
        schoolYear,
        baseRowVersion: classroom.rowVersion,
      });
      this.closeEditor();
    });
  }

  private async loadClassrooms(schoolId: string): Promise<void> {
    this.classrooms.set(
      await this.api.executeQuery<readonly Classroom[]>('ListClassrooms', {
        schoolId,
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

/**
 * Anno scolastico proposto nella maschera: quello in corso secondo il calendario italiano, che
 * comincia a settembre. È un valore predefinito modificabile, non una regola del contratto.
 */
function DefaultSchoolYear(today = new Date()): string {
  const year = today.getMonth() >= 8 ? today.getFullYear() : today.getFullYear() - 1;

  return `${year}/${year + 1}`;
}
