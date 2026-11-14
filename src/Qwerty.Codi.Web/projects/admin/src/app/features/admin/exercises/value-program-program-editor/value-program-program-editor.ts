/**
 * Editor del progetto a blocchi di un esercizio che **conserva valori** (§13.9).
 *
 * È la copia di `GridPathProgramEditor` cablata sul catalogo di `valueprogram-1`. La copia è
 * deliberata: generalizzare il componente significherebbe un componente a sé con due callback
 * iniettate, e il valore di quella generalizzazione non è ancora dimostrato da un terzo motore. Il
 * giorno in cui ne arriva uno, la forma giusta si vedrà da sé.
 *
 * Il componente non conosce il salvataggio: carica lo stato che riceve, lo serializza quando cambia e
 * lo comunica al genitore. Così la form dell'esercizio resta l'unico punto che compone la richiesta.
 */ import {
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
import {
  buildValueProgramToolbox,
  registerValueProgramBlocks,
  setValueProgramVariables,
} from '@exercise-engine/value-blocks';
import {
  deserializeWorkspaceJson,
  InvalidWorkspaceStateError,
  serializeWorkspace,
} from '@exercise-engine/workspace-serialization';

/**
 * Editor del progetto a blocchi di un esercizio (§13.1, §13.3).
 *
 * Il componente non conosce il salvataggio: carica lo stato che riceve, lo serializza quando
 * cambia e lo comunica al genitore. Così la pagina dell'esercizio resta l'unico punto che compone
 * la richiesta e che confronta ciò che è stato salvato con ciò che è a schermo.
 *
 * Il cassetto mostra soltanto i blocchi che la definizione mette a disposizione, ma non è il
 * cassetto a rendere valido un progetto: la verifica è del server (§13.3) e della compilazione.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-value-program-program-editor',
  styleUrl: './value-program-program-editor.css',
  templateUrl: './value-program-program-editor.html',
})
export class ValueProgramProgramEditor implements OnDestroy {
  private readonly host = viewChild.required<ElementRef<HTMLDivElement>>('workspaceHost');

  /** Blocchi che la definizione mette a disposizione: il cassetto mostra questi. */
  readonly allowedBlocks = input.required<readonly string[]>();

  /**
   * Variabili dichiarate dalla definizione dell'esercizio: i menu dei blocchi con variabile offrono
   * queste. Vuote significa «nessuna dichiarata»: il catalogo ripristina la lista del corso.
   */
  readonly declaredVariables = input<readonly string[]>([]);

  /** Stato del workspace da mostrare; è la serializzazione di Blockly. */
  readonly workspaceJson = input.required<string>();

  /** Una versione pubblicata si consulta senza modificare i blocchi. */
  readonly readOnly = input(false);

  /** Blocco da evidenziare durante una prova; `null` toglie l'evidenziazione. */
  readonly activeBlockId = input<string | null>(null);

  /** Nuovo stato del workspace, già serializzato. */
  readonly workspaceChanged = output<string>();

  /** Vero quando l'ambiente non permette di montare il workspace: la pagina lo dice. */
  protected readonly unavailable = signal('');

  /** Problema di lettura dello stato ricevuto: il progetto precedente resta intatto. */
  protected readonly problem = signal('');

  private workspace: Blockly.WorkspaceSvg | null = null;

  /** Ultimo stato applicato al workspace e ultimo stato comunicato: evitano anelli di ricarica. */
  private loadedJson: string | null = null;
  private emittedJson: string | null = null;

  constructor() {
    afterNextRender(() => {
      this.mount();
    });

    effect(() => {
      // Le variabili si impostano prima di ricaricare i blocchi: un campo già salvato deve trovare
      // il suo nome nel menu, non una lista rimasta a un esercizio precedente.
      setValueProgramVariables(this.declaredVariables());
      this.load(this.workspaceJson());
    });

    effect(() => {
      this.updateToolbox(this.allowedBlocks());
    });

    effect(() => {
      this.highlight(this.activeBlockId());
    });
  }

  ngOnDestroy(): void {
    this.workspace?.dispose();
    this.workspace = null;
  }

  /** Monta il workspace e vi applica lo stato letto. */
  private mount(): void {
    try {
      setValueProgramVariables(this.declaredVariables());
      registerValueProgramBlocks();
      const workspace = Blockly.inject(this.host().nativeElement, {
        readOnly: this.readOnly(),
        toolbox: this.readOnly() ? undefined : buildValueProgramToolbox(this.allowedBlocks()),
        trashcan: !this.readOnly(),
        zoom: { controls: true, wheel: true, startScale: 1 },
        move: { scrollbars: true, drag: true, wheel: true },
      });

      workspace.addChangeListener((event) => {
        this.onChange(event);
      });

      this.workspace = workspace;
      this.load(this.workspaceJson());
    } catch (error) {
      // Un ambiente che non permette di disegnare il workspace lo dichiara: la pagina non deve
      // mostrare un riquadro vuoto senza spiegazione.
      this.unavailable.set(
        `Il progetto a blocchi non è disponibile in questo ambiente: ${error instanceof Error ? error.message : 'errore sconosciuto'}.`,
      );
    }
  }

  /** Applica lo stato ricevuto, se è diverso da quello già caricato. */
  private load(json: string): void {
    const workspace = this.workspace;
    if (workspace === null || json === this.loadedJson) {
      return;
    }

    this.problem.set('');
    try {
      deserializeWorkspaceJson(json, workspace);
      this.loadedJson = json;
      this.emittedJson = json;
    } catch (error) {
      this.problem.set(
        error instanceof InvalidWorkspaceStateError
          ? error.message
          : 'Il progetto salvato non è leggibile con questa versione dell’editor.',
      );
    }
  }

  /** Aggiorna il cassetto quando cambiano i blocchi disponibili. */
  private updateToolbox(allowedBlocks: readonly string[]): void {
    if (!this.readOnly()) this.workspace?.updateToolbox(buildValueProgramToolbox(allowedBlocks));
  }

  /** Evidenzia il blocco in esecuzione, se c'è. */
  private highlight(blockId: string | null): void {
    this.workspace?.highlightBlock(blockId);
  }

  /** Comunica al genitore lo stato serializzato, saltando le variazioni che non lo cambiano. */
  private onChange(event: Blockly.Events.Abstract): void {
    const workspace = this.workspace;
    if (workspace === null || event.isUiEvent || this.readOnly()) {
      return;
    }

    const json = serializeWorkspace(workspace);
    if (json === this.emittedJson) {
      return;
    }

    this.emittedJson = json;
    this.loadedJson = json;
    this.workspaceChanged.emit(json);
  }
}
