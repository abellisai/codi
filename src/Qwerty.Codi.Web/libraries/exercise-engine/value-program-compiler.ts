import {
  MAX_VALUE_INTEGER,
  type ValueComparisonOperator,
  type ValueExpression,
  type ValueInstruction,
} from './value-program-runner';

/**
 * Compila un progetto a blocchi di `valueprogram-1` nelle istruzioni che il motore esegue (§13.9).
 *
 * Il progetto è la forma con cui l'alunno **compone** il programma, le istruzioni sono la forma che
 * il motore **esegue**: sono due formati diversi, e questo è il punto in cui l'uno diventa l'altro.
 * La traduzione è totale: se qualcosa non si può tradurre, non si restituisce un programma a metà —
 * si restituiscono i problemi, e chi legge li mostra al bambino.
 *
 * Le diagnosi sono in italiano e nominano il blocco: «il blocco "porta a" vuole un numero dentro» è
 * una cosa che un bambino di otto anni può correggere, «compilazione fallita» no.
 */

/** Un problema che impedisce di eseguire il progetto, con il blocco che lo ha causato. */
export interface ProgramProblem {
  /** Identificativo del blocco, quando è noto: serve a evidenziarlo nell'editor. */
  readonly blockId: string | null;
  /** Che cosa non va, in una frase che nomina il blocco e ciò che manca. */
  readonly message: string;
}

/** Regole dell'esercizio che il progetto non porta con sé: nomi e valori iniziali delle variabili. */
export interface ValueProgramRules {
  readonly variables: readonly string[];
  readonly defaults: Readonly<Record<string, number>>;
}

/** Esito della compilazione: o un programma, o i problemi che ne impediscono l'esecuzione. */
export type CompileResult =
  { readonly instructions: ValueInstruction[] } | { readonly problems: ProgramProblem[] };

/** Blocco grezzo del documento Blockly: si legge quel che serve, il resto non si tocca. */
interface RawBlock {
  readonly type?: string;
  readonly id?: string;
  readonly fields?: Record<string, unknown>;
  readonly inputs?: Record<string, { block?: RawBlock } | undefined>;
  readonly next?: { block?: RawBlock } | undefined;
}

const COMMAND_TYPES = [
  'val_start',
  'val_set',
  'val_change',
  'val_show',
  'val_ask',
  'val_if',
  'val_repeat',
];
const EXPRESSION_TYPES = ['val_number', 'val_variable', 'val_answer', 'val_add', 'val_compare'];

/**
 * Compila il progetto. Un workspace vuoto non è un errore: è la condizione di partenza, e il
 * problema che ne nasce — manca il punto di ingresso — è quello che l'alunno deve leggere.
 */
export function compileValueProgram(
  workspaceJson: string,
  rules: ValueProgramRules,
): CompileResult {
  let documento: unknown;
  try {
    documento = JSON.parse(workspaceJson);
  } catch {
    return problemi([
      { blockId: null, message: 'Il progetto non si legge: non è nel formato atteso.' },
    ]);
  }

  const elenco = (documento as { blocks?: { blocks?: unknown } })?.blocks?.blocks;
  if (!Array.isArray(elenco) || elenco.length === 0) {
    return problemi([
      { blockId: null, message: 'Il programma è vuoto: comincia dal blocco «quando si avvia».' },
    ]);
  }

  // L'indice per identificativo serve a un solo scopo: dire **quale** blocco ha un problema, così
  // l'editor lo può evidenziare invece di lasciare il bambino a cercarlo.
  const perId = new Map<string, RawBlock>();
  indicizza(elenco as RawBlock[], perId);

  const avvii = (elenco as RawBlock[]).filter((blocco) => blocco.type === 'val_start');
  if (avvii.length === 0) {
    return problemi([
      {
        blockId: null,
        message: 'Manca il blocco «quando si avvia»: senza, il programma non parte.',
      },
    ]);
  }

  if (avvii.length > 1) {
    return problemi([
      {
        blockId: avvii[1]?.id ?? null,
        message: `Ci sono ${String(avvii.length)} blocchi «quando si avvia»: ne basta uno.`,
      },
    ]);
  }

  const contesto = new Compiler(rules, perId);
  const corpo = contesto.sequenza(avvii[0].next?.block);
  const raccolti = contesto.raccogli();
  if (raccolti.length > 0) return problemi(raccolti);

  return { instructions: corpo };
}

/** Compilatore: tiene i problemi raccolti e le regole dell'esercizio. */
class Compiler {
  private readonly problems: ProgramProblem[] = [];

  constructor(
    private readonly rules: ValueProgramRules,
    private readonly perId: Map<string, RawBlock>,
  ) {}

  raccogli(): ProgramProblem[] {
    return this.problems;
  }

  /** Compila una catena di comandi, seguendo `next` fino in fondo. */
  sequenza(primo: RawBlock | undefined): ValueInstruction[] {
    const istruzioni: ValueInstruction[] = [];
    let corrente = primo;
    let passi = 0;

    while (corrente !== undefined && passi < 1_000) {
      passi += 1;
      const istruzione = this.comando(corrente);
      if (istruzione !== null) istruzioni.push(istruzione);
      // La catena continua nel blocco appeso: `next` è una proprietà il cui valore è il blocco, non
      // il blocco stesso. Leggerlo senza scendere di un livello significa fermarsi al primo comando.
      corrente = corrente.next?.block;
    }

    return istruzioni;
  }

  private comando(blocco: RawBlock): ValueInstruction | null {
    const id = blocco.id ?? '';
    const tipo = blocco.type ?? '';

    switch (tipo) {
      case 'val_start':
        // Un secondo «quando si avvia» dentro la catena non è un inizio: è un blocco appoggiato dove
        // non si esegue.
        this.errore(id, 'Il blocco «quando si avvia» apre il programma: non si attacca in mezzo.');

        return null;

      case 'val_set': {
        const variabile = this.variabile(blocco);
        const valore = this.espressione(blocco.inputs?.['VALUE']?.block, id);
        if (variabile === null || valore === null) return null;

        return { kind: 'set', blockId: id, variable: variabile, value: valore };
      }

      case 'val_change': {
        // «cambia Punti di 1»: una variabile da cambiare e una quantità da sommare.
        const variabile = this.variabile(blocco);
        const delta = this.espressione(blocco.inputs?.['DELTA']?.block, id);
        if (variabile === null || delta === null) return null;

        return { kind: 'change', blockId: id, variable: variabile, delta };
      }

      case 'val_show': {
        const valore = this.espressione(blocco.inputs?.['VALUE']?.block, id);
        if (valore === null) return null;

        return { kind: 'show', blockId: id, value: valore };
      }

      case 'val_ask': {
        // «chiedi [domanda?] e attendi»: la domanda è testo libero, e la risposta resta nel reporter
        // «risposta» finché il programma non la memorizza con un altro blocco.
        const domanda = String(blocco.fields?.['QUESTION'] ?? '').trim();
        if (domanda.length === 0) {
          this.errore(id, 'Il blocco «chiedi» vuole la domanda da mostrare: scrivila.');

          return null;
        }

        return { kind: 'ask', blockId: id, variable: null, question: domanda };
      }

      case 'val_if': {
        const condizione = this.confronto(blocco.inputs?.['VALUE']?.block, id);
        if (condizione === null) return null;

        return {
          kind: 'if',
          blockId: id,
          condition: condizione,
          then: this.sequenza(blocco.inputs?.['DO']?.block),
          otherwise: this.sequenza(blocco.inputs?.['ELSE']?.block),
        };
      }

      case 'val_repeat': {
        const quante = this.numero(blocco, 'COUNT', 'quante volte ripetere');
        if (quante === null) return null;

        if (quante < 1) {
          this.errore(id, 'Un ciclo deve ripetere almeno una volta: scrivi un numero da 1 in su.');

          return null;
        }

        return {
          kind: 'repeat',
          blockId: id,
          count: Math.trunc(quante),
          body: this.sequenza(blocco.inputs?.['DO']?.block),
        };
      }

      default:
        this.errore(id, `Il blocco «${tipo}» non è un comando di questo esercizio.`);

        return null;
    }
  }

  private espressione(blocco: RawBlock | undefined, padre: string): ValueExpression | null {
    if (blocco === undefined) {
      this.errore(padre, 'Manca il valore dentro il blocco: trascinaci un numero o una variabile.');

      return null;
    }

    const id = blocco.id ?? '';
    const tipo = blocco.type ?? '';

    switch (tipo) {
      case 'val_number': {
        const valore = this.numero(blocco, 'NUM', 'il numero');
        if (valore === null) return null;

        if (Math.abs(valore) > MAX_VALUE_INTEGER) {
          this.errore(id, `Il numero supera il massimo ammesso (${String(MAX_VALUE_INTEGER)}).`);

          return null;
        }

        return { kind: 'literal', value: Math.trunc(valore) };
      }

      case 'val_variable': {
        const variabile = this.variabile(blocco);
        if (variabile === null) return null;

        return { kind: 'variable', variable: variabile };
      }

      case 'val_answer':
        // Il reporter «risposta»: non ha campi, perché la risposta non si sceglie.
        return { kind: 'answer' };

      case 'val_add': {
        const sinistra = this.espressione(blocco.inputs?.['VALUE']?.block, id);
        const destra = this.espressione(blocco.inputs?.['OTHER']?.block, id);
        if (sinistra === null || destra === null) return null;

        return { kind: 'add', left: sinistra, right: destra };
      }

      case 'val_compare':
        // Un confronto non è un valore: sta in un «se», e metterlo in una somma o in un'uscita non
        // vuol dire nulla. Il motore lo rifiuterebbe dopo; qui si dice prima.
        this.errore(id, 'Un confronto decide un ramo: non si può sommare né mostrare.');

        return null;

      default:
        this.errore(id, `Il blocco «${tipo}» non produce un valore in questo esercizio.`);

        return null;
    }
  }

  private confronto(
    blocco: RawBlock | undefined,
    padre: string,
  ): { left: ValueExpression; operator: ValueComparisonOperator; right: ValueExpression } | null {
    if (blocco === undefined || blocco.type !== 'val_compare') {
      this.errore(padre, 'Il blocco «se» vuole un confronto: trascinaci dentro «confronta».');

      return null;
    }

    const id = blocco.id ?? '';
    const sinistra = this.espressione(blocco.inputs?.['VALUE']?.block, id);
    const destra = this.espressione(blocco.inputs?.['OTHER']?.block, id);
    if (sinistra === null || destra === null) return null;

    const relazione = String(blocco.fields?.['OP'] ?? '');
    const operatore = relazioni[relazione];
    if (operatore === undefined) {
      this.errore(id, `La relazione «${relazione}» non è fra quelle previste.`);

      return null;
    }

    return { left: sinistra, operator: operatore, right: destra };
  }

  /** Nome di variabile dichiarato nell'esercizio: un nome fuori elenco è un programma da correggere. */
  private variabile(blocco: RawBlock): string | null {
    const id = blocco.id ?? '';
    const nome = String(blocco.fields?.['VAR'] ?? '').trim();

    if (nome.length === 0) {
      this.errore(id, 'Il blocco non dice su quale variabile lavorare: scrivine il nome.');

      return null;
    }

    if (!this.rules.variables.includes(nome)) {
      this.errore(
        id,
        `La variabile «${nome}» non è fra quelle dell'esercizio: ${this.rules.variables.join(', ')}.`,
      );

      return null;
    }

    return nome;
  }

  private numero(blocco: RawBlock, campo: string, che: string): number | null {
    const id = blocco.id ?? '';
    const grezzo = blocco.fields?.[campo];

    if (grezzo === undefined || grezzo === null || grezzo === '') {
      this.errore(id, `Manca ${che}.`);

      return null;
    }

    const valore = typeof grezzo === 'number' ? grezzo : Number.parseInt(String(grezzo), 10);
    if (Number.isNaN(valore)) {
      this.errore(id, `Il valore di ${che} non è un numero: hai scritto «${String(grezzo)}».`);

      return null;
    }

    return valore;
  }

  private errore(blockId: string, message: string): void {
    // Un blocco già segnalato non si segnala due volte: due problemi per un blocco solo confondono
    // chi legge più di quanto aiutino.
    if (this.problems.some((problema) => problema.blockId === blockId && blockId !== '')) return;

    this.problems.push({ blockId: blockId === '' ? null : blockId, message });
  }
}

const relazioni: Record<string, ValueComparisonOperator> = {
  Greater: 'Greater',
  Less: 'Less',
  Equal: 'Equal',
};

function problemi(problems: ProgramProblem[]): CompileResult {
  return { problems };
}

/** Indicizza i blocchi per identificativo, scendendo in tutte le sequenze e gli ingressi. */
function indicizza(elenco: RawBlock[], perId: Map<string, RawBlock>): void {
  for (const blocco of elenco) {
    if (typeof blocco.id === 'string') perId.set(blocco.id, blocco);

    if (blocco.next?.block) indicizza([blocco.next.block], perId);
    for (const ingresso of Object.values(blocco.inputs ?? {})) {
      if (ingresso?.block) indicizza([ingresso.block], perId);
    }
  }
}

/** Nomi dei tipi di comando ed espressione, esposti perché i test li possano percorrere. */
export const VALUE_COMMAND_TYPES = COMMAND_TYPES;
export const VALUE_EXPRESSION_TYPES = EXPRESSION_TYPES;
