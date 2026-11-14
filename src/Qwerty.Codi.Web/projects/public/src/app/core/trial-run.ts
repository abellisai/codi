import { Injectable, signal } from '@angular/core';
import {
  parseGridPathDefinition,
  GridPathDefinitionError,
} from '@exercise-engine/grid-path-definition';
import {
  compileGridPathProgram,
  GridPathProgramError,
  type GridPathRules,
} from '@exercise-engine/grid-path-program';
import {
  createGridPathRun,
  type GridPathOutcome,
  type GridPathRun,
  type GridPathRunState,
} from '@exercise-engine/grid-path-runner';
import type { GridCell, GridDirection } from '@exercise-engine/grid-path-scene';

/** Distanza fra due passi della prova: i passi restano osservabili, come nel player (§13.6). */
const STEP_DELAY_MS = 300;

/**
 * Prova locale del programma dell'alunno (§13.1, §13.5, §14.4). L'alunno preme **Prova** e vede il
 * proprio personaggio muoversi sulla griglia: è lo stesso motore che esegue la corsa comandata dal
 * insegnante e che l'anteprima dell'autore usa in stato isolato, ma la corsa vive **qui**.
 *
 * Il confine è netto e dichiarato: la prova **non** è un'esecuzione della lezione. Non produce
 * checkpoint, non tocca il progetto salvato, non aggiorna la telemetria e non compare nella regia
 * del docente — l’insegnante continua a vedere soltanto la corsa che ha comandato. Per questo la prova
 * non passa dal worker del player: userebbe lo stesso canale dei comandi e potrebbe sovrapporsi a
 * una direttiva in arrivo.
 *
 * Una prova appartiene alla definizione e al progetto con cui è cominciata: se il progetto cambia,
 * la prova si azzera invece di mostrare la posizione di un programma che non esiste più.
 */
@Injectable({ providedIn: 'root' })
export class TrialRun {
  /** Vero quando la scena mostrata è quella della prova: la pagina cede al disegno locale. */
  readonly active = signal(false);

  /** Vero mentre i passi scorrono da soli: «Ferma» lo interrompe senza cancellare la posizione. */
  readonly running = signal(false);

  /** Personaggio della prova; `null` quando la prova non è iniziata. */
  readonly position = signal<GridCell | null>(null);
  readonly direction = signal<GridDirection>('East');

  /** Programma che non si compila: la diagnosi più utile che la prova possa dare (§13.3). */
  readonly problem = signal('');

  /** Esito didattico della prova, con i messaggi della definizione. */
  readonly message = signal('');
  readonly succeeded = signal(false);

  /** Passi e rotazioni eseguiti: le operazioni che si vedono. */
  readonly steps = signal(0);

  private run: GridPathRun | null = null;
  private timer: ReturnType<typeof setInterval> | null = null;
  private feedback = { success: '', collision: '' };

  /** Avvia la prova dall'inizio e lascia scorrere i passi. */
  play(definitionJson: string, workspaceJson: string): void {
    this.stopTimer();

    if (!this.prepare(definitionJson, workspaceJson)) {
      return;
    }

    this.running.set(true);
    this.timer = setInterval(() => this.advance(), STEP_DELAY_MS);
  }

  /**
   * Un solo passo: è il modo con cui l'alunno segue il proprio programma senza fretta. Se la prova
   * non è ancora cominciata — o se è già finita — il passo la fa ripartire dalla partenza: dopo un
   * esito, premere di nuovo «Un passo» deve mostrare il programma, non restare fermi.
   */
  step(definitionJson: string, workspaceJson: string): void {
    if (this.running()) {
      return;
    }

    if ((this.run === null || this.run.done()) && !this.prepare(definitionJson, workspaceJson)) {
      return;
    }

    this.advance();
  }

  /** Ferma lo scorrimento: la posizione raggiunta resta, il progetto resta quello composto. */
  stop(): void {
    this.stopTimer();
    this.running.set(false);
  }

  /** Riporta la scena alla partenza e dimentica la prova. */
  reset(): void {
    this.stopTimer();
    this.running.set(false);
    this.run = null;
    this.active.set(false);
    this.position.set(null);
    this.direction.set('East');
    this.problem.set('');
    this.message.set('');
    this.succeeded.set(false);
    this.steps.set(0);
  }

  /** Smaltisce il timer alla distruzione della vista (§14.2). */
  dispose(): void {
    this.stopTimer();
  }

  /** Prepara una corsa nuova: compila il progetto e azzera la scena. */
  private prepare(definitionJson: string, workspaceJson: string): boolean {
    this.reset();

    try {
      const definition = parseGridPathDefinition(definitionJson);
      this.feedback = definition.feedback;
      const rules: GridPathRules = {
        engineVersion: definition.engineVersion,
        allowedBlocks: definition.allowedBlocks,
        maxBlocks: definition.limits.maxBlocks,
        maxNestingDepth: definition.limits.maxNestingDepth,
      };
      const program = compileGridPathProgram(JSON.parse(workspaceJson), rules);
      this.run = createGridPathRun(program, definition.scene, {
        maxExecutionSteps: definition.limits.maxExecutionSteps,
        maxActiveDurationMs: definition.limits.maxActiveDurationMs,
      });
      this.active.set(true);
      // La posizione è una cella: l'orientamento vive nel proprio segnale e non si mescola a essa.
      this.position.set({ x: definition.scene.start.x, y: definition.scene.start.y });
      this.direction.set(definition.scene.start.direction);

      return true;
    } catch (error) {
      this.problem.set(this.describeProblem(error));

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
    this.steps.set(state.executions);

    if (state.outcome !== 'running') {
      this.stop();
      this.message.set(this.describeOutcome(state));
      this.succeeded.set(state.outcome === 'success');
    }
  }

  /**
   * Traduzione dell'esito con i messaggi della definizione: sono gli stessi che l'alunno vedrebbe
   * nella corsa comandata, così la prova non insegna parole diverse dal gioco vero.
   */
  private describeOutcome(state: GridPathRunState): string {
    switch (state.outcome) {
      case 'success':
        return this.feedback.success;
      case 'collision':
        return this.feedback.collision;
      case 'leftTheGrid':
        return 'Il personaggio esce dalla griglia: prova a cambiare percorso.';
      case 'budget':
        return 'Il programma ha esaurito i passi o il tempo a disposizione.';
      case 'finished':
        return 'Il programma è finito, ma il personaggio non ha raggiunto il traguardo.';
      case 'running':
        return '';
      default:
        return '';
    }
  }

  /** Un progetto che non si compila si spiega: dire «non va» non aiuterebbe a correggerlo. */
  private describeProblem(error: unknown): string {
    if (error instanceof GridPathProgramError || error instanceof GridPathDefinitionError) {
      return error.message;
    }

    return 'Il progetto non è leggibile: riprova a comporlo con i blocchi.';
  }

  private stopTimer(): void {
    if (this.timer !== null) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }
}

/** Esito della prova, per chi lo vuole tipizzato: è lo stesso vocabolario del motore. */
export type TrialOutcome = GridPathOutcome;
