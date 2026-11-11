import {
  CdkDrag,
  CdkDragDrop,
  CdkDragHandle,
  CdkDropList,
  moveItemInArray,
} from '@angular/cdk/drag-drop';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { disabled, form, FormField, FormRoot, required } from '@angular/forms/signals';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArchive, lucideGripVertical } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import {
  ApiError,
  ApiService,
  Course,
  Exercise,
  Lesson,
  LessonExercise,
  LessonExerciseInput,
  toApiError,
} from '@core/api';
import { DetailHeader, type DetailCrumb } from '@shared/components/detail-header/detail-header';
import { EmptyState } from '@shared/components/empty-state/empty-state';

/**
 * Riga della bozza: l'identificativo dell'esercizio dice che cosa si associa, la chiave dice
 * **quale riga** è. Servono entrambe perché lo stesso esercizio può comparire due volte nella
 * sequenza: senza chiave stabile Angular ricostruirebbe le righe a ogni spostamento e il
 * trascinamento perderebbe il riferimento all'elemento mosso.
 */
interface SequenceRow {
  readonly key: string;
  readonly exerciseId: string;
}

/**
 * Sequenza degli esercizi di una lezione (§6.1, §10.4, §14.1): terzo livello della navigazione.
 *
 * È il fondo del percorso Scuole → Classi, Corsi → Lezioni → Esercizi: qui non si scende oltre, si
 * compone. La lezione arriva dall'indirizzo (`/courses/:courseId/lessons/:lessonId`) e si rilegge
 * dal server, perché la sostituzione della sequenza si invia con la **versione della lezione**.
 *
 * L'ordine si cambia **trascinando la riga** per la sua maniglia; la stessa modifica si ottiene da
 * tastiera con le frecce su e giù mentre la maniglia ha il fuoco, perché il solo trascinamento non è
 * utilizzabile da tutti (§20.3). Le due strade passano dalla stessa funzione di riordino, così non
 * possono divergere, e ogni spostamento viene annunciato da una regione `role="status"`.
 *
 * La bozza è locale e modificabile — aggiungi, sposta, rimuovi — e diventa quella salvata soltanto
 * con «Salva sequenza», che sostituisce l'intera sequenza in blocco: le posizioni si rinumerano da 1
 * e svuotarla è ammesso. Finché la bozza è identica a quella letta, i due pulsanti sono spenti: non
 * si invia una scrittura che non cambia nulla.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmButtonImports,
    HlmFieldImports,
    HlmNativeSelectImports,
    CdkDrag,
    CdkDropList,
    CdkDragHandle,
    DetailHeader,
    EmptyState,
    FormField,
    FormRoot,
    RouterLink,
    NgIcon,
    HlmTooltipImports,
  ],
  providers: [provideIcons({ lucideArchive, lucideGripVertical })],
  selector: 'app-lesson-detail',
  styleUrl: './lesson-detail.css',
  templateUrl: './lesson-detail.html',
})
export class LessonDetail implements OnInit {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly courseId = signal<string>('');
  private readonly lessonId = signal<string>('');

  protected readonly lesson = signal<Lesson | null>(null);

  /**
   * Corso della lezione. Serve al **percorso**, per nominare il gradino intermedio: la lezione da
   * sola dice a quale corso appartiene solo per identificativo.
   */
  protected readonly course = signal<Course | null>(null);

  /** Associazioni salvate, nell'ordine di posizione: è il termine di confronto della bozza. */
  protected readonly sequence = signal<readonly LessonExercise[]>([]);

  /** Bozza modificabile: ciò che l'utente vede e che il salvataggio sostituisce in blocco. */
  protected readonly draft = signal<readonly SequenceRow[]>([]);

  /** Anagrafica degli esercizi, sorgente dei nomi della sequenza e del selettore. */
  protected readonly exercises = signal<readonly Exercise[]>([]);

  /** Ultimo spostamento, annunciato ai lettori di schermo: il trascinamento è silenzioso. */
  protected readonly moveNotice = signal('');

  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<ApiError | null>(null);

  /** Contatore delle chiavi delle righe aggiunte qui: quelle lette dal server usano l'id salvato. */
  private nextLocalKey = 0;

  /** Titolo della pagina: il titolo autorevole della lezione, letto dal server. */
  protected readonly title = computed(() => this.lesson()?.title ?? 'Lezione');

  /** Percorso: corsi, il corso della lezione e la lezione che si sta leggendo. */
  protected readonly crumbs = computed<readonly DetailCrumb[]>(() => [
    { label: 'Corsi', link: ['/courses'] },
    { label: this.course()?.title ?? 'Lezioni', link: ['/courses', this.courseId()] },
    { label: this.title() },
  ]);

  /** Riga di contesto: quante posizioni ha la bozza e lo stato di archiviazione della lezione. */
  protected readonly subtitle = computed(() => {
    const count = this.draft().length;
    const exercises = count === 1 ? '1 esercizio' : `${count} esercizi`;

    return this.lesson()?.isArchived === true ? `${exercises} · lezione archiviata` : exercises;
  });

  /** La bozza è cambiata rispetto alla sequenza letta dal server: abilita il salvataggio. */
  protected readonly changed = computed(() => {
    const stored = this.sequence().map((row) => row.exerciseId);
    const draft = this.draft().map((row) => row.exerciseId);

    return draft.length !== stored.length || draft.some((id, index) => id !== stored[index]);
  });

  /**
   * Esercizi che si possono ancora aggiungere: quelli già nella bozza escono dal selettore, perché
   * aggiungerli di nuovo creerebbe due caselle identiche nella stessa lezione. Togliendo una riga
   * l'esercizio torna disponibile. L'anagrafica intera resta la sorgente dei nomi della sequenza:
   * il filtro vale per il selettore, non per la lettura dei titoli.
   */
  protected readonly availableExercises = computed(() => {
    const used = new Set(this.draft().map((row) => row.exerciseId));

    return this.exercises().filter((item) => !used.has(item.id));
  });

  /** Non resta nulla da aggiungere: selettore e pulsante si spengono e la pagina lo dichiara. */
  protected readonly nothingToAdd = computed(
    () => this.exercises().length > 0 && this.availableExercises().length === 0,
  );

  private readonly addFormModel = signal({ exerciseId: '' });
  protected readonly addForm = form(
    this.addFormModel,
    (path) => {
      // Con l'anagrafica tutta in sequenza non c'è nulla da scegliere: il campo si spegne, invece di
      // offrire un elenco che porterebbe solo a un errore di validazione.
      disabled(path, () => this.busy() || this.nothingToAdd());
      required(path.exerciseId, { message: 'Scegli un esercizio.' });
    },
    {
      submission: {
        action: async () => {
          this.addExercise();
        },
        ignoreValidators: 'none',
        onInvalid: (field) => field().errorSummary()[0]?.fieldTree().focusBoundControl(),
      },
    },
  );

  ngOnInit(): void {
    // L'indirizzo è l'unica sorgente della lezione mostrata: la sottoscrizione resta attiva per
    // rispondere anche ai movimenti avanti e indietro del browser.
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((parameters) => {
      this.courseId.set(parameters.get('courseId') ?? '');
      this.lessonId.set(parameters.get('lessonId') ?? '');
      void this.reload();
    });
  }

  protected async reload(): Promise<void> {
    const id = this.lessonId();
    if (id === '') {
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    try {
      this.course.set(await this.api.executeQuery<Course>('GetCourse', { id: this.courseId() }));
      this.lesson.set(await this.api.executeQuery<Lesson>('GetLesson', { id }));
      if (this.lesson()?.courseId !== this.courseId()) {
        throw new ApiError(
          'ResourceNotFound',
          404,
          'La lezione non appartiene al corso selezionato.',
        );
      }
      await this.loadExercises();
      await this.loadSequence(id);
    } catch (failure) {
      // Una lezione cancellata o un indirizzo sbagliato non lasciano la pagina a metà: si svuota
      // quello che si è letto e si mostra l'errore, con il ritorno al corso sempre disponibile.
      this.course.set(null);
      this.lesson.set(null);
      this.sequence.set([]);
      this.draft.set([]);
      this.error.set(toApiError(failure));
    } finally {
      this.loading.set(false);
    }
  }

  /** Aggiunge in coda l'esercizio scelto nell'anagrafica. */
  protected addExercise(): void {
    const exerciseId = this.addForm.exerciseId().value();
    if (exerciseId === '') {
      return;
    }

    this.draft.update((current) => [...current, this.row(exerciseId)]);
    this.addForm.exerciseId().value.set('');
    this.addForm().reset();
  }

  /** Riga rilasciata dal trascinamento: CDK consegna gli indici di partenza e di arrivo. */
  protected drop(event: CdkDragDrop<readonly SequenceRow[]>): void {
    const moved = this.draft()[event.previousIndex];
    if (moved === undefined) {
      return;
    }

    this.moveRow(event.previousIndex, event.currentIndex, moved.exerciseId);
  }

  /**
   * Frecce su e giù mentre la maniglia ha il fuoco: la stessa modifica del trascinamento, per chi
   * non usa il mouse. Il tasto viene intercettato, altrimenti la pagina scorrerebbe invece di
   * spostare la riga.
   */
  protected moveWithKeyboard(index: number, delta: number, event: Event): void {
    event.preventDefault();

    const row = this.draft()[index];
    if (row === undefined) {
      return;
    }

    this.moveRow(index, index + delta, row.exerciseId);
  }

  protected removeRow(index: number): void {
    this.draft.update((current) => current.filter((_, position) => position !== index));
    this.moveNotice.set('');
  }

  /** Riporta la bozza all'ordine letto dal server. */
  protected resetDraft(): void {
    this.draft.set(this.toRows(this.sequence()));
    this.moveNotice.set('');
  }

  /**
   * Sostituisce la sequenza in blocco. La versione inviata è quella **della lezione**, non delle
   * righe di associazione: la sequenza è un valore unico (§10.4). La sostituzione non alza la
   * versione della lezione, quindi dopo il salvataggio si rilegge la sequenza, che è cambiata.
   */
  protected async saveSequence(): Promise<void> {
    const lesson = this.lesson();
    if (lesson === null || !this.changed()) {
      return;
    }

    const exercises: readonly LessonExerciseInput[] = this.draft().map((row) => ({
      exerciseId: row.exerciseId,
    }));

    await this.run(async () => {
      await this.api.executeCommand<readonly LessonExercise[]>('ReplaceLessonExercises', {
        lessonId: lesson.id,
        baseRowVersion: lesson.rowVersion,
        exercises,
      });
      await this.loadSequence(lesson.id);
    });
  }

  protected exerciseTitle(exerciseId: string): string {
    return (
      this.exercises().find((item) => item.id === exerciseId)?.title ?? 'Esercizio non disponibile'
    );
  }

  /**
   * Sposta una riga della bozza. La usano il trascinamento e la tastiera: la regola dell'ordine sta
   * in un posto solo, quindi le due strade non possono divergere.
   */
  private moveRow(from: number, to: number, exerciseId: string): void {
    const rows = [...this.draft()];
    if (from === to || from < 0 || from >= rows.length || to < 0 || to >= rows.length) {
      return;
    }

    // `moveItemInArray` è la stessa operazione che CDK esegue internamente al rilascio: l'ordine
    // mostrato e quello salvato restano così la stessa cosa.
    moveItemInArray(rows, from, to);
    this.draft.set(rows);
    this.moveNotice.set(`«${this.exerciseTitle(exerciseId)}» è ora in posizione ${to + 1}.`);
  }

  private async loadExercises(): Promise<void> {
    this.exercises.set(
      await this.api.executeQuery<readonly Exercise[]>('ListExercises', { includeArchived: false }),
    );
  }

  private async loadSequence(lessonId: string): Promise<void> {
    const rows = await this.api.executeQuery<readonly LessonExercise[]>('ListLessonExercises', {
      lessonId,
    });
    this.sequence.set(rows);
    this.draft.set(this.toRows(rows));
    this.moveNotice.set('');
  }

  /** Le righe lette dal server si riconoscono dalla loro associazione: la chiave è già stabile. */
  private toRows(rows: readonly LessonExercise[]): readonly SequenceRow[] {
    return rows.map((row) => ({ key: row.id, exerciseId: row.exerciseId }));
  }

  private row(exerciseId: string): SequenceRow {
    this.nextLocalKey += 1;

    return { key: `nuovo-${String(this.nextLocalKey)}`, exerciseId };
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
    } catch (failure) {
      this.error.set(toApiError(failure));
    } finally {
      this.busy.set(false);
    }
  }
}
