import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  WritableSignal,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { disabled, form, FormField, FormRoot, required } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCheck,
  lucideMonitorPlay,
  lucidePlus,
  lucideTrash2,
  lucideX,
} from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialog, HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import {
  ApiError,
  ApiService,
  Classroom,
  Course,
  Lesson,
  School,
  Session,
  SessionDeleted,
  SessionSummary,
  toApiError,
} from '@core/api';
import { DataTable, type DataTableColumn } from '@shared/components/data-table/data-table';
import { DataTableCell } from '@shared/components/data-table/data-table-cell';
import { admissionsLabel, formatInstant, sessionStatusLabel } from './session-format';

/**
 * Elenco delle sessioni (§6.1, §14.1, §14.4) e istanziazione di una lezione in una classe.
 *
 * La pagina mostra lo stato di ogni sessione e **porta** alla regia: la riga offre il pulsante che
 * apre `/sessions/:sessionId`, dove si comanda il ciclo di vita. Il pulsante «Nuova sessione» apre
 * il dialogo che sceglie a cascata scuola → classe → corso → lezione e invoca `CreateSession`; al
 * successo si va direttamente alla regia della sessione appena creata.
 *
 * La cascata è reattiva: un livello si abilita solo quando quello superiore è scelto, e cambiare un
 * livello ripulisce quelli inferiori, così non si invia una classe di una scuola con la lezione di
 * un'altra. Le letture delle liste passano dal dispatcher come il resto della sezione; nessun
 * componente usa `HttpClient` (§14.2).
 *
 * Dopo un rifiuto del server il dialogo resta aperto con i valori scelti, perché il docente possa
 * correggerli senza ricominciare; dopo ogni mutazione l'elenco non si presume aggiornato.
 *
 * L'elenco permette anche di **eliminare** una sessione conclusa o mai avviata (§6.1): la
 * cancellazione è definitiva e separata dalla chiusura, quindi chiede conferma **sulla riga** —
 * lo stesso gesto di archiviazione di scuole, classi, corsi ed esercizi — e una sessione in corso
 * non la offre: prima si conclude con Stop.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmButtonImports,
    HlmFieldImports,
    HlmDialogImports,
    HlmNativeSelectImports,
    DataTable,
    DataTableCell,
    FormField,
    FormRoot,
    RouterLink,
    NgIcon,
    HlmTooltipImports,
  ],
  providers: [provideIcons({ lucidePlus, lucideMonitorPlay, lucideTrash2, lucideCheck, lucideX })],
  selector: 'app-session-list',
  styleUrl: './session-list.css',
  templateUrl: './session-list.html',
})
export class SessionList implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  private readonly editor = viewChild(HlmDialog);

  protected readonly sessions = signal<readonly SessionSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);

  /** Errore dell'elenco: la pagina delle sessioni resta utilizzabile. */
  protected readonly error = signal<ApiError | null>(null);

  /** Errore del dialogo: le liste non caricate e il rifiuto di `CreateSession`. */
  protected readonly dialogError = signal<ApiError | null>(null);

  /**
   * Riga in attesa di conferma di eliminazione. La conferma vive nella riga, come per
   * l'archiviazione di scuole, classi, corsi ed esercizi: un dialogo in più sarebbe un secondo
   * modo di chiedere la stessa cosa.
   */
  protected readonly pendingDeleteId = signal<string | null>(null);

  /** Livelli della cascata: si riempiono quando il livello superiore viene scelto. */
  protected readonly schools = signal<readonly School[]>([]);
  protected readonly classrooms = signal<readonly Classroom[]>([]);
  protected readonly courses = signal<readonly Course[]>([]);
  protected readonly lessons = signal<readonly Lesson[]>([]);

  protected readonly statusLabel = sessionStatusLabel;
  protected readonly admissions = admissionsLabel;
  protected readonly instant = formatInstant;

  /** Colonne dell'elenco: i titoli della catena, lo stato e le azioni a fine riga. */
  protected readonly columns: readonly DataTableColumn[] = [
    { key: 'lessonTitle', label: 'Lezione' },
    { key: 'courseTitle', label: 'Corso' },
    { key: 'classroomName', label: 'Classe' },
    { key: 'schoolName', label: 'Scuola' },
    { key: 'status', label: 'Stato' },
    { key: 'admissionsOpen', label: 'Ingressi' },
    { key: 'startedAtUtc', label: 'Avvio' },
    { key: 'actions', label: 'Azioni', align: 'end' },
  ];

  /** Identità stabile delle righe: evita di ricostruire il DOM quando l'elenco si rilegge. */
  protected readonly identity = (session: SessionSummary): string => session.id;

  private readonly formModel = signal({
    schoolId: '',
    classroomId: '',
    courseId: '',
    lessonId: '',
  });

  /**
   * Modulo di istanziazione con Signal Forms: la cascata è dichiarata nello schema, così un campo
   * dipendente non è modificabile finché il suo livello superiore non è scelto, e la disabilitazione
   * accompagna la mutazione in corso.
   *
   * Il livello superiore si legge dal segnale del modello e non da `path`: nel callback dello
   * schema `path.schoolId` è un percorso, non il campo, quindi non si può interrogare. Il modello è
   * lo stato da cui la form nasce ed è già inizializzato quando lo schema viene valutato.
   */
  protected readonly createForm = form(
    this.formModel,
    (path) => {
      disabled(path.schoolId, () => this.busy());
      disabled(path.classroomId, () => this.busy() || this.formModel().schoolId === '');
      disabled(path.courseId, () => this.busy() || this.formModel().classroomId === '');
      disabled(path.lessonId, () => this.busy() || this.formModel().courseId === '');

      required(path.schoolId, { message: 'Scegli la scuola.' });
      required(path.classroomId, { message: 'Scegli la classe.' });
      required(path.courseId, { message: 'Scegli il corso.' });
      required(path.lessonId, { message: 'Scegli la lezione.' });
    },
    {
      submission: {
        action: async () => {
          await this.createSession();
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
      this.sessions.set(await this.api.executeQuery<readonly SessionSummary[]>('ListSessions', {}));
    } catch (failure) {
      this.error.set(toApiError(failure));
    } finally {
      this.loading.set(false);
    }
  }

  /** Apre il dialogo in istanziazione: modulo vuoto e scuole pronte per la prima scelta. */
  protected async startCreate(): Promise<void> {
    this.dialogError.set(null);
    this.classrooms.set([]);
    this.courses.set([]);
    this.lessons.set([]);
    this.createForm.schoolId().value.set('');
    this.createForm.classroomId().value.set('');
    this.createForm.courseId().value.set('');
    this.createForm.lessonId().value.set('');
    this.createForm().reset();

    await this.loadLists();
  }

  /** Scelta della scuola: le classi si rileggono e i livelli inferiori si azzerano. */
  protected async onSchoolChange(event: Event): Promise<void> {
    this.createForm.classroomId().value.set('');
    this.createForm.courseId().value.set('');
    this.createForm.lessonId().value.set('');
    this.classrooms.set([]);
    this.courses.set([]);
    this.lessons.set([]);

    const schoolId = selectValue(event);
    if (schoolId === '') {
      return;
    }

    await this.load(this.classrooms, () =>
      this.api.executeQuery<readonly Classroom[]>('ListClassrooms', {
        schoolId,
        includeArchived: false,
      }),
    );
  }

  /** Scelta della classe: i corsi si rileggono e la lezione si azzera. */
  protected async onClassroomChange(event: Event): Promise<void> {
    this.createForm.courseId().value.set('');
    this.createForm.lessonId().value.set('');
    this.courses.set([]);
    this.lessons.set([]);

    if (selectValue(event) === '') {
      return;
    }

    await this.load(this.courses, () =>
      this.api.executeQuery<readonly Course[]>('ListCourses', { includeArchived: false }),
    );
  }

  /** Scelta del corso: le lezioni si rileggono per il corso scelto. */
  protected async onCourseChange(event: Event): Promise<void> {
    this.createForm.lessonId().value.set('');
    this.lessons.set([]);

    const courseId = selectValue(event);
    if (courseId === '') {
      return;
    }

    await this.load(this.lessons, () =>
      this.api.executeQuery<readonly Lesson[]>('ListLessons', { courseId, includeArchived: false }),
    );
  }

  /** Istanzia la lezione nella classe scelta e apre la regia della sessione creata. */
  protected async createSession(): Promise<void> {
    const classroomId = this.createForm.classroomId().value();
    const lessonId = this.createForm.lessonId().value();
    if (this.busy() || classroomId === '' || lessonId === '') {
      return;
    }

    this.busy.set(true);
    this.dialogError.set(null);

    try {
      const created = await this.api.executeCommand<Session>('CreateSession', {
        lessonId,
        classroomId,
      });
      this.editor()?.close();
      await this.router.navigate(['/sessions', created.id]);
    } catch (failure) {
      this.dialogError.set(toApiError(failure));
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * Solo una sessione conclusa o mai avviata si elimina (§6.1): una in corso si conclude prima
   * con Stop, così la cancellazione non è mai lo scorciatoio per fermare la classe.
   */
  protected canDelete(session: SessionSummary): boolean {
    return session.status === 'Ended' || session.status === 'Lobby';
  }

  /** Mostra la conferma sulla riga della sessione scelta e dimentica un rifiuto precedente. */
  protected askDelete(session: SessionSummary): void {
    this.error.set(null);
    this.pendingDeleteId.set(session.id);
  }

  /** Annulla la conferma senza scrivere nulla. */
  protected cancelDelete(): void {
    this.pendingDeleteId.set(null);
  }

  /**
   * Elimina la sessione confermata. La revisione letta accompagna la richiesta, così il server
   * rifiuta una cancellazione su una sessione cambiata nel frattempo (§6.4). Un rifiuto lascia la
   * conferma aperta e mostra il messaggio nella pagina, come le altre mutazioni dell'elenco.
   */
  protected async confirmDelete(session: SessionSummary): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      await this.api.executeCommand<SessionDeleted>('DeleteSession', {
        sessionId: session.id,
        expectedSessionRevision: session.sessionRevision,
      });
      this.pendingDeleteId.set(null);
      await this.reload();
    } catch (failure) {
      this.error.set(toApiError(failure));
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * Carica un livello della cascata nel suo segnale. Le letture differiscono solo per la query e
   * per il segnale che le riceve, quindi il corpo — con il rifiuto del server — sta in un posto
   * solo, invece di ripetere lo stesso `try` a ogni livello.
   */
  private async load<TItem>(
    target: WritableSignal<readonly TItem[]>,
    read: () => Promise<readonly TItem[]>,
  ): Promise<void> {
    try {
      target.set(await read());
    } catch (failure) {
      this.dialogError.set(toApiError(failure));
    }
  }

  /** Primo livello della cascata: le scuole, da cui si scende a classi, corsi e lezioni. */
  private async loadLists(): Promise<void> {
    await this.load(this.schools, () =>
      this.api.executeQuery<readonly School[]>('ListSchools', { includeArchived: false }),
    );
  }
}

/** Valore scelto in un `<select>`: la cascata legge il DOM, non lo stato del modulo. */
function selectValue(event: Event): string {
  return event.target instanceof HTMLSelectElement ? event.target.value : '';
}
