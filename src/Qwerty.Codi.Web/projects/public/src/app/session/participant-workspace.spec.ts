import * as Blockly from 'blockly';
import 'blockly/blocks';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ParticipantWorkspace } from './participant-workspace';
import { InvalidWorkspaceStateError } from '@exercise-engine/workspace-serialization';
import {
  NUMBER_FIELD,
  OTHER_INPUT,
  VALUE_INPUT,
  VALUE_PROGRAM_BLOCK_TYPES,
  VALUE_PROGRAM_ENGINE_VERSION,
} from '@exercise-engine/value-blocks';

/**
 * Prova della via assistita `applyProgram` sul componente dell'alunno: il programma entra
 * dal metodo pubblico, non dal trascinamento, e la modifica segue la via normale del
 * salvataggio automatico (`workspaceChanged`). Un JSON rifiutato non deve toccare il
 * workspace e non deve produrre modifiche da salvare.
 */

/** Programma di collaudo: bandiera, dire (2 + 3). Le posizioni arrivano dal JSON. */
const programJson = JSON.stringify({
  blocks: {
    languageVersion: 0,
    blocks: [
      {
        type: 'val_start',
        id: 'qaStart',
        x: 40,
        y: 40,
        next: {
          block: {
            type: 'val_show',
            id: 'qaShow',
            inputs: {
              [VALUE_INPUT]: {
                block: {
                  type: 'val_add',
                  id: 'qaAdd',
                  inputs: {
                    [VALUE_INPUT]: {
                      block: { type: 'val_number', id: 'qaTwo', fields: { [NUMBER_FIELD]: 2 } },
                    },
                    [OTHER_INPUT]: {
                      block: { type: 'val_number', id: 'qaThree', fields: { [NUMBER_FIELD]: 3 } },
                    },
                  },
                },
              },
            },
          },
        },
      },
    ],
  },
});

/** Progetto iniziale di partenza: una sola bandiera, per misurare il dopo. */
const initialJson = JSON.stringify({
  blocks: { languageVersion: 0, blocks: [{ type: 'val_start', id: 'initialStart', x: 40, y: 40 }] },
});

describe('app-participant-workspace: applyProgram', () => {
  afterEach(() => {
    while (!Blockly.Events.isEnabled()) {
      Blockly.Events.enable();
    }
  });

  it('carica il programma e la modifica arriva al salvataggio automatico', async () => {
    const fixture = await createWorkspace(initialJson);
    const emitted: string[] = [];
    fixture.componentInstance.workspaceChanged.subscribe((json: string) => {
      emitted.push(json);
    });

    fixture.componentInstance.applyProgram(programJson);
    await waitForEmitted(emitted);

    expect(emitted).toHaveLength(1);
    const saved = JSON.parse(emitted[0]) as { blocks: { blocks: Record<string, unknown>[] } };
    const start = saved.blocks.blocks[0];
    expect(start['type']).toBe('val_start');
    const show = (start['next'] as { block: Record<string, unknown> }).block;
    expect(show['type']).toBe('val_show');
    const add = (show['inputs'] as Record<string, { block: Record<string, unknown> }>)[VALUE_INPUT]
      .block;
    expect(add['type']).toBe('val_add');
    // Il workspace contiene tutti i blocchi del programma caricato.
    expect(workspaceOf(fixture).getAllBlocks(false)).toHaveLength(5);
  });

  it('JSON rifiutato: il workspace resta il progetto iniziale e nessuna modifica viene emessa', async () => {
    const fixture = await createWorkspace(initialJson);
    const emitted: string[] = [];
    fixture.componentInstance.workspaceChanged.subscribe((json: string) => {
      emitted.push(json);
    });

    expect(() => fixture.componentInstance.applyProgram('non è un programma')).toThrow(
      InvalidWorkspaceStateError,
    );
    // Tempo sufficiente perché eventuali eventi in coda arrivino al listener.
    await new Promise((resolve) => setTimeout(resolve, 50));

    const blocks = workspaceOf(fixture).getAllBlocks(false);
    expect(blocks).toHaveLength(1);
    expect(blocks[0].type).toBe('val_start');
    expect(emitted).toEqual([]);
  });
});

/** Monta il componente con un progetto dato e ne attende il montaggio di Blockly. */
async function createWorkspace(workspaceJson: string) {
  const fixture = TestBed.createComponent(ParticipantWorkspace);
  fixture.componentRef.setInput('workspaceJson', workspaceJson);
  fixture.componentRef.setInput('allowedBlocks', [...VALUE_PROGRAM_BLOCK_TYPES]);
  fixture.componentRef.setInput('engineVersion', VALUE_PROGRAM_ENGINE_VERSION);
  // La via assistita di collaudo lavora sul workspace editabile dell'alunno.
  fixture.componentRef.setInput('readOnly', false);
  fixture.detectChanges();
  // Il workspace nasce in afterNextRender: quandoStable lo porta a termine.
  await fixture.whenStable();
  return fixture;
}

function workspaceOf(fixture: ComponentFixture<ParticipantWorkspace>): Blockly.WorkspaceSvg {
  const workspace = (
    fixture.componentInstance as unknown as { workspace: Blockly.WorkspaceSvg | null }
  ).workspace;
  if (workspace === null) {
    throw new Error('Il workspace Blockly non è montato: la prova non è valida.');
  }
  return workspace;
}

/**
 * Attende che gli eventi Blockly, smaltiti in coda dopo la carica, raggiungano il
 * listener del componente e producano l'emissione.
 */
async function waitForEmitted(emitted: string[]): Promise<void> {
  for (let tick = 0; tick < 40 && emitted.length === 0; tick += 1) {
    await new Promise((resolve) => setTimeout(resolve, 5));
  }
}
