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
import { exerciseTypeLabel, knownExerciseTypes } from '@exercise-engine/engine-registry';
import { ApiError, ApiService, Exercise, toApiError } from '@core/api';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialog, HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArchive,
  lucideCheck,
  lucideListChecks,
  lucidePencil,
  lucidePencilRuler,
  lucidePlus,
  lucideX,
} from '@ng-icons/lucide';
import { DataTable, type DataTableColumn } from '@shared/components/data-table/data-table';
import { DataTableCell } from '@shared/components/data-table/data-table-cell';
import { textRules } from '@shared/forms';
import { PUBLICATION_LABELS } from './publication-feedback';

/**
 * Anagrafica degli esercizi (§6.1, §10.4, §10.5, §14.1). L'esercizio è il materiale che le lezioni
 * associano nella loro sequenza: si crea, si rinomina e si archivia qui, poi si collega dalla
 * scheda «Lezioni» di un corso.
 *
 * La pagina dichiara colonne e righe e non disegna la tabella: la presenta `app-data-table`, che
 * porta con sé la paginazione e lo stato vuoto. Inserimento e modifica avvengono in un dialogo — la
 * pagina non trasforma una riga dell'elenco in un modulo — mentre l'archiviazione resta un'azione
 * esplicita con conferma, perché toglie l'esercizio dalle nuove associazioni conservandone lo
 * storico.
 *
 * Lo stato di attesa e l'esito delle chiamate non sono gestiti qui: li governano gli interceptor
 * (§14.2). Questa pagina conserva soltanto il proprio `busy`, che blocca i pulsanti mentre una
 * mutazione è in corso, e l'obbligo di rileggere dal server dopo ogni scrittura riuscita.
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
    provideIcons({
      lucidePencil,
      lucidePencilRuler,
      lucidePlus,
      lucideListChecks,
      lucideArchive,
      lucideCheck,
      lucideX,
    }),
  ],
  selector: 'app-exercise-list',
  templateUrl: './exercise-list.html',
})
export class ExerciseList implements OnInit {
  protected publicationLabel(exercise: Exercise): string {
    return PUBLICATION_LABELS[exercise.publicationStatus ?? 'Draft'];
  }
  private readonly api = inject(ApiService);

  private readonly editor = viewChild(HlmDialog);

  protected readonly exercises = signal<readonly Exercise[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<ApiError | null>(null);

  /** Modo del dialogo: decide titolo e modulo da mostrare. */
  protected readonly mode = signal<'create' | 'edit'>('create');

  protected readonly dialogTitle = computed(() =>
    this.mode() === 'create' ? 'Nuovo esercizio' : 'Modifica esercizio',
  );

  /** Colonne della tabella: l'ordine è quello di lettura, le azioni stanno a fine riga. */
  protected readonly columns: readonly DataTableColumn[] = [
    { key: 'title', label: 'Titolo' },
    { key: 'exerciseType', label: 'Tipologia' },
    { key: 'publicationStatus', label: 'Stato' },
    { key: 'description', label: 'Descrizione' },
    { key: 'actions', label: 'Azioni', align: 'end' },
  ];

  /**
   * Tipologia dell'esercizio in italiano. Il tipo si sceglie alla creazione e non si modifica, ma
   * nella griglia serve per riconoscere a colpo d'occhio che cosa contiene un titolo: senza, due
   * esercizi con lo stesso titolo restano indistinguibili.
   */
  protected readonly typeLabel = exerciseTypeLabel;

  /** Identità stabile della riga: evita di ricostruire il DOM quando l'elenco si rilegge. */
  protected readonly rowIdentity = (exercise: Exercise): string => exercise.id;

  private readonly createFormModel = signal({
    exerciseType: 'GridPath',
    title: '',
    description: '',
  });
  protected readonly createForm = form(
    this.createFormModel,
    (path) => {
      disabled(path, () => this.busy());
      textRules(path.exerciseType, 'Tipo di esercizio', 60);
      textRules(path.title, 'Titolo', 160);
      textRules(path.description, 'Descrizione', 2000, false);
    },
    {
      submission: {
        action: async () => {
          await this.create();
        },
        ignoreValidators: 'none',
        onInvalid: (field) => field().errorSummary()[0]?.fieldTree().focusBoundControl(),
      },
    },
  );

  /**
   * Tipi di esercizio che il docente può creare, nell'ordine in cui si offrono. Il tipo si sceglie
   * alla creazione e non si cambia più: un esercizio già assegnato ha versioni pubblicate che
   * dichiarano il loro tipo, e cambiarlo renderebbe l'anagrafica incoerente con quelle.
   *
   * L'elenco si legge dal **registro dei motori**, e non è scritto qui: un tipo che il registro
   * conosce deve comparire, e un tipo nuovo che qualcuno dimenticasse di aggiungere qui sarebbe un
   * esercizio che il server accetta e il docente non può creare. È il difetto che il collaudo nel
   * browser ha trovato quando il terzo motore è entrato nel registro.
   */
  protected readonly exerciseTypes = knownExerciseTypes();

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

  protected readonly editingId = signal<string | null>(null);
  protected readonly pendingArchiveId = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      this.exercises.set(
        await this.api.executeQuery<readonly Exercise[]>('ListExercises', {
          includeArchived: false,
        }),
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
    this.createForm.exerciseType().value.set('GridPath');
    this.createForm.title().value.set('');
    this.createForm.description().value.set('');
    this.createForm().reset();
  }

  /** Apre il dialogo in modifica, con i valori della riga scelta. */
  protected startEdit(exercise: Exercise): void {
    this.pendingArchiveId.set(null);
    this.mode.set('edit');
    this.editingId.set(exercise.id);
    this.editForm.title().value.set(exercise.title);
    this.editForm.description().value.set(exercise.description);
    this.editForm().reset();
  }

  protected async create(): Promise<void> {
    const title = this.createForm.title().value().trim();
    if (title === '') {
      return;
    }

    await this.mutate(async () => {
      await this.api.executeCommand<Exercise>('CreateExercise', {
        exerciseType: this.createForm.exerciseType().value(),
        title,
        description: this.createForm.description().value().trim(),
      });
      this.closeEditor();
    });
  }

  protected askArchive(exercise: Exercise): void {
    this.pendingArchiveId.set(exercise.id);
  }

  protected cancelArchive(): void {
    this.pendingArchiveId.set(null);
  }

  protected async confirmArchive(exercise: Exercise): Promise<void> {
    await this.mutate(async () => {
      await this.api.executeCommand<Exercise>('ArchiveExercise', {
        id: exercise.id,
        baseRowVersion: exercise.rowVersion,
      });
      this.pendingArchiveId.set(null);
    });
  }

  private async submitEdit(): Promise<void> {
    const title = this.editForm.title().value().trim();
    const item = this.exercises().find((candidate) => candidate.id === this.editingId());
    if (title === '' || item === undefined) {
      return;
    }

    await this.mutate(async () => {
      await this.api.executeCommand<Exercise>('UpdateExercise', {
        id: item.id,
        title,
        description: this.editForm.description().value().trim(),
        // La versione letta accompagna la modifica: il server rifiuta la scrittura su una riga
        // cambiata nel frattempo, invece di sovrascriverla (§10.2).
        baseRowVersion: item.rowVersion,
      });
      this.closeEditor();
    });
  }

  /** Chiude il dialogo e dimentica la riga in modifica: il modulo non sopravvive alla chiusura. */
  private closeEditor(): void {
    this.editingId.set(null);
    this.editor()?.close();
  }

  /** Una mutazione riuscita rilegge sempre l'elenco: è il server a dire com'è andata. */
  private async mutate(action: () => Promise<void>): Promise<void> {
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
