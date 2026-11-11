import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  output,
  signal,
  TemplateRef,
  viewChild,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { disabled, form, FormField } from '@angular/forms/signals';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideTrash2, lucideX } from '@ng-icons/lucide';
import type { GridPathRules } from '@exercise-engine/grid-path-program';
import type { GridPathLimits, GridPathScene } from '@exercise-engine/grid-path-scene';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmBadge, type BadgeVariants } from '@spartan-ng/helm/badge';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { toast } from '@spartan-ng/brain/sonner';
import {
  ApiError,
  ApiService,
  ExerciseDraftValidation,
  ExerciseVersion,
  ExerciseVersionDeleted,
  Exercise,
  toApiError,
} from '@core/api';
import { EXERCISE_BLOCKS, GRID_PATH_ENGINE_VERSION, parseDefinition } from '../exercise-definition';
import { ExercisePreview } from '../exercise-preview/exercise-preview';
import { parseQuizDefinition } from '../quiz-definition';
import { parseValueProgramDefinition } from '../value-program-definition';
import { ValueProgramProgramEditor } from '../value-program-program-editor/value-program-program-editor';
import {
  PUBLICATION_LABELS,
  type PublicationStatus,
  type PublicationIssue,
  type PreparedExerciseDraft,
  focusPublicationProblem,
} from '../publication-feedback';

/** Istanti ISO del server in ora locale, come nell'elenco delle sessioni. */
const publishedFormat = new Intl.DateTimeFormat('it-IT', {
  dateStyle: 'short',
  timeStyle: 'short',
});

/** Tutto ciò che serve a eseguire il progetto iniziale di una versione, già pronto per l'anteprima. */
interface VersionPreview {
  readonly scene: GridPathScene;
  readonly rules: GridPathRules;
  readonly limits: GridPathLimits;
  readonly workspaceJson: string;
  readonly successMessage: string;
  readonly collisionMessage: string;
  readonly title: string;
  /** Motivo per cui la prova non si offre: un progetto iniziale vuoto non ha nulla da eseguire. */
  readonly trialDisabledReason: string | null;
}

/**
 * Pubblicazione dell'esercizio (§13.1, §13.8): verifica la bozza, crea la versione immutabile e
 * mostra le versioni già pubblicate.
 *
 * Il comando principale prepara la bozza tramite l'editor, verifica i documenti salvati e pubblica
 * la revisione restituita dal server. La verifica separata controlla invece i documenti correnti
 * senza salvarli. Il blocco delle azioni impedisce invii concorrenti durante la pubblicazione.
 *
 * Le note dell'autore accompagnano la versione e non l'esercizio: sono il promemoria di chi ha
 * pubblicato — «qui ho alzato il limite dei passi» — e restano un contenuto riservato al docente
 * (§13.1). Riguardando una versione già pubblicata, le note si scrivono solo pubblicandone una
 * nuova, perché una versione non si aggiorna (§13.8).
 *
 * Una versione si può anche **guardare** senza copiarla nella bozza: l'anteprima esegue il progetto
 * iniziale pubblicato, in stato isolato, e non tocca nulla. È la differenza che conta quando il
 * docente vuole solo controllare com'era fatta una versione: copiarla sostituirebbe la bozza su
 * cui sta lavorando.
 *
 * Una versione che non è più la più recente si può anche eliminare (§13.8): la cronologia si
 * ripulisce senza toccare il materiale delle lezioni. L'ultima resta, perché è quella che le
 * prossime assegnazioni userebbero, e una versione già assegnata il server la rifiuta comunque.
 * Come nell'archiviazione degli elenchi, la conferma sta **sulla riga**: eliminare è definitivo.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgTemplateOutlet,
    NgIcon,
    ExercisePreview,
    ValueProgramProgramEditor,
    FormField,
    HlmBadge,
    HlmButtonImports,
    HlmFieldImports,
    HlmTextareaImports,
    HlmTooltipImports,
  ],
  viewProviders: [provideIcons({ lucideCheck, lucideTrash2, lucideX })],
  selector: 'app-exercise-publication',
  styleUrl: './exercise-publication.css',
  templateUrl: './exercise-publication.html',
})
export class ExercisePublication implements OnInit {
  private readonly api = inject(ApiService);

  /** Esercizio di cui si pubblica la bozza. */
  readonly exerciseId = input.required<string>();

  /** Documenti correnti dell'editor: sono quelli che la verifica controlla. */
  readonly definitionJson = input.required<string>();
  readonly workspaceJson = input.required<string>();

  /** Soluzione di riferimento composta; `null` quando non ce n'è una. */
  readonly solutionJson = input<string | null>(null);

  /** Revisione salvata della bozza; `null` quando la bozza non esiste ancora. */
  readonly draftRowVersion = input<string | null>(null);

  /** Vero quando l'editor ha modifiche non salvate. */
  readonly hasUnsavedChanges = input(false);
  readonly editorBusy = input(false);
  readonly prepareDraft = input<
    ((forPublication: boolean) => Promise<PreparedExerciseDraft | null>) | null
  >(null);
  readonly issues = input<readonly PublicationIssue[]>([]);
  readonly editorError = input<ApiError | null>(null);
  readonly toolbar = viewChild.required<TemplateRef<unknown>>('toolbar');
  private readonly savedStatus = signal<PublicationStatus>('Draft');
  readonly status = computed(() =>
    this.savedStatus() !== 'Draft' && this.hasUnsavedChanges()
      ? 'UnpublishedChanges'
      : this.savedStatus(),
  );
  protected readonly labels = PUBLICATION_LABELS;

  /**
   * Variante del badge per stato: neutra per la bozza, piena per ciò che è disponibile, di
   * attenzione per le modifiche che aspettano una nuova pubblicazione. Il testo resta nel badge,
   * quindi lo stato si legge anche senza distinguere i colori.
   */
  protected readonly statusVariant = computed<BadgeVariants['variant']>(() => {
    switch (this.status()) {
      case 'Available':
        return 'default';
      case 'UnpublishedChanges':
        return 'destructive';
      default:
        return 'secondary';
    }
  });

  /** Spiegazione breve mostrata al passaggio del mouse: dice come si cambia lo stato. */
  protected readonly statusHint = computed(() => {
    switch (this.status()) {
      case 'Available':
        return 'La revisione salvata è pubblicata e pronta per le lezioni.';
      case 'UnpublishedChanges':
        return 'Ci sono modifiche salvate dopo l’ultima versione: serve una nuova pubblicazione.';
      default:
        return 'La bozza esiste, ma non è ancora disponibile per le lezioni.';
    }
  });

  /** Emesso dopo una copia di versione nella bozza: la pagina la ricarica. */
  readonly draftReplaced = output<void>();

  protected readonly busy = signal(false);
  /** La form resta disponibile durante la preparazione locale, poi si blocca fino all'esito server. */
  readonly locking = signal(false);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly notice = signal('');

  /** Problemi riportati dalla verifica; `null` finché non è stata eseguita. */
  protected readonly problems = signal<readonly string[] | null>(null);
  protected readonly versions = signal<readonly ExerciseVersion[]>([]);

  /** Le versioni e le copie si fanno su una bozza salvata: senza, non c'è nulla da fotografare. */
  protected readonly canAct = computed(
    () => !this.busy() && !this.editorBusy() && !this.hasUnsavedChanges(),
  );

  protected readonly canPublish = computed(
    () =>
      !this.busy() &&
      !this.editorBusy() &&
      (this.prepareDraft() !== null ||
        (!this.hasUnsavedChanges() && this.draftRowVersion() !== null)),
  );

  protected async saveOnly(): Promise<void> {
    await this.run(async () => {
      // L'esito lo annuncia l'avviso standard del dispatcher: qui resta solo lo stato riletto.
      await this.prepareDraft()?.(false);
      await this.refreshStatus();
    });
  }

  /** Note dell'autore della prossima versione: libere e facoltative, quindi senza regole. */
  private readonly notesModel = signal({ notes: '' });

  protected readonly notesForm = form(this.notesModel, (path) => {
    disabled(path, () => this.busy());
  });

  /** Versione che il docente sta guardando; `null` quando nessuna anteprima è aperta. */
  protected readonly inspected = signal<ExerciseVersion | null>(null);

  /** Versione per cui è stata chiesta la conferma di eliminazione; `null` quando nessuna. */
  protected readonly pendingDeleteVersion = signal<string | null>(null);

  /**
   * Numero della versione più recente. Si guarda il massimo e non la prima riga: l'ordine
   * dell'elenco è una scelta del server e non deve decidere che cosa si può eliminare.
   */
  private readonly latestVersionNumber = computed(() =>
    this.versions().reduce((latest, version) => Math.max(latest, Number(version.versionNumber)), 0),
  );

  /** Una domanda pubblicata si consulta come testo, senza il motore della griglia. */
  protected readonly inspectedQuiz = computed(() => {
    const version = this.inspected();
    return version === null ? null : parseQuizDefinition(version.definitionJson);
  });

  protected readonly inspectedValues = computed(() => {
    const version = this.inspected();
    return version === null ? null : parseValueProgramDefinition(version.definitionJson);
  });

  /**
   * Progetto iniziale della versione guardata, nella forma che l'anteprima esegue. `null` quando la
   * definizione non è leggibile: una versione pubblicata da una forma precedente del formato non si
   * può eseguire con il motore di oggi, e dirlo è meglio che mostrare una prova che non parte.
   */
  protected readonly inspectedPreview = computed<VersionPreview | null>(() => {
    const version = this.inspected();
    if (version === null) {
      return null;
    }

    const definition = parseDefinition(version.definitionJson);
    if (definition === null) {
      return null;
    }

    return {
      scene: definition.scene,
      rules: {
        // La versione dichiarata dalla versione pubblicata, non quella corrente: l'anteprima deve
        // eseguire il progetto con il motore che lo ha prodotto, altrimenti mostrerebbe l'esito di
        // un esercizio diverso da quello che il docente ha verificato.
        engineVersion: version.engineVersion || GRID_PATH_ENGINE_VERSION,
        allowedBlocks: EXERCISE_BLOCKS.filter((block) =>
          definition.toolbox.allowedBlocks.includes(block),
        ),
        maxBlocks: definition.limits.maxBlocks,
        maxNestingDepth: definition.limits.maxNestingDepth,
      },
      limits: definition.limits,
      workspaceJson: version.workspaceJson,
      successMessage: definition.feedback.success,
      collisionMessage: definition.feedback.collision,
      title: definition.title,
      trialDisabledReason: hasBlocks(version.workspaceJson)
        ? null
        : 'Questa versione parte da un progetto vuoto, che compone l’alunno: non c’è un programma da eseguire qui. Per provare l’esercizio si usa la soluzione di riferimento nell’editor.',
    };
  });

  ngOnInit(): void {
    void this.refresh();
  }

  /** Verifica la bozza composta: non pubblica e non salva nulla. L'esito è un avviso. */
  protected async validate(): Promise<void> {
    await this.run(async () => {
      const validation = await this.api.executeCommand<ExerciseDraftValidation>(
        'ValidateExerciseDraft',
        {
          exerciseId: this.exerciseId(),
          definitionJson: this.definitionJson(),
          workspaceJson: this.workspaceJson(),
          referenceSolutionJson: this.solutionJson(),
        },
      );

      this.problems.set(validation.problems);
      if (validation.isValid) {
        toast.success('La bozza è pubblicabile.');
      } else {
        // I problemi restano in pagina, dove sono cliccabili: l'avviso dice l'esito, la pagina dice
        // che cosa correggere.
        toast.error('La bozza non è ancora pubblicabile.', {
          description: 'I problemi sono elencati nella pagina.',
        });
      }
    });
  }

  /** Pubblica la revisione salvata e aggiorna l'elenco delle versioni. */
  protected async publish(): Promise<void> {
    await this.run(async () => {
      this.problems.set(null);
      const prepare = this.prepareDraft();
      const draft = prepare === null ? null : await prepare(true);
      if (prepare !== null && draft === null) return;
      const version = draft?.rowVersion ?? this.draftRowVersion();
      if (version === null) return;
      this.locking.set(true);
      const validation = await this.api.executeCommand<ExerciseDraftValidation>(
        'ValidateExerciseDraft',
        {
          exerciseId: this.exerciseId(),
          definitionJson: draft?.definitionJson ?? this.definitionJson(),
          workspaceJson: draft?.workspaceJson ?? this.workspaceJson(),
          referenceSolutionJson: draft === null ? this.solutionJson() : draft.referenceSolutionJson,
        },
      );
      if (!validation.isValid) {
        this.problems.set(validation.problems);
        this.notice.set('Completa i dati indicati prima di rendere disponibile l’esercizio.');
        return;
      }
      await this.api.executeCommand<ExerciseVersion>('PublishExerciseVersion', {
        exerciseId: this.exerciseId(),
        baseRowVersion: version,
        // Le note accompagnano la versione: vuote restano assenti, e una versione senza note
        // non ne inventa una.
        authorNotes: this.notesModel().notes.trim() || null,
      });

      this.problems.set(null);
      // Nessun messaggio proprio: lo stato della barra passa a «Disponibile» e l'elenco si rilegge.
      await this.refreshVersions();
      await this.refreshStatus();
    });
  }

  /** Copia una versione pubblicata nella bozza, che la pagina ricarica. */
  protected async copy(version: ExerciseVersion): Promise<void> {
    await this.run(async () => {
      const revision = this.draftRowVersion();
      const payload: Record<string, unknown> = {
        exerciseId: this.exerciseId(),
        versionNumber: version.versionNumber,
      };
      if (revision !== null) {
        payload['baseRowVersion'] = revision;
      }

      await this.api.executeCommand('CopyExerciseVersionToDraft', payload);
      this.problems.set(null);
      // L'esito lo annuncia l'avviso standard del dispatcher: la pagina ricarica i documenti.
      this.draftReplaced.emit();
    });
  }

  /** Apre l'anteprima di una versione, o la richiude se è già quella aperta. */
  protected inspect(version: ExerciseVersion): void {
    this.inspected.set(this.inspected()?.id === version.id ? null : version);
  }

  /** L'ultima versione non si elimina: è quella che le prossime lezioni userebbero (§13.8). */
  protected isLatest(version: ExerciseVersion): boolean {
    return Number(version.versionNumber) === this.latestVersionNumber();
  }

  protected askDelete(version: ExerciseVersion): void {
    this.pendingDeleteVersion.set(version.id);
    // Il pulsante che ha aperto la conferma sparisce dalla riga: il focus passa a quello di
    // conferma, la cui etichetta dice che cosa sta per succedere. Il cambio di stato è quindi
    // annunciato anche a chi usa la tastiera o uno screen reader.
    setTimeout(() => document.getElementById(this.confirmDeleteId(version))?.focus());
  }

  /** Id del pulsante di conferma: serve al focus, che segue il cambio di stato della riga. */
  protected confirmDeleteId(version: ExerciseVersion): string {
    return `confirm-delete-${version.id}`;
  }

  protected cancelDelete(): void {
    this.pendingDeleteVersion.set(null);
  }

  /** Elimina una versione non più recente e rilegge l'elenco: l'esito lo annuncia l'avviso. */
  protected async deleteVersion(version: ExerciseVersion): Promise<void> {
    await this.run(async () => {
      try {
        await this.api.executeCommand<ExerciseVersionDeleted>('DeleteExerciseVersion', {
          exerciseId: this.exerciseId(),
          versionNumber: version.versionNumber,
        });

        // L'anteprima di una versione che non esiste più non si lascia aperta.
        if (this.inspected()?.id === version.id) {
          this.inspected.set(null);
        }

        await this.refreshVersions();
        await this.refreshStatus();
      } finally {
        // La conferma si chiude in ogni caso: dopo un rifiuto la riga non resta a metà, con una
        // domanda la cui risposta il server ha già dato.
        this.pendingDeleteVersion.set(null);
      }
    });
  }

  /** Data leggibile della pubblicazione. */
  protected publishedAt(version: ExerciseVersion): string {
    return publishedFormat.format(new Date(version.createdAt));
  }

  /** Impronta abbreviata: basta a riconoscere due versioni a colpo d'occhio. */
  protected shortHash(version: ExerciseVersion): string {
    return version.contentHash.slice(0, 8);
  }

  private async refresh(): Promise<void> {
    await this.run(async () => {
      await this.refreshVersions();
      await this.refreshStatus();
    });
  }

  private async refreshStatus(): Promise<void> {
    const exercise = await this.api.executeQuery<Exercise>('GetExercise', {
      id: this.exerciseId(),
    });
    this.savedStatus.set(exercise?.publicationStatus ?? 'Draft');
  }

  protected focusProblem(message: string): void {
    let type = '';
    try {
      type = JSON.parse(this.definitionJson()).exerciseType;
    } catch {
      /* La verifica espone il problema di formato. */
    }
    focusPublicationProblem(type, message);
  }

  private async refreshVersions(): Promise<void> {
    const versions = await this.api.executeQuery<readonly ExerciseVersion[]>(
      'ListExerciseVersions',
      { exerciseId: this.exerciseId() },
    );

    this.versions.set(versions);
  }

  /** Esegue un'azione conservando i valori in caso di rifiuto (§14.2). */
  private async run(action: () => Promise<void>): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.notice.set('');

    try {
      await action();
    } catch (failure) {
      this.error.set(toApiError(failure));
    } finally {
      this.locking.set(false);
      this.busy.set(false);
    }
  }
}

/**
 * Vero quando il documento Blockly contiene almeno un blocco. Un progetto vuoto non si esegue: la
 * compilazione lo rifiuta (§13.4), e per l'anteprima di una versione è la condizione normale —
 * il programma lo compone l'alunno — non un errore da mostrare.
 */
function hasBlocks(workspaceJson: string): boolean {
  try {
    const parsed: unknown = JSON.parse(workspaceJson);
    if (typeof parsed !== 'object' || parsed === null) {
      return false;
    }

    const blocks = (parsed as { blocks?: unknown }).blocks;
    if (typeof blocks !== 'object' || blocks === null) {
      return false;
    }

    const list = (blocks as { blocks?: unknown }).blocks;

    return Array.isArray(list) && list.length > 0;
  } catch {
    return false;
  }
}
