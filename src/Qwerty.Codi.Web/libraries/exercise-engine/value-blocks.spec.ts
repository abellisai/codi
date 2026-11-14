import * as Blockly from 'blockly';
import {
  COMPARE_FIELD,
  COURSE_VARIABLES,
  NUMBER_FIELD,
  OTHER_INPUT,
  VALUE_INPUT,
  VARIABLE_FIELD,
  registerValueProgramBlocks,
  resetValueProgramVariables,
  setValueProgramVariables,
  valueProgramVariableOptions,
} from './value-blocks';
import { compileValueProgram } from './value-program-compiler';
import { deserializeWorkspaceJson, serializeWorkspace } from './workspace-serialization';

registerValueProgramBlocks();

/**
 * Il menu a tendina delle variabili ValueProgram (§13.9). Le opzioni vengono lette all'apertura del
 * menu dalla lista attiva, che il chiamante imposta dalla definizione dell'esercizio. Il server
 * accetta solo le variabili dichiarate: un menu fermo alla lista del corso renderebbe l'esercizio
 * non eseguibile dall'UI.
 */
describe('Variable dei blocchi ValueProgram', () => {
  afterEach(() => {
    // La lista attiva è stato del modulo: un caso che la lasciasse compromessa
    // contaminerebbe i successivi e i workspace montati altrove (§20.3).
    resetValueProgramVariables();
  });

  it('menu con le variabili dichiarate: offre solo quelle', () => {
    setValueProgramVariables(['Somma']);

    const workspace = new Blockly.Workspace();
    const reporter = workspace.newBlock('val_variable');
    const setter = workspace.newBlock('val_set');
    const changer = workspace.newBlock('val_change');

    for (const block of [reporter, setter, changer]) {
      const field = block.getField(VARIABLE_FIELD) as Blockly.FieldDropdown;

      expect(field.getOptions().map((option) => option[1])).toEqual(['Somma']);
      expect(field.getValue()).toBe('Somma');
    }

    workspace.dispose();
  });

  it('definizione senza variabili: il menu tiene la lista del corso', () => {
    setValueProgramVariables([]);

    const workspace = new Blockly.Workspace();
    const reporter = workspace.newBlock('val_variable');
    const field = reporter.getField(VARIABLE_FIELD) as Blockly.FieldDropdown;

    expect(field.getOptions().map((option) => option[1])).toEqual([...COURSE_VARIABLES]);
    expect(field.getValue()).toBe('Punti');

    workspace.dispose();
  });

  it('esercizio successivo senza variabili: la lista non resta contaminata', () => {
    setValueProgramVariables(['Somma']);
    expect(valueProgramVariableOptions().map((option) => option[1])).toEqual(['Somma']);

    setValueProgramVariables([]);
    const options = valueProgramVariableOptions().map((option) => option[1]);

    expect(options).toEqual([...COURSE_VARIABLES]);
    expect(options).not.toContain('Somma');
  });

  it('nomi con spazi e duplicati: la lista si normalizza', () => {
    setValueProgramVariables([' Somma ', 'Somma', '  ', 'Punteggio']);

    expect(valueProgramVariableOptions().map((option) => option[1])).toEqual([
      'Somma',
      'Punteggio',
    ]);
  });

  it('round trip con variabile dichiarata: il campo salvato arriva alla compilazione', () => {
    setValueProgramVariables(['Somma']);

    const source = new Blockly.Workspace();
    const start = source.newBlock('val_start');
    const set = source.newBlock('val_set');
    set.setFieldValue('Somma', VARIABLE_FIELD);
    const add = source.newBlock('val_add');
    const two = source.newBlock('val_number');
    two.setFieldValue(2, NUMBER_FIELD);
    const three = source.newBlock('val_number');
    three.setFieldValue(3, NUMBER_FIELD);
    const show = source.newBlock('val_show');
    const somma = source.newBlock('val_variable');

    connectStatement(start, set);
    connectInput(set, VALUE_INPUT, add);
    connectInput(add, VALUE_INPUT, two);
    connectInput(add, OTHER_INPUT, three);
    connectStatement(set, show);
    connectInput(show, VALUE_INPUT, somma);

    const saved = serializeWorkspace(source);

    const target = new Blockly.Workspace();
    deserializeWorkspaceJson(saved, target);

    const loaded = target.getBlockById(set.id);
    expect(loaded?.getFieldValue(VARIABLE_FIELD)).toBe('Somma');

    // Il compilatore del pannello alunno accetta lo stesso stato con le stesse variabili: il campo
    // serializzato è esattamente quello che anche il server si aspetta.
    const compilato = compileValueProgram(saved, { variables: ['Somma'], defaults: { Somma: 0 } });
    expect('problems' in compilato).toBe(false);

    source.dispose();
    target.dispose();
  });

  it('gli altri menu del catalogo restano fermi', () => {
    const workspace = new Blockly.Workspace();
    const compare = workspace.newBlock('val_compare');
    const field = compare.getField(COMPARE_FIELD) as Blockly.FieldDropdown;

    expect(field.getOptions().map((option) => option[0])).toEqual([
      'maggiore di',
      'minore di',
      'uguale a',
    ]);

    workspace.dispose();
  });
});

/** Attacca un blocco in catena sotto un altro, come farebbe il trascinamento. */
function connectStatement(previous: Blockly.Block, next: Blockly.Block): void {
  const from = previous.nextConnection;
  const to = next.previousConnection;
  if (from === null || to === null) {
    throw new Error('Il blocco non ha la connessione di catena: la prova non è valida.');
  }
  from.connect(to);
}

/** Inserisce un blocco espressione nell'ingresso di valore indicato. */
function connectInput(owner: Blockly.Block, inputName: string, child: Blockly.Block): void {
  const input = owner.getInput(inputName);
  if (
    input?.connection === undefined ||
    input.connection === null ||
    child.outputConnection === null
  ) {
    throw new Error(`L'ingresso ${inputName} non è disponibile: la prova non è valida.`);
  }
  input.connection.connect(child.outputConnection);
}
