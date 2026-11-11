import * as Blockly from 'blockly';

/**
 * Blocco del prototipo di T0: serve a provare caricamento e round trip del workspace.
 * La scena GridPath e i suoi blocchi appartengono a T6 e sostituiranno questo tipo.
 */
export const PROTOTYPE_BLOCK_TYPE = 'codi_prototype_step';

let registered = false;

/** Registra il blocco del prototipo. Va invocata prima di montare il workspace. */
export function registerPrototypeBlock(): void {
  // La definizione vive in un registro globale di Blockly: registrarla di nuovo a ogni
  // navigazione non aggiunge nulla e produrrebbe solo avvisi.
  if (registered) {
    return;
  }

  Blockly.common.defineBlocksWithJsonArray([
    {
      type: PROTOTYPE_BLOCK_TYPE,
      message0: 'passo %1',
      args0: [
        {
          type: 'field_input',
          name: 'DIRECTION',
          text: 'avanti',
        },
      ],
      previousStatement: null,
      nextStatement: null,
      colour: 210,
      tooltip: 'Blocco del prototipo T0, non didattico.',
    },
  ]);

  registered = true;
}
