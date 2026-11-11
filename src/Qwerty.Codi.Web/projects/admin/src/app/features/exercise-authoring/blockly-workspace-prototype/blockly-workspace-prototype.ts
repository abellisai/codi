import { HlmButtonImports } from '@spartan-ng/helm/button';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  signal,
  viewChild,
} from '@angular/core';
import * as Blockly from 'blockly';
import { PROTOTYPE_BLOCK_TYPE, registerPrototypeBlock } from '@exercise-engine/prototype-block';
import {
  deserializeWorkspaceJson,
  InvalidWorkspaceStateError,
  serializeWorkspace,
} from '@exercise-engine/workspace-serialization';

/** Cassetto con il solo blocco del prototipo: T6 definirà la scena GridPath. */
const prototypeToolbox: Blockly.utils.toolbox.ToolboxDefinition = {
  kind: 'categoryToolbox',
  contents: [
    {
      kind: 'category',
      name: 'Prototipo T0',
      colour: '210',
      contents: [{ kind: 'block', type: PROTOTYPE_BLOCK_TYPE }],
    },
  ],
};

/**
 * Pagina minima del prototipo T0: monta Blockly e prova il round trip del workspace.
 * Non è l'editor degli esercizi di T6 e non contiene il motore di esecuzione di T7.
 */
@Component({
  imports: [HlmButtonImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-blockly-workspace-prototype',
  styleUrl: './blockly-workspace-prototype.css',
  templateUrl: './blockly-workspace-prototype.html',
})
export class BlocklyWorkspacePrototype implements OnDestroy {
  private readonly workspaceHost = viewChild.required<ElementRef<HTMLDivElement>>('workspaceHost');

  private workspace: Blockly.WorkspaceSvg | null = null;

  private savedState: string | null = null;

  protected readonly blockCount = signal(0);

  protected readonly hasSavedState = signal(false);

  protected readonly message = signal('Workspace non ancora inizializzato.');

  constructor() {
    afterNextRender(() => this.startWorkspace());
  }

  ngOnDestroy(): void {
    this.workspace?.dispose();
    this.workspace = null;
  }

  protected save(): void {
    if (!this.workspace) {
      return;
    }

    this.savedState = serializeWorkspace(this.workspace);
    this.hasSavedState.set(true);
    this.message.set(
      `Salvati ${this.countBlocks()} blocchi: ${this.savedState.length} caratteri di JSON.`,
    );
  }

  protected reload(): void {
    if (!this.workspace || this.savedState === null) {
      this.message.set('Non c’è ancora un workspace salvato da ricaricare.');
      return;
    }

    try {
      deserializeWorkspaceJson(this.savedState, this.workspace);
      this.message.set(`Ricaricati ${this.countBlocks()} blocchi dal JSON salvato.`);
    } catch (error) {
      this.message.set(
        error instanceof InvalidWorkspaceStateError ? error.message : 'Ricarica non riuscita.',
      );
    }
  }

  private startWorkspace(): void {
    registerPrototypeBlock();

    this.workspace = Blockly.inject(this.workspaceHost().nativeElement, {
      toolbox: prototypeToolbox,
    });
    this.workspace.addChangeListener(() => this.countBlocks());

    this.message.set('Trascina il blocco «passo», poi salva e ricarica il workspace.');
  }

  private countBlocks(): number {
    const count = this.workspace?.getAllBlocks(false).length ?? 0;
    this.blockCount.set(count);
    return count;
  }
}
