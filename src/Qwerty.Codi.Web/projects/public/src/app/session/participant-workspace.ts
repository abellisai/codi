import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  afterNextRender,
  effect,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import * as Blockly from 'blockly';
import { findEngine, registerKnownEngineBlocks } from '@exercise-engine/engine-registry';
import { setValueProgramVariables } from '@exercise-engine/value-blocks';
import {
  deserializeWorkspaceJson,
  serializeWorkspace,
} from '@exercise-engine/workspace-serialization';

/** Editor dell'alunno. Il server e il lease restano le autorità dei permessi. */
@Component({
  selector: 'app-participant-workspace',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div #host class="workspace" aria-label="Progetto a blocchi"></div>
    @if (problem()) {
      <p role="alert">{{ problem() }}</p>
    }`,
  styles: `
    .workspace {
      height: 420px;
      width: 100%;
    }
  `,
})
export class ParticipantWorkspace implements OnDestroy {
  readonly workspaceJson = input.required<string>();
  readonly allowedBlocks = input.required<readonly string[]>();
  /** Versione del motore dell'esercizio: da qui vengono i blocchi e il cassetto. */
  readonly engineVersion = input.required<string>();
  /**
   * Variabili dichiarate dalla definizione: i menu dei blocchi con variabile offrono queste. Vuote
   * significa «nessuna dichiarata»: il catalogo ripristina la lista del corso.
   */
  readonly declaredVariables = input<readonly string[]>([]);
  readonly readOnly = input(true);
  readonly workspaceChanged = output<string>();
  protected readonly problem = signal('');
  private readonly host = viewChild.required<ElementRef<HTMLDivElement>>('host');
  private workspace: Blockly.WorkspaceSvg | null = null;
  private loaded: string | null = null;
  private mounted = false;
  private mode = true;
  constructor() {
    afterNextRender(() => {
      this.mounted = true;
      this.mount();
    });
    effect(() => {
      const json = this.workspaceJson();
      const mode = this.readOnly();
      const blocks = this.allowedBlocks();
      const declared = this.declaredVariables();
      const engine = findEngine(this.engineVersion());
      if (!this.mounted) return;
      // Le variabili si impostano prima di ricaricare i blocchi: un campo già salvato deve trovare
      // il suo nome nel menu, non una lista rimasta a un esercizio precedente.
      setValueProgramVariables(declared);
      if (this.mode !== mode) this.mount();
      if (!mode && engine !== null) this.workspace?.updateToolbox(engine.buildToolbox(blocks));
      this.load(json);
    });
  }
  ngOnDestroy(): void {
    this.workspace?.dispose();
  }
  /**
   * Carica un programma nel workspace in modo assistito: via pubblica per l'automazione di
   * collaudo e per gli strumenti docente. Il JSON è validato prima del caricamento; se il
   * JSON è rifiutato il workspace non cambia e viene sollevato `InvalidWorkspaceStateError`.
   * A differenza del caricamento dall'input, gli eventi Blockly restano attivi: la modifica
   * arriva ai listener e segue la via normale di `workspaceChanged` e del salvataggio
   * automatico. Le posizioni dei blocchi arrivano dal JSON, senza spostamenti manuali.
   * In workspace di sola lettura la modifica non è emessa, come per le altre modifiche.
   */
  applyProgram(json: string): void {
    const workspace = this.workspace;
    if (workspace === null) {
      throw new Error('Il workspace non è pronto: attendere il montaggio del progetto.');
    }
    try {
      deserializeWorkspaceJson(json, workspace, { emitEvents: true });
      this.problem.set('');
    } catch (error) {
      this.problem.set('Il programma da applicare non è leggibile. Il progetto non è cambiato.');
      throw error;
    }
  }
  private mount(): void {
    this.workspace?.dispose();
    this.workspace = null;
    this.loaded = null;
    this.mode = this.readOnly();
    try {
      // I blocchi e il cassetto vengono dalla versione del motore, non da un catalogo cablato: un
      // esercizio di una versione nuova monta i propri blocchi senza toccare questo componente.
      const engine = findEngine(this.engineVersion());
      setValueProgramVariables(this.declaredVariables());
      registerKnownEngineBlocks();
      this.workspace = Blockly.inject(this.host().nativeElement, {
        readOnly: this.mode,
        ...(!this.mode && engine !== null
          ? { toolbox: engine.buildToolbox(this.allowedBlocks()), trashcan: true }
          : {}),
        move: { scrollbars: true, wheel: true },
        zoom: { controls: true, wheel: true },
      });
      this.workspace.addChangeListener((event) => {
        if (this.readOnly() || event.isUiEvent || this.workspace === null) return;
        const json = serializeWorkspace(this.workspace);
        if (json === this.loaded) return;
        this.loaded = json;
        this.workspaceChanged.emit(json);
      });
      this.load(this.workspaceJson());
    } catch {
      this.problem.set('Il progetto a blocchi non è disponibile in questo ambiente.');
    }
  }
  private load(json: string): void {
    if (this.workspace === null || this.loaded === json) return;
    try {
      deserializeWorkspaceJson(json, this.workspace);
      this.loaded = json;
      this.problem.set('');
    } catch {
      this.problem.set('Il progetto salvato non è leggibile. Chiedi al docente.');
    }
  }
}
