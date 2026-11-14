import * as Blockly from 'blockly';

/**
 * Catalogo dei blocchi della versione `valueprogram-1` (§13.9). I nomi dei tipi, dei campi e degli
 * ingressi devono coincidere con quelli del dominio .NET (`ValueBlockCatalog`), perché il server
 * verifica il progetto che l'editor produce: due nomi diversi per lo stesso blocco significherebbero
 * un progetto salvato che il server rifiuta.
 *
 * Il catalogo ha due famiglie. I **comandi** stanno in una catena e si eseguono in ordine; le
 * **espressioni** si annidano dentro un ingresso di valore e producono un valore. La distinzione si
 * vede nella forma di ogni blocco: un comando ha `previousStatement`/`nextStatement`, un'espressione
 * ha `output`.
 *
 * Il vocabolario è quello del corso, non un mio equivalente: «quando si clicca sulla bandiera
 * verde», «chiedi [domanda?] e attendi», «porta … a (risposta)», «cambia … di …», «dire …». Un bambino
 * che ha visto questi blocchi in Scratch li deve riconoscere qui, altrimenti sta imparando due lingue
 * per la stessa idea.
 */
export const VALUE_PROGRAM_ENGINE_VERSION = 'valueprogram-1';
export const VALUE_PROGRAM_EXERCISE_TYPE = 'ValueProgram';

/** Tipi dei blocchi di questa versione, nell'ordine in cui si offrono nel cassetto. */
export const VALUE_PROGRAM_BLOCK_TYPES = [
  'val_start',
  'val_set',
  'val_change',
  'val_show',
  'val_ask',
  'val_if',
  'val_repeat',
  'val_number',
  'val_variable',
  'val_answer',
  'val_add',
  'val_compare',
] as const;

/** Tipo di un blocco del catalogo. */
export type ValueProgramBlockType = (typeof VALUE_PROGRAM_BLOCK_TYPES)[number];

/** Campo con il nome della variabile assegnata, cambiata o letta. */
export const VARIABLE_FIELD = 'VAR';

/** Campo con la domanda che «chiedi … e attendi» mostra mentre aspetta. */
export const QUESTION_FIELD = 'QUESTION';

/** Campo con la quantità di «cambia … di …». */
export const DELTA_FIELD = 'DELTA';

/** Campo con il numero scritto in un blocco numerico. */
export const NUMBER_FIELD = 'NUM';

/** Campo con il numero di ripetizioni di un ciclo contato. */
export const REPEAT_COUNT_FIELD = 'COUNT';

/** Campo con la relazione di un confronto. */
export const COMPARE_FIELD = 'OP';

/** Ingresso con il valore da assegnare, mostrare, sommare o confrontare. */
export const VALUE_INPUT = 'VALUE';

/** Secondo valore di una somma o di un confronto. */
export const OTHER_INPUT = 'OTHER';

/** Ingresso con la sequenza eseguita quando il confronto è vero. */
export const THEN_INPUT = 'DO';

/** Ingresso con la sequenza eseguita quando il confronto è falso. */
export const ELSE_INPUT = 'ELSE';

/** Conteggio minimo e massimo di un ciclo contato. */
export const MIN_REPEAT_COUNT = 1;
export const MAX_REPEAT_COUNT = 50;

/** Valore più grande ammesso in un numero scritto. */
export const MAX_INTEGER = 1_000_000;

/**
 * Variabili che il blocco offre quando la definizione non ne dichiara nessuna.
 *
 * I nomi sono quelli del corso — `Punti` per la macchina dei punti, `Numero` per la carta appena
 * letta, `A`, `B`, `Totale` per la somma — così il bambino trova il blocco pronto invece di dover
 * scrivere un nome a mano. È anche il fallback: un esercizio che dichiara variabili le mostra tutte
 * e non queste, perché il server accetta soltanto le variabili dichiarate e offrire un nome fuori
 * elenco sarebbe un progetto destinato al rifiuto.
 */
export const COURSE_VARIABLES: readonly string[] = ['Punti', 'Numero', 'Totale', 'A', 'B'];

/** Variabili attive nei menu: lo stato è del modulo, non del singolo workspace. */
let activeVariables: readonly string[] = COURSE_VARIABLES;

/**
 * Imposta le variabili dichiarate dall'esercizio aperto: i menu dei blocchi con variabile offrono
 * solo queste. Una lista vuota ripristina `COURSE_VARIABLES`, così un esercizio senza variabili
 * mantiene il comportamento precedente. La lista è uno stato del modulo: chi monta un workspace la
 * imposta **prima** di caricare o aggiornare i blocchi, e ogni definizione la reimposta — una
 * definizione caricata non deve contaminare il workspace di un esercizio successivo.
 */
export function setValueProgramVariables(variables: readonly string[]): void {
  const declared = [
    ...new Set(variables.map((name) => name.trim()).filter((name) => name.length > 0)),
  ];

  activeVariables = declared.length > 0 ? declared : COURSE_VARIABLES;
}

/** Ripristina la lista del corso: la usano i test e chi non ha più un esercizio aperto. */
export function resetValueProgramVariables(): void {
  activeVariables = COURSE_VARIABLES;
}

/** Opzioni correnti del menu a tendina: etichetta e valore hanno lo stesso nome della variabile. */
export function valueProgramVariableOptions(): Blockly.MenuOption[] {
  return activeVariables.map((name) => [name, name]);
}

/** Rappresentazione di una relazione di confronto: valore per il server, etichetta per il bambino. */
const COMPARISON_OPTIONS: readonly (readonly [string, string])[] = [
  ['maggiore di', 'Greater'],
  ['minore di', 'Less'],
  ['uguale a', 'Equal'],
];

let registered = false;

/**
 * Registra i blocchi del catalogo. La definizione vive in un registro globale di Blockly: registrarla
 * di nuovo a ogni navigazione non aggiunge nulla, e registrare due volte lo stesso tipo è un errore.
 *
 * I tre blocchi con il menu della variabile sono registrati a mano, non dall'array JSON: il menu di
 * una definizione JSON è una lista ferma alla registrazione, mentre qui le opzioni devono essere
 * lette **all'apertura** del menu, quando le variabili dell'esercizio sono già note. Gli altri blocchi
 * restano nell'array JSON perché le loro opzioni non cambiano mai.
 */
export function registerValueProgramBlocks(): void {
  if (registered) {
    return;
  }

  Blockly.Blocks['val_set'] = {
    init(this: Blockly.Block) {
      this.appendValueInput(VALUE_INPUT)
        .appendField('porta')
        .appendField(new Blockly.FieldDropdown(valueProgramVariableOptions), VARIABLE_FIELD)
        .appendField('a');
      this.setPreviousStatement(true, null);
      this.setNextStatement(true, null);
      this.setColour(230);
      this.setTooltip('Conserva un valore in una variabile.');
      this.setHelpUrl('');
    },
  };

  Blockly.Blocks['val_change'] = {
    init(this: Blockly.Block) {
      // «cambia Punti di 1»: un blocco del corso, e non una scorciatoia per «porta Punti a Punti + 1».
      // Non ripete il nome della variabile, che è la forma in cui un bambino lo sbaglia.
      this.appendValueInput(DELTA_FIELD)
        .appendField('cambia')
        .appendField(new Blockly.FieldDropdown(valueProgramVariableOptions), VARIABLE_FIELD)
        .appendField('di');
      this.setPreviousStatement(true, null);
      this.setNextStatement(true, null);
      this.setColour(230);
      this.setTooltip('Somma una quantità al valore che la variabile ha in questo momento.');
      this.setHelpUrl('');
    },
  };

  Blockly.Blocks['val_variable'] = {
    init(this: Blockly.Block) {
      // Il reporter di una variabile: nel corso si trascina nel foro di un blocco, e mostra il nome in
      // una forma ovale. Un campo di testo lascerebbe il bambino scrivere un nome che non esiste; una
      // scelta fra le variabili dichiarate glielo impedisce.
      this.appendDummyInput().appendField(
        new Blockly.FieldDropdown(valueProgramVariableOptions),
        VARIABLE_FIELD,
      );
      this.setOutput(true, null);
      this.setColour(330);
      this.setTooltip('Il valore che la variabile ha in questo momento.');
      this.setHelpUrl('');
    },
  };

  Blockly.common.defineBlocksWithJsonArray([
    {
      type: 'val_start',
      // Il vocabolario del corso: «quando si clicca sulla bandiera verde». Il punto di ingresso apre
      // la catena e non si attacca sotto un altro blocco, così il programma ha un inizio
      // riconoscibile senza che il server debba indovinarlo.
      message0: '🏁 quando si clicca sulla bandiera verde',
      previousStatement: null,
      nextStatement: null,
      colour: 120,
      tooltip: 'Il programma comincia quando si clicca sulla bandiera verde.',
      helpUrl: '',
    },
    {
      type: 'val_show',
      // «dire», il blocco con cui il corso mostra il risultato.
      message0: 'dire %1',
      args0: [{ type: 'input_value', name: VALUE_INPUT }],
      previousStatement: null,
      nextStatement: null,
      colour: 20,
      tooltip: 'Fa vedere un valore: è il risultato del programma.',
      helpUrl: '',
    },
    {
      type: 'val_ask',
      // «chiedi [Numero della carta?] e attendi»: la domanda è testo libero, e la risposta si usa con
      // il reporter «risposta» — non si assegna qui.
      message0: 'chiedi %1 e attendi',
      args0: [{ type: 'field_input', name: QUESTION_FIELD, text: 'Numero della carta?' }],
      previousStatement: null,
      nextStatement: null,
      colour: 160,
      tooltip: 'Legge un valore: il programma si ferma finché non arriva una risposta.',
      helpUrl: '',
    },
    {
      type: 'val_if',
      message0: 'se %1',
      args0: [{ type: 'input_value', name: VALUE_INPUT }],
      message1: 'allora %1',
      args1: [{ type: 'input_statement', name: THEN_INPUT }],
      message2: 'altrimenti %1',
      args2: [{ type: 'input_statement', name: ELSE_INPUT }],
      previousStatement: null,
      nextStatement: null,
      colour: 210,
      tooltip: 'Esegue i blocchi interni solo se il confronto è vero.',
      helpUrl: '',
    },
    {
      type: 'val_repeat',
      message0: 'ripeti %1 volte',
      args0: [
        {
          type: 'field_number',
          name: REPEAT_COUNT_FIELD,
          value: 3,
          min: MIN_REPEAT_COUNT,
          max: MAX_REPEAT_COUNT,
          precision: 1,
        },
      ],
      message1: 'fai %1',
      args1: [{ type: 'input_statement', name: 'DO' }],
      previousStatement: null,
      nextStatement: null,
      colour: 40,
      tooltip: `Ripete i blocchi interni, da ${String(MIN_REPEAT_COUNT)} a ${String(MAX_REPEAT_COUNT)} volte.`,
      helpUrl: '',
    },
    {
      type: 'val_number',
      message0: '%1',
      args0: [
        {
          type: 'field_number',
          name: NUMBER_FIELD,
          value: 1,
          min: -MAX_INTEGER,
          max: MAX_INTEGER,
          precision: 1,
        },
      ],
      output: null,
      colour: 330,
      tooltip: 'Un numero scritto nel programma.',
      helpUrl: '',
    },
    {
      type: 'val_answer',
      // Il reporter «risposta»: non ha campi perché la risposta non si sceglie — è ciò che l'ultima
      // lettura ha prodotto.
      message0: 'risposta',
      output: null,
      colour: 160,
      tooltip: 'L’ultima risposta data alla domanda. Chiedere di nuovo la sostituisce.',
      helpUrl: '',
    },
    {
      type: 'val_add',
      message0: '%1 + %2',
      args0: [
        { type: 'input_value', name: VALUE_INPUT },
        { type: 'input_value', name: OTHER_INPUT },
      ],
      output: null,
      colour: 330,
      tooltip: 'La somma di due valori.',
      helpUrl: '',
    },
    {
      type: 'val_compare',
      message0: '%1 %2 %3',
      args0: [
        { type: 'input_value', name: VALUE_INPUT },
        {
          type: 'field_dropdown',
          name: COMPARE_FIELD,
          options: COMPARISON_OPTIONS.map(([label, value]) => [label, value]),
        },
        { type: 'input_value', name: OTHER_INPUT },
      ],
      output: null,
      colour: 210,
      tooltip: 'Un confronto: decide quale ramo si esegue.',
      helpUrl: '',
    },
  ]);

  registered = true;
}

/**
 * Cassetto dei blocchi disponibili: solo quelli che la definizione mette a disposizione, nell'ordine
 * del catalogo. Il cassetto è una scelta dell'editor; ciò che il progetto può contenere lo decide
 * comunque il server, quindi un blocco trascinato per errore non entra in una bozza valida.
 */
export function buildValueProgramToolbox(
  allowedBlocks: readonly string[],
): Blockly.utils.toolbox.ToolboxDefinition {
  return {
    kind: 'flyoutToolbox',
    contents: VALUE_PROGRAM_BLOCK_TYPES.filter((type) => allowedBlocks.includes(type)).map(
      (type) => ({
        kind: 'block',
        type,
      }),
    ),
  };
}
