import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  computed,
  effect,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import {
  compileGridPathProgram,
  GridPathProgramError,
  type GridPathRules,
} from '@exercise-engine/grid-path-program';
import {
  createGridPathRun,
  type GridPathRun,
  type GridPathRunState,
} from '@exercise-engine/grid-path-runner';
import type {
  GridCell,
  GridDirection,
  GridPathLimits,
  GridPathScene,
} from '@exercise-engine/grid-path-scene';
import { DIRECTION_ARROWS, DIRECTION_LABELS, sameCell } from '../exercise-definition';

/** Intervallo fra due passi della prova automatica: i passi restano osservabili (§13.6). */
const STEP_DELAY_MS = 300;

/**
 * Anteprima dell'esercizio (§13.1, §13.5): esegue il progetto composto con lo stesso motore degli
 * alunni, in **stato isolato**. Non produce comandi di classe, non tocca il progetto salvato e non
 * registra risultati di alunni: è una prova dell'autore (§10.4).
 *
 * La corsa avanza di un'operazione visibile alla volta, automaticamente o con il comando «Passo
 * avanti»: così il docente può seguire il programma e vedere il blocco evidenziato nell'editor,
 * senza dipendere dal tempo di animazione per capire che cosa è successo.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmButtonImports],
  selector: 'app-exercise-preview',
  styleUrl: './exercise-preview.css',
  templateUrl: './exercise-preview.html',
})
export class ExercisePreview implements OnDestroy {
  /** Scena dell'esercizio: griglia, partenza, ostacoli e traguardo. */
  readonly scene = input.required<GridPathScene>();
  /** Regole di composizione del progetto: versione del motore, blocchi ammessi e limiti. */
  readonly rules = input.required<GridPathRules>();

  /** Limiti di esecuzione: passi e durata attiva. */
  readonly limits = input.required<GridPathLimits>();

  /** Progetto da eseguire, nella serializzazione di Blockly. */
  readonly workspaceJson = input.required<string>();

  /** Messaggi di esito della definizione, quelli che vedrà l'alunno. */
  readonly successMessage = input.required<string>();
  readonly collisionMessage = input.required<string>();

  /**
   * Motivo per cui la prova non si offre, `null` quando è disponibile. Serve alle versioni
   * pubblicate con un progetto iniziale vuoto: l'alunno lo compone, quindi qui non c'è nulla da
   * eseguire, e un pulsante che produce un errore sarebbe una diagnosi fuorviante.
   */
  readonly trialDisabledReason = input<string | null>(null);

  /** Blocco in esecuzione, per evidenziarlo nell'editor; `null` quando non c'è. */
  readonly activeBlockChanged = output<string | null>();

  protected readonly problem = signal('');
  protected readonly message = signal('');
  protected readonly position = signal<GridCell | null>(null);
  protected readonly direction = signal<GridDirection>('East');
  protected readonly running = signal(false);

  private run: GridPathRun | null = null;
  private timer: ReturnType<typeof setInterval> | null = null;

  /** Blocco comunicato per ultimo, per non ripetere la stessa evidenziazione (§14.2). */
  private readonly highlightedBlockId = signal<string | null>(null);

  constructor() {
    effect(() => {
      // La prova vale per la scena e il progetto con cui è cominciata: se cambiano, ricomincia da
      // capo. Evitare di azzerare la corsa mostrerebbe una posizione che non corrisponde più.
      this.scene();
      this.workspaceJson();
      this.rules();
      this.limits();

      // L'azzeramento scrive dei segnali: fuori da `untracked` l'effetto li leggerebbe, si
      // riattiverebbe da solo e cancellerebbe i passi appena compiuti.
      untracked(() => {
        this.reset();
      });
    });
  }

  protected readonly rows = computed<readonly (readonly GridCell[])[]>(() => {
    const { width, height } = this.scene();

    return Array.from({ length: height }, (_, y) =>
      Array.from({ length: width }, (_, x) => ({ x, y })),
    );
  });

  /**
   * Vero quando c'è un passo da compiere. Prima della partenza non esiste ancora una corsa, e un
   * passo la prepara: il comando resta quindi disponibile finché non ha raggiunto un esito.
   */
  protected canStep(): boolean {
    return this.run === null || !this.run.done();
  }

  protected readonly boardLabel = computed(
    () =>
      `Scena dell'esercizio, ${String(this.scene().width)} colonne per ${String(this.scene().height)} righe.`,
  );

  ngOnDestroy(): void {
    this.clearTimer();
  }

  /** Avvia la prova dall'inizio. */
  protected play(): void {
    if (this.trialDisabledReason() !== null || this.running() || !this.prepare()) {
      return;
    }

    this.running.set(true);
    this.timer = setInterval(() => {
      this.advance();
    }, STEP_DELAY_MS);
  }

  /** Compie un solo passo: è il modo con cui si segue il programma senza fretta. */
  protected stepOnce(): void {
    if (this.trialDisabledReason() !== null || this.running()) {
      return;
    }

    if (this.run === null && !this.prepare()) {
      return;
    }

    this.advance();
  }

  /** Ferma la prova automatica: il progetto resta quello composto. */
  protected stop(): void {
    this.clearTimer();
    this.running.set(false);
  }

  /** Riporta la scena al punto di partenza e dimentica la corsa. */
  protected reset(): void {
    this.stop();
    this.run = null;
    this.problem.set('');
    this.message.set('');
    this.position.set(this.scene().start);
    this.direction.set(this.scene().start.direction);
    this.emitActiveBlock(null);
  }

  /** Classi della cella: stato della scena ed eventuale presenza del personaggio. */
  protected cellClasses(cell: GridCell): string {
    const classes = ['cell', `cell-${this.cellState(cell)}`];
    if (this.isCharacter(cell)) {
      classes.push('cell-character');
    }

    return classes.join(' ');
  }

  /** Simbolo visibile: comunica lo stato anche senza distinguere i colori (§20.3). */
  protected cellSymbol(cell: GridCell): string {
    if (this.isCharacter(cell)) {
      return DIRECTION_ARROWS[this.direction()];
    }

    switch (this.cellState(cell)) {
      case 'start':
        return '•';
      case 'goal':
        return '★';
      case 'obstacle':
        return '■';
      default:
        return '';
    }
  }

  /** Etichetta per i lettori di schermo: posizione, contenuto e personaggio in parole. */
  protected cellLabel(cell: GridCell): string {
    const where = `Riga ${String(cell.y + 1)}, colonna ${String(cell.x + 1)}`;
    const state = this.cellState(cell);
    const content =
      state === 'start'
        ? `partenza, orientamento ${DIRECTION_LABELS[this.scene().start.direction]}`
        : state === 'goal'
          ? 'traguardo'
          : state === 'obstacle'
            ? 'ostacolo'
            : 'libera';
    const character = this.isCharacter(cell)
      ? `, personaggio rivolto a ${DIRECTION_LABELS[this.direction()]}`
      : '';

    return `${where}: ${content}${character}`;
  }

  /** Vero se in questa cella c'è il personaggio. */
  private isCharacter(cell: GridCell): boolean {
    const position = this.position();

    return position !== null && sameCell(position, cell);
  }

  /** Prepara la corsa: compila il progetto e azzera la scena. */
  private prepare(): boolean {
    this.reset();
    try {
      const program = compileGridPathProgram(JSON.parse(this.workspaceJson()), this.rules());
      this.run = createGridPathRun(program, this.scene(), {
        maxExecutionSteps: this.limits().maxExecutionSteps,
        maxActiveDurationMs: this.limits().maxActiveDurationMs,
      });

      return true;
    } catch (error) {
      // Un progetto che non si compila è la diagnosi più utile che l'anteprima possa dare: dice
      // che cosa manca prima di provare a eseguirlo.
      this.problem.set(
        error instanceof GridPathProgramError
          ? error.message
          : 'Il progetto non è leggibile: riprova a comporlo nell’editor dei blocchi.',
      );

      return false;
    }
  }

  /** Avanza di un passo e aggiorna ciò che si vede. */
  private advance(): void {
    const run = this.run;
    if (run === null || run.done()) {
      this.stop();

      return;
    }

    const state = run.step();
    this.position.set(state.position);
    this.direction.set(state.direction);
    this.emitActiveBlock(state.activeBlockId);

    if (state.outcome !== 'running') {
      this.stop();
      this.message.set(this.describeOutcome(state));
      this.emitActiveBlock(null);
    }
  }

  /** Comunica il blocco evidenziato, saltando i messaggi che non cambiano nulla. */
  private emitActiveBlock(blockId: string | null): void {
    if (this.highlightedBlockId() === blockId) {
      return;
    }

    this.highlightedBlockId.set(blockId);
    this.activeBlockChanged.emit(blockId);
  }

  private describeOutcome(state: GridPathRunState): string {
    switch (state.outcome) {
      case 'success':
        return this.successMessage();
      case 'collision':
        return this.collisionMessage();
      case 'leftTheGrid':
        return 'Il personaggio esce dalla griglia: prova a cambiare percorso.';
      case 'budget':
        return 'Il programma ha esaurito i passi o il tempo a disposizione.';
      case 'finished':
        return 'Il programma è finito, ma il personaggio non ha raggiunto il traguardo.';
      case 'running':
        return '';
    }
  }

  private cellState(cell: GridCell): 'start' | 'goal' | 'obstacle' | 'free' {
    const scene = this.scene();
    if (sameCell(cell, scene.start)) {
      return 'start';
    }

    if (sameCell(cell, scene.goal)) {
      return 'goal';
    }

    return scene.obstacles.some((obstacle) => sameCell(obstacle, cell)) ? 'obstacle' : 'free';
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }
}
