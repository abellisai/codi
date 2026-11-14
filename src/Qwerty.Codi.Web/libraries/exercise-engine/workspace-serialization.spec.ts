import * as Blockly from 'blockly';
import 'blockly/blocks';
import { PROTOTYPE_BLOCK_TYPE, registerPrototypeBlock } from './prototype-block';
import {
  deserializeWorkspaceJson,
  InvalidWorkspaceStateError,
  serializeWorkspace,
  type WorkspaceState,
} from './workspace-serialization';

registerPrototypeBlock();

/** Stato sorgente dei round trip: un blocco del prototipo con un campo compilato. */
const oneBlockState: WorkspaceState = {
  blocks: {
    languageVersion: 0,
    blocks: [
      {
        type: PROTOTYPE_BLOCK_TYPE,
        id: 'prototypeBlock1',
        fields: { DIRECTION: 'avanti' },
      },
    ],
  },
};

describe('workspace serialization', () => {
  afterEach(() => {
    // Gli eventi sono uno stato globale di Blockly: un caso che li lasciasse soppressi
    // renderebbe dipendenti dall'ordine gli altri (§20.3).
    while (!Blockly.Events.isEnabled()) {
      Blockly.Events.enable();
    }
  });

  it('round trip: il workspace salvato si ricarica con gli stessi blocchi e campi', () => {
    const source = new Blockly.Workspace();
    deserializeWorkspaceJson(JSON.stringify(oneBlockState), source);

    const saved = serializeWorkspace(source);

    const target = new Blockly.Workspace();
    deserializeWorkspaceJson(saved, target);

    const blocks = target.getAllBlocks(false);
    expect(blocks).toHaveLength(1);
    expect(blocks[0].type).toBe(PROTOTYPE_BLOCK_TYPE);
    expect(blocks[0].getFieldValue('DIRECTION')).toBe('avanti');
  });

  it('ricarica ripetuta: il contenuto viene sostituito, non duplicato', () => {
    const workspace = new Blockly.Workspace();
    const saved = JSON.stringify(oneBlockState);

    deserializeWorkspaceJson(saved, workspace);
    deserializeWorkspaceJson(saved, workspace);

    expect(workspace.getAllBlocks(false)).toHaveLength(1);
  });

  it('round trip con shadow di riserva: conserva blocco attivo e fallback senza contarli due volte', () => {
    const source = new Blockly.Workspace();
    const target = new Blockly.Workspace();
    try {
      Blockly.serialization.workspaces.load(shadowState(), source);
      deserializeWorkspaceJson(serializeWorkspace(source), target);

      expect(target.getAllBlocks(false)).toHaveLength(2);
      const root = target.getBlockById('root');
      const active = root?.getInputTargetBlock('NUM');
      expect(active?.getFieldValue('NUM')).toBe(9);
      expect(root?.getInput('NUM')?.connection?.getShadowState()?.type).toBe('math_number');
    } finally {
      source.dispose();
      target.dispose();
    }
  });

  it('shadow senza sostituzione: ricarica il blocco shadow attivo', () => {
    const workspace = new Blockly.Workspace();
    try {
      const state = shadowState(false);
      deserializeWorkspaceJson(JSON.stringify(state), workspace);
      expect(workspace.getAllBlocks(false)).toHaveLength(2);
      expect(workspace.getBlockById('root')?.getInputTargetBlock('NUM')?.isShadow()).toBe(true);
    } finally {
      workspace.dispose();
    }
  });

  it('shadow di riserva sconosciuto: rifiuta prima di modificare il workspace', () => {
    const workspace = new Blockly.Workspace();
    try {
      deserializeWorkspaceJson(JSON.stringify(oneBlockState), workspace);
      const state = shadowState(true, 'codi_unknown_shadow');
      expect(() => deserializeWorkspaceJson(JSON.stringify(state), workspace)).toThrow(
        InvalidWorkspaceStateError,
      );
      expect(workspace.getAllBlocks(false)[0].type).toBe(PROTOTYPE_BLOCK_TYPE);
    } finally {
      workspace.dispose();
    }
  });

  it.each(['non è json', '[]', 'null', '"testo"', '42'])(
    'testo non utilizzabile (%s): rifiutato senza toccare il workspace',
    (json) => {
      const workspace = new Blockly.Workspace();

      expect(() => deserializeWorkspaceJson(json, workspace)).toThrow(InvalidWorkspaceStateError);
      expect(workspace.getAllBlocks(false)).toHaveLength(0);
    },
  );

  it('stato con un blocco che la piattaforma non conosce: rifiutato e contenuto invariato', () => {
    const workspace = new Blockly.Workspace();
    deserializeWorkspaceJson(JSON.stringify(oneBlockState), workspace);

    const unknownState: WorkspaceState = {
      blocks: {
        languageVersion: 0,
        blocks: [{ type: 'codi_unknown_block', id: 'unknownBlock1' }],
      },
    };

    expect(() => deserializeWorkspaceJson(JSON.stringify(unknownState), workspace)).toThrow(
      InvalidWorkspaceStateError,
    );

    const blocks = workspace.getAllBlocks(false);
    expect(blocks).toHaveLength(1);
    expect(blocks[0].type).toBe(PROTOTYPE_BLOCK_TYPE);
  });

  it('registrazione ripetuta del blocco: il blocco resta utilizzabile', () => {
    registerPrototypeBlock();
    registerPrototypeBlock();

    const workspace = new Blockly.Workspace();
    deserializeWorkspaceJson(JSON.stringify(oneBlockState), workspace);

    expect(workspace.getAllBlocks(false)).toHaveLength(1);
  });

  it('soppressione eventi già attiva nel chiamante: resta attiva dopo la ricarica', () => {
    const workspace = new Blockly.Workspace();

    Blockly.Events.disable();
    try {
      deserializeWorkspaceJson(JSON.stringify(oneBlockState), workspace);

      expect(Blockly.Events.isEnabled()).toBe(false);
    } finally {
      Blockly.Events.enable();
    }
  });

  it('caricamento assistito (emitEvents): gli eventi di modifica arrivano ai listener', async () => {
    const workspace = new Blockly.Workspace();
    const seen: string[] = [];
    workspace.addChangeListener((event) => {
      seen.push(event.type);
    });

    deserializeWorkspaceJson(JSON.stringify(oneBlockState), workspace, { emitEvents: true });
    await waitForBlocklyEvents(seen);

    expect(workspace.getAllBlocks(false)).toHaveLength(1);
    expect(seen).toContain(Blockly.Events.BLOCK_CREATE);
    // Gli eventi non sono soppressi dopo la carica: il listener di salvataggio resta attivo.
    expect(Blockly.Events.isEnabled()).toBe(true);
    workspace.dispose();
  });

  it('caricamento assistito: il workspace con eventi attivi registra la modifica come i trascinamenti', async () => {
    const workspace = new Blockly.Workspace();
    // Prima la via ordinaria, senza eventi: il listener non deve vedere nulla.
    deserializeWorkspaceJson(JSON.stringify(oneBlockState), workspace);
    const seen: string[] = [];
    workspace.addChangeListener((event) => {
      seen.push(event.type);
    });
    const otherState: WorkspaceState = {
      blocks: {
        languageVersion: 0,
        blocks: [{ type: 'math_number', id: 'plainNumber', fields: { NUM: 7 } }],
      },
    };

    deserializeWorkspaceJson(JSON.stringify(otherState), workspace, { emitEvents: true });
    await waitForBlocklyEvents(seen);

    expect(seen).toContain(Blockly.Events.BLOCK_CREATE);
    expect(seen).toContain(Blockly.Events.BLOCK_DELETE);
    workspace.dispose();
  });

  it('caricamento assistito: JSON rifiutato lascia il workspace intatto e il ripristino è silenzioso', async () => {
    const workspace = new Blockly.Workspace();
    deserializeWorkspaceJson(JSON.stringify(oneBlockState), workspace);
    const seen: string[] = [];
    workspace.addChangeListener((event) => {
      seen.push(event.type);
    });

    expect(() =>
      deserializeWorkspaceJson(
        '{"blocks":{"languageVersion":0,"blocks":[{"type":"codi_unknown_block","id":"x"}]}}',
        workspace,
        {
          emitEvents: true,
        },
      ),
    ).toThrow(InvalidWorkspaceStateError);
    await waitForBlocklyEvents(seen, 0);

    const blocks = workspace.getAllBlocks(false);
    expect(blocks).toHaveLength(1);
    expect(blocks[0].type).toBe(PROTOTYPE_BLOCK_TYPE);
    // Nessun evento di annullamento: una modifica rifiutata non arriva al salvataggio.
    expect(seen).toEqual([]);
    expect(Blockly.Events.isEnabled()).toBe(true);
    workspace.dispose();
  });

  it('caricamento assistito con soppressione già attiva nel chiamante: resta attiva', () => {
    const workspace = new Blockly.Workspace();
    const seen: string[] = [];
    workspace.addChangeListener((event) => {
      seen.push(event.type);
    });

    Blockly.Events.disable();
    try {
      deserializeWorkspaceJson(JSON.stringify(oneBlockState), workspace, { emitEvents: true });
      expect(Blockly.Events.isEnabled()).toBe(false);
    } finally {
      Blockly.Events.enable();
    }

    expect(seen).toEqual([]);
    expect(workspace.getAllBlocks(false)).toHaveLength(1);
    workspace.dispose();
  });
});

/**
 * Attende che la coda eventi di Blockly arrivi ai listener. Blockly smaltisce gli eventi
 * in un microtask/macrotask successivo alla carica: senza attesa il listener non avrebbe
 * ancora visto nulla. `minTicks` permette di accertare che nessun evento arriva.
 */
async function waitForBlocklyEvents(seen: string[], minTicks = 1): Promise<void> {
  // La coda eventi di Blockly usa ritardi reali: in jsdom i setTimeout(0) ravvicinati non
  // bastano; piccoli ritardi di alcuni millisecondi garantiscono lo smaltimento.
  for (let tick = 0; tick < 40; tick += 1) {
    await new Promise((resolve) => setTimeout(resolve, 5));
    if (minTicks === 0) continue;
    if (seen.length > 0) break;
  }
  if (minTicks === 0) {
    // Spazio sufficiente perché eventuali eventi in coda siano già stati smaltiti.
    await new Promise((resolve) => setTimeout(resolve, 50));
  }
}

function shadowState(withActiveBlock = true, shadowType = 'math_number'): WorkspaceState {
  return {
    blocks: {
      languageVersion: 0,
      blocks: [
        {
          type: 'math_single',
          id: 'root',
          fields: { OP: 'ROOT' },
          inputs: {
            NUM: {
              shadow: { type: shadowType, id: 'fallback', fields: { NUM: 0 } },
              ...(withActiveBlock
                ? { block: { type: 'math_number', id: 'active', fields: { NUM: 9 } } }
                : {}),
            },
          },
        },
      ],
    },
  };
}
