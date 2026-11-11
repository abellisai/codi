import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  computed,
  inject,
  signal,
  viewChildren,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  disabled,
  form,
  FormField,
  FormRoot,
  max,
  min,
  PathKind,
  required,
  SchemaPath,
  SchemaPathRules,
  validate,
  submit,
} from '@angular/forms/signals';
import { NgTemplateOutlet } from '@angular/common';
import { formPublicationIssues, type PreparedExerciseDraft } from './publication-feedback';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePencilRuler, lucideRotateCcw } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';
import {
  ApiError,
  ApiService,
  Exercise,
  ExerciseDraft,
  ExerciseReferenceSolution,
  toApiError,
} from '@core/api';
import type { GridPathRules } from '@exercise-engine/grid-path-program';
import { DetailHeader, type DetailCrumb } from '@shared/components/detail-header/detail-header';
import { textRules } from '@shared/forms';
import { ExercisePreview } from './exercise-preview/exercise-preview';
import { ExercisePublication } from './exercise-publication/exercise-publication';
import { GridPathProgramEditor } from './grid-path-program-editor/grid-path-program-editor';
import { QuizEditor } from './quiz-editor';
import { ValueProgramEditor } from './value-program-editor';
import { findEngineByType, QUIZ_EXERCISE_TYPE } from '@exercise-engine/engine-registry';
import { GRID_PATH_EXERCISE_TYPE } from '@exercise-engine/grid-path-scene';
import { VALUE_PROGRAM_EXERCISE_TYPE } from '@exercise-engine/value-blocks';
import {
  BLOCK_LABELS,
  DIRECTION_ARROWS,
  DIRECTION_LABELS,
  DURATION_LIMIT_LABEL,
  DURATION_LIMIT_RANGE,
  EMPTY_INITIAL_WORKSPACE,
  EXERCISE_BLOCKS,
  GRID_DIRECTIONS,
  GRID_PATH_ENGINE_VERSION,
  MAX_GRID_SIZE,
  MAX_INSTRUCTIONS_LENGTH,
  MAX_MESSAGE_LENGTH,
  MIN_GRID_SIZE,
  buildDefinition,
  parseDefinition,
  sameCell,
  suggestedEditorModel,
  toEditorModel,
  type ExerciseEditorModel,
  type GridCell,
  type GridPathDefinition,
} from './exercise-definition';

/** Strumento attivo: che cosa applica il clic o l'attivazione di una cella. */
type SceneTool = 'obstacle' | 'start' | 'goal';

/** Campo numerico dell'editor: ammette il vuoto, che Signal Forms rappresenta con `null`. */
type NumericPath = SchemaPath<number | null, SchemaPathRules.Supported, PathKind.Child>;

/**
 * Editor di un esercizio `GridPath` (§13.1, §13.2). Seconda porzione di T6: compone la bozza
 * didattica — consegna, scena, blocchi, limiti, messaggi — e la salva con `SaveExerciseDraft`.
 *
 * La definizione non si scrive a mano: la pagina compone i campi e la definizione JSON nasce dalle
 * funzioni pure di `exercise-definition.ts`, così il **completamento coincide sempre con il
 * traguardo** e la scena mostrata non può divergere da quella inviata. La griglia è una matrice di
 * pulsanti: il clic applica lo strumento scelto, invio o spazio attivano la cella con il fuoco, e le
 * frecce spostano il cursore. Ogni cella ha un'etichetta che ne dice riga, colonna e stato, quindi
 * lo stato non è comunicato soltanto dal colore (§20.3).
 *
 * Quando `GetExerciseDraft` risponde `404` la bozza non esiste: la pagina propone una definizione
 * iniziale suggerita e il salvataggio successivo è una creazione, senza `baseRowVersion`. Un
 * rifiuto conserva i valori digitati e mostra il messaggio del server; successo e «Annulla
 * modifiche» riportano il modulo all'ultima definizione salvata.
 *
 * **Confine dichiarato.** Questa pagina compone esercizi `GridPath`, e i suoi campi — scena,
 * quattro blocchi, quattro limiti, completamento e messaggi — sono quelli di quella versione del
 * motore. Il registro delle versioni esiste lato server e nella libreria del motore, ma l'editor
 * non ha ancora un selettore di tipo: aggiungere una versione richiederà qui i campi propri del
 * tipo e la scelta esplicita del motore, invece di un ramo silenzioso dentro i campi attuali.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmButtonImports,
    HlmFieldImports,
    HlmInputImports,
    HlmNativeSelectImports,
    HlmTextareaImports,
    DetailHeader,
    ExercisePreview,
    ExercisePublication,
    FormField,
    FormRoot,
    GridPathProgramEditor,
    QuizEditor,
    ValueProgramEditor,
    RouterLink,
    NgIcon,
    NgTemplateOutlet,
  ],
  providers: [provideIcons({ lucidePencilRuler, lucideRotateCcw })],
  selector: 'app-exercise-editor',
  styleUrl: './exercise-editor.css',
  templateUrl: './exercise-editor.html',
})
export class ExerciseEditor implements OnInit {
  protected readonly publication = viewChild(ExercisePublication);
  private readonly latestDraft = signal<ExerciseDraft | null>(null);
  private readonly publicationAttempted = signal(false);
  protected readonly publicationIssues = computed(() =>
    this.publicationAttempted() ? formPublicationIssues(this.editorForm) : [],
  );
  /**
   * Preparazione della bozza per la verifica o la pubblicazione. Quando serve alla pubblicazione,
   * il salvataggio che ne fa parte non annuncia un successo proprio: la conferma è una sola, quella
   * della pubblicazione (§14.2).
   */
  private publishingDraft = false;
  protected readonly prepareForPublication = async (
    forPublication: boolean,
  ): Promise<PreparedExerciseDraft | null> => {
    this.publicationAttempted.set(forPublication);
    this.error.set(null);
    this.publishingDraft = forPublication;
    try {
      await submit(this.editorForm);
    } finally {
      this.publishingDraft = false;
    }
    const draft = this.latestDraft();
    return this.error() !== null || this.editorForm().invalid() || this.changed() || draft === null
      ? null
      : { ...draft, referenceSolutionJson: this.referenceSolution() };
  };
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly exerciseId = signal('');
  protected readonly title = signal('');
  protected readonly description = signal('');

  /**
   * Tipo di attività dell'esercizio, dall'anagrafica. Decide **quale editor** compone la bozza: la
   * pagina della griglia non sa comporre una domanda con risposta, e le due form non condividono un
   * solo campo.
   */
  protected readonly exerciseType = signal('');

  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<ApiError | null>(null);

  /** Vero quando il server non ha una bozza: il primo salvataggio la crea, senza versione base. */
  protected readonly isNewDraft = signal(false);

  /** Ultima definizione letta o salvata: termine di confronto di «modifiche non salvate». */
  private readonly savedDefinition = signal<GridPathDefinition | null>(null);

  protected readonly savedRowVersion = signal<string | null>(null);

  /**
   * Progetto iniziale degli alunni: vuoto, perché non è più un campo dell'admin. La pagina lo passa
   * al pannello di pubblicazione, che lo verifica insieme alla definizione.
   */
  protected readonly initialWorkspace = EMPTY_INITIAL_WORKSPACE;

  /**
   * Soluzione di riferimento: un progetto a blocchi riservato al docente (§13.1). È vuota quando il
   * docente non ne ha composta una, e non è mai inviata agli alunni.
   *
   * Il **programma iniziale** non è più un campo dell'admin: l'alunno parte da un progetto vuoto, e
   * la bozza conserva `EMPTY_INITIAL_WORKSPACE` per quel campo. Un progetto iniziale invisibile
   * sarebbe un'attività che il docente non può vedere né correggere.
   */
  protected readonly solutionJson = signal(EMPTY_INITIAL_WORKSPACE);
  private readonly savedSolutionJson = signal(EMPTY_INITIAL_WORKSPACE);

  private readonly model = signal<ExerciseEditorModel>(suggestedEditorModel());

  /** Strumento applicato dal clic su una cella; i pulsanti lo commutano. */
  protected readonly tool = signal<SceneTool>('obstacle');

  /** Cella attiva del cursore di tastiera. */
  protected readonly activeCell = signal<GridCell>({ x: 0, y: 0 });

  /** Annuncio per i lettori di schermo: il clic sulla griglia è silenzioso. */
  protected readonly announcement = signal('');

  /** I pulsanti della griglia, in ordine di riga e colonna: servono a spostare il fuoco. */
  private readonly cellButtons = viewChildren<ElementRef<HTMLButtonElement>>('cell');

  protected readonly editorForm = form(
    this.model,
    (path) => {
      disabled(path, () => this.busy() || this.publication()?.locking() === true);
      // Senza il punto di ingresso il catalogo non è eseguibile (§13.4): resta sempre selezionato.
      disabled(path.blocks.aula_start, () => true);

      textRules(path.instructions, 'Consegna', MAX_INSTRUCTIONS_LENGTH);
      textRules(path.successMessage, 'Messaggio di successo', MAX_MESSAGE_LENGTH);
      textRules(path.collisionMessage, 'Messaggio di collisione', MAX_MESSAGE_LENGTH);

      numberRules(path.width, 'Larghezza', MIN_GRID_SIZE, MAX_GRID_SIZE);
      numberRules(path.height, 'Altezza', MIN_GRID_SIZE, MAX_GRID_SIZE);

      numberRules(
        path.maxActiveDurationMinutes,
        DURATION_LIMIT_LABEL,
        DURATION_LIMIT_RANGE.min,
        DURATION_LIMIT_RANGE.max,
      );
    },
    {
      submission: {
        action: async () => {
          await this.save();
        },
        ignoreValidators: 'none',
        onInvalid: (field) => field().errorSummary()[0]?.fieldTree().focusBoundControl(),
      },
    },
  );

  protected readonly blocks = EXERCISE_BLOCKS;
  protected readonly blockLabels = BLOCK_LABELS;
  protected readonly directions = GRID_DIRECTIONS;
  protected readonly directionLabels = DIRECTION_LABELS;
  protected readonly limitLabel = DURATION_LIMIT_LABEL;
  protected readonly limitRange = DURATION_LIMIT_RANGE;

  /** La definizione che il salvataggio invierebbe: griglia, blocchi, limiti e messaggi. */
  protected readonly definition = computed(() =>
    buildDefinition(this.title() || 'Esercizio', this.model()),
  );

  /** La definizione come viaggia nella richiesta: la stessa stringa serve al salvataggio e alla verifica. */
  protected readonly definitionJson = computed(() => JSON.stringify(this.definition()));

  /** Il documento della soluzione da salvare: il progetto vuoto significa «nessuna soluzione». */
  protected readonly referenceSolution = computed(() =>
    this.solutionJson() === EMPTY_INITIAL_WORKSPACE ? null : this.solutionJson(),
  );

  /** Scena normalizzata: è la sorgente della griglia mostrata. */
  protected readonly scene = computed(() => this.definition().scene);

  /** Blocchi che il progetto può usare: quelli selezionati nella definizione. */
  protected readonly allowedBlocks = computed(() =>
    EXERCISE_BLOCKS.filter((block) => this.model().blocks[block]),
  );

  /** Regole di composizione del progetto: le stesse che la compilazione e il server applicano. */
  protected readonly rules = computed<GridPathRules>(() => ({
    engineVersion: GRID_PATH_ENGINE_VERSION,
    allowedBlocks: this.allowedBlocks(),
    maxBlocks: this.definition().limits.maxBlocks,
    maxNestingDepth: this.definition().limits.maxNestingDepth,
  }));

  /** Celle in ordine di riga: la griglia si disegna riga per riga. */
  protected readonly rows = computed<readonly (readonly GridCell[])[]>(() => {
    const { width, height } = this.scene();

    return Array.from({ length: height }, (_, y) =>
      Array.from({ length: width }, (_, x) => ({ x, y })),
    );
  });

  protected readonly pageTitle = computed(() => this.title() || 'Esercizio');

  protected readonly crumbs = computed<readonly DetailCrumb[]>(() => [
    { label: 'Esercizi', link: ['/exercises'] },
    { label: this.pageTitle() },
  ]);

  protected readonly subtitle = computed(() =>
    this.description() === '' ? 'Definizione della bozza dell’esercizio.' : this.description(),
  );

  /** La definizione corrente differisce dall'ultima salvata: abilita salvataggio e annullamento. */
  protected readonly changed = computed(() => {
    const saved = this.savedDefinition();

    return (
      saved === null ||
      JSON.stringify(saved) !== JSON.stringify(this.definition()) ||
      this.solutionJson() !== this.savedSolutionJson()
    );
  });

  /**
   * Vero quando c'è qualcosa da salvare. Su un esercizio che il server non ha ancora in bozza c'è
   * sempre: il primo salvataggio la crea. Senza questa distinzione il pulsante resterebbe spento su
   * un esercizio appena aggiunto alla libreria, e il docente non potrebbe salvare la definizione
   * proposta senza prima modificarne un campo.
   */
  protected readonly canSave = computed(() => this.changed() || this.isNewDraft());

  /** Etichetta della cella attiva, usata dai comandi accessibili. */
  protected readonly activeCellLabel = computed(() => {
    const cell = this.activeCell();
    const scene = this.scene();

    return `riga ${String(cell.y + 1)}, colonna ${String(cell.x + 1)} su ${String(scene.height)}×${String(scene.width)}`;
  });

  /** Comando di ostacolo: alterna fra rendere ostacolo e rendere libera la cella attiva. */
  protected readonly obstacleCommandLabel = computed(() =>
    this.isObstacle(this.activeCell()) ? 'Rendi libera' : 'Rendi ostacolo',
  );

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((parameters) => {
      this.exerciseId.set(parameters.get('exerciseId') ?? '');
      void this.load();
    });
  }

  private async load(): Promise<void> {
    const id = this.exerciseId();
    this.loading.set(true);
    this.error.set(null);

    try {
      const exercise = await this.api.executeQuery<Exercise>('GetExercise', { id });
      this.title.set(exercise.title);
      this.description.set(exercise.description);
      this.exerciseType.set(exercise.exerciseType);
      // Un esercizio dichiarativo lo compone la sua form, che carica da sé: la pagina della griglia
      // non deve leggere una definizione che non è sua né proporre una scena che non esiste.
      await (this.usesGrid() ? this.loadDraft() : this.loadDraftPlaceholder());
    } catch (failure) {
      this.error.set(toApiError(failure));
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * Vero quando la bozza la compone la pagina della griglia.
   *
   * Si cerca per **tipo**, non per versione: l'anagrafica porta il tipo (`GridPath`, `Quiz`,
   * `ValueProgram`) e la versione vive nella definizione. Un tipo che questa build non conosce non
   * ricade qui: la pagina lo dice, invece di mostrare una scena che non saprebbe comporre.
   *
   * Il confronto è sul **tipo** e non sulla bandiera `blockly`: due motori compongono un progetto a
   * blocchi — il percorso e il programma di valori — e chiedere alla bandiera quale form montare
   * manderebbe un esercizio di valori nell'editor della griglia. La bandiera dice se c'è un progetto,
   * non quale scena disegnarlo.
   */
  protected readonly usesGrid = computed(() => this.exerciseType() === GRID_PATH_EXERCISE_TYPE);

  /** Vero quando l'esercizio conserva valori: lo compone la form delle variabili e dei casi. */
  protected readonly usesValues = computed(
    () => this.exerciseType() === VALUE_PROGRAM_EXERCISE_TYPE,
  );

  /** Vero quando l'esercizio è quello dichiarativo: lo compone la form della domanda. */
  protected readonly usesQuiz = computed(() => this.exerciseType() === QUIZ_EXERCISE_TYPE);

  /** Vero quando l'anagrafica dichiara un tipo che questa build non sa comporre. */
  protected readonly unknownType = computed(
    () => this.exerciseType() !== '' && findEngineByType(this.exerciseType()) === null,
  );

  /**
   * Per un esercizio dichiarativo questa pagina non compone nulla: prepara soltanto il termine di
   * confronto perché «Ricarica» e l'intestazione restino coerenti. La form del tipo dichiarativo
   * legge e salva per conto proprio.
   */
  private async loadDraftPlaceholder(): Promise<void> {
    this.savedRowVersion.set(null);
    this.savedDefinition.set(null);
    this.isNewDraft.set(true);
  }

  private async loadDraft(): Promise<void> {
    try {
      const draft = await this.api.executeQuery<ExerciseDraft>(
        'GetExerciseDraft',
        { exerciseId: this.exerciseId() },
        // Una bozza che non esiste è la condizione di partenza dell'editor, non un guasto.
        { notFoundIsExpected: true },
      );
      const parsed = parseDefinition(draft.definitionJson);
      this.latestDraft.set(draft);

      this.savedRowVersion.set(draft.rowVersion);
      this.isNewDraft.set(false);
      await this.loadReferenceSolution();
      if (parsed !== null) {
        this.resetWith(toEditorModel(parsed));
      } else {
        this.error.set(
          new ApiError(
            'DefinitionUnreadable',
            0,
            'La bozza salvata non è leggibile con questa versione dell’editor: i valori mostrati sono quelli iniziali.',
          ),
        );
        this.resetWith(suggestedEditorModel());
      }
    } catch (failure) {
      const error = toApiError(failure);
      if (error.status !== 404) {
        throw failure;
      }

      // 404: la bozza non esiste ancora. Si propone una definizione iniziale e il salvataggio
      // sarà una creazione, senza `baseRowVersion`.
      this.savedRowVersion.set(null);
      this.applySolution(EMPTY_INITIAL_WORKSPACE);
      this.isNewDraft.set(true);
      this.resetWith(suggestedEditorModel());
    }
  }

  /**
   * Legge la soluzione privata della bozza. Una bozza che non esiste non ha una soluzione: il
   * <c>404</c> è la risposta «non c'è ancora», non un guasto, e la pagina riparte dal vuoto.
   */
  private async loadReferenceSolution(): Promise<void> {
    try {
      const solution = await this.api.executeQuery<ExerciseReferenceSolution>(
        'GetReferenceSolution',
        { exerciseId: this.exerciseId() },
        { notFoundIsExpected: true },
      );

      this.applySolution(solution.workspaceJson ?? EMPTY_INITIAL_WORKSPACE);
    } catch (failure) {
      if (toApiError(failure).status !== 404) {
        throw failure;
      }

      this.applySolution(EMPTY_INITIAL_WORKSPACE);
    }
  }

  /** Allinea la soluzione mostrata e quella salvata: sono lo stesso documento finché non cambia. */
  private applySolution(json: string): void {
    this.solutionJson.set(json);
    this.savedSolutionJson.set(json);
  }

  /** Applica una definizione al modulo azzerando lo stato d'interazione e riallineando il confronto. */
  private resetWith(model: ExerciseEditorModel): void {
    this.model.set(model);
    this.editorForm().reset();
    this.savedDefinition.set(this.definition());
    this.activeCell.set({ x: 0, y: 0 });
    this.announcement.set('');
  }

  /** La soluzione composta nell'editor dei blocchi: resta privata e viaggia con la bozza. */
  protected onSolutionChanged(solutionJson: string): void {
    this.solutionJson.set(solutionJson);
  }

  /** Dopo una copia di versione la bozza è cambiata sul server: la pagina la rilegge. */
  protected onDraftReplaced(): void {
    void this.reloadDraft();
  }

  /** Rilegge la bozza dopo una copia: l'esito lo annuncia l'avviso standard del dispatcher. */
  private async reloadDraft(): Promise<void> {
    await this.load();
  }

  /** Annulla le modifiche: riporta il modulo e la soluzione all'ultima definizione salvata. */
  protected resetDraft(): void {
    const saved = this.savedDefinition();
    if (saved === null) {
      return;
    }

    this.solutionJson.set(this.savedSolutionJson());
    this.resetWith(toEditorModel(saved));
  }

  protected selectTool(tool: SceneTool): void {
    this.tool.set(tool);
    this.announcement.set(
      tool === 'obstacle'
        ? 'Strumento ostacolo: attiva una cella per alternare ostacolo e cella libera.'
        : tool === 'start'
          ? 'Strumento partenza: attiva una cella per spostare qui la partenza.'
          : 'Strumento traguardo: attiva una cella per spostare qui il traguardo.',
    );
  }

  protected isActive(cell: GridCell): boolean {
    return sameCell(cell, this.activeCell());
  }

  protected cellState(cell: GridCell): 'start' | 'goal' | 'obstacle' | 'free' {
    const scene = this.scene();
    if (sameCell(cell, scene.start)) {
      return 'start';
    }
    if (sameCell(cell, scene.goal)) {
      return 'goal';
    }

    return this.isObstacle(cell) ? 'obstacle' : 'free';
  }

  /** Simbolo visibile della cella: comunica lo stato anche senza distinguere i colori. */
  protected cellSymbol(cell: GridCell): string {
    const scene = this.scene();

    switch (this.cellState(cell)) {
      case 'start':
        return DIRECTION_ARROWS[scene.start.direction];
      case 'goal':
        return '★';
      case 'obstacle':
        return '■';
      default:
        return '';
    }
  }

  /** Etichetta per i lettori di schermo: riga, colonna e stato in parole. */
  protected cellLabel(cell: GridCell): string {
    const scene = this.scene();
    const position = `Riga ${String(cell.y + 1)}, colonna ${String(cell.x + 1)}`;

    switch (this.cellState(cell)) {
      case 'start':
        return `${position}: partenza, direzione ${DIRECTION_LABELS[scene.start.direction]}`;
      case 'goal':
        return `${position}: traguardo`;
      case 'obstacle':
        return `${position}: ostacolo`;
      default:
        return `${position}: libera`;
    }
  }

  protected gridLabel(): string {
    const { width, height } = this.scene();

    return `Griglia della scena, ${String(width)} colonne per ${String(height)} righe. Usa le frecce per spostare il cursore fra le celle.`;
  }

  protected focusCell(cell: GridCell): void {
    this.activeCell.set(cell);
  }

  /** Frecce: spostano il cursore di una cella, restando dentro la griglia. */
  protected onCellKeydown(cell: GridCell, event: KeyboardEvent): void {
    switch (event.key) {
      case 'ArrowUp':
        this.moveCursor(cell.x, cell.y - 1);
        break;
      case 'ArrowDown':
        this.moveCursor(cell.x, cell.y + 1);
        break;
      case 'ArrowLeft':
        this.moveCursor(cell.x - 1, cell.y);
        break;
      case 'ArrowRight':
        this.moveCursor(cell.x + 1, cell.y);
        break;
      default:
        return;
    }

    event.preventDefault();
  }

  /** Un clic o l'attivazione da tastiera applica lo strumento scelto alla cella. */
  protected applyTool(cell: GridCell): void {
    switch (this.tool()) {
      case 'obstacle':
        this.toggleObstacle(cell);
        break;
      case 'start':
        this.placeStart(cell);
        break;
      case 'goal':
        this.placeGoal(cell);
        break;
    }
  }

  protected toggleObstacle(cell: GridCell): void {
    const scene = this.scene();
    if (sameCell(cell, scene.start) || sameCell(cell, scene.goal)) {
      this.announcement.set(
        `${this.cellLabel(cell)}: non può diventare un ostacolo, la partenza e il traguardo restano liberi.`,
      );
      return;
    }

    const obstacles = this.isObstacle(cell)
      ? scene.obstacles.filter((obstacle) => !sameCell(obstacle, cell))
      : [...scene.obstacles, cell];

    this.model.update((current) => ({ ...current, obstacles }));
    this.announcement.set(
      this.isObstacle(cell)
        ? `${this.cellLabel(cell)}: ora è un ostacolo.`
        : `${this.cellLabel(cell)}: ora è libera.`,
    );
  }

  protected placeStart(cell: GridCell): void {
    const scene = this.scene();
    // Se la cella è il traguardo, i due si scambiano: partenza e traguardo non possono coincidere.
    const goal = sameCell(cell, scene.goal) ? { x: scene.start.x, y: scene.start.y } : scene.goal;
    const obstacles = scene.obstacles.filter((obstacle) => !sameCell(obstacle, cell));

    this.model.update((current) => ({
      ...current,
      startX: cell.x,
      startY: cell.y,
      goalX: goal.x,
      goalY: goal.y,
      obstacles: obstacles.filter((obstacle) => !sameCell(obstacle, goal)),
    }));
    this.announcement.set(`${this.cellLabel(cell)}: la partenza è qui.`);
  }

  protected placeGoal(cell: GridCell): void {
    const scene = this.scene();
    const start = sameCell(cell, scene.start) ? { x: scene.goal.x, y: scene.goal.y } : scene.start;
    const obstacles = scene.obstacles.filter((obstacle) => !sameCell(obstacle, cell));

    this.model.update((current) => ({
      ...current,
      startX: start.x,
      startY: start.y,
      goalX: cell.x,
      goalY: cell.y,
      obstacles: obstacles.filter((obstacle) => !sameCell(obstacle, start)),
    }));
    this.announcement.set(`${this.cellLabel(cell)}: il traguardo è qui.`);
  }

  protected isObstacle(cell: GridCell): boolean {
    return this.scene().obstacles.some((obstacle) => sameCell(obstacle, cell));
  }

  /** Salva la bozza: crea se non esiste, altrimenti aggiorna con la versione letta. */
  protected async save(): Promise<void> {
    if (!this.canSave()) return;
    const version = this.savedRowVersion();
    const payload: Record<string, unknown> = {
      exerciseId: this.exerciseId(),
      definitionJson: this.definitionJson(),
      // Il programma iniziale non è un campo dell'admin: l'alunno parte dal progetto vuoto. Il
      // server richiede comunque il documento, quindi si invia quello vuoto e non un'assenza.
      workspaceJson: EMPTY_INITIAL_WORKSPACE,
      referenceSolutionJson: this.referenceSolution(),
    };
    if (version !== null) {
      // La versione è obbligatoria quando la bozza esiste: il server risponde 428 se manca.
      payload['baseRowVersion'] = version;
    }

    await this.run(async () => {
      const draft = await this.api.executeCommand<ExerciseDraft>('SaveExerciseDraft', payload, {
        // Dentro «Rendi disponibile» il salvataggio è un passo interno: la conferma è una sola.
        silentSuccess: this.publishingDraft,
      });
      this.latestDraft.set(draft);
      const parsed = parseDefinition(draft.definitionJson);

      this.savedRowVersion.set(draft.rowVersion);
      this.savedSolutionJson.set(this.solutionJson());
      this.isNewDraft.set(false);
      if (parsed !== null) {
        this.resetWith(toEditorModel(parsed));
      } else {
        this.editorForm().reset();
        this.savedDefinition.set(this.definition());
      }
    });
  }

  private moveCursor(x: number, y: number): void {
    const { width, height } = this.scene();
    const clampedX = Math.min(Math.max(x, 0), width - 1);
    const clampedY = Math.min(Math.max(y, 0), height - 1);

    this.activeCell.set({ x: clampedX, y: clampedY });
    this.cellButtons()[clampedY * width + clampedX]?.nativeElement.focus();
  }

  /** Esegue una mutazione conservando i valori in caso di rifiuto (§14.2). */
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

/** Campo numerico intero con intervallo: i limiti sono nello schema, non nel template (§14.2). */
function numberRules(path: NumericPath, label: string, minValue: number, maxValue: number): void {
  required(path, { message: `${label}: inserisci un valore.` });
  min(path, minValue, { message: `${label}: il minimo è ${String(minValue)}.` });
  max(path, maxValue, { message: `${label}: il massimo è ${String(maxValue)}.` });
  validate(path, ({ value }) => {
    const number = value();

    return number === null || Number.isInteger(number)
      ? undefined
      : { kind: 'integer', message: `${label}: usa un numero intero.` };
  });
}
