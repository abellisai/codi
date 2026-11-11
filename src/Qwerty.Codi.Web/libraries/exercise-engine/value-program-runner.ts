/**
 * Forme ed esecuzione della versione `valueprogram-1` (§13.9) nel browser.
 *
 * È il secondo modello di esecuzione del prodotto. In `gridpath-1` lo stato è dove si trova un
 * personaggio su una griglia e la corsa finisce al traguardo; qui lo stato è un **insieme di
 * variabili con il loro valore**, l'esito è quello che il programma **mostra**, e la corsa può
 * fermarsi **in attesa di un valore** che l'alunno deve fornire.
 *
 * La differenza che conta è la sospensione: quando l'istruzione chiede un valore che non c'è, la
 * corsa si ferma lì, con le variabili come sono, e riprende dallo stesso punto quando il valore
 * arriva. Non si ricomincia: è il senso di conservare un valore.
 *
 * Il motore .NET ha le stesse regole e le stesse forme (`ValueProgramRunner`), e la versione del
 * motore è il nome con cui entrambe le dichiarano: due implementazioni che divergessero
 * distribuirebbero un esercizio che una delle due non sa svolgere.
 */

/** Versione del motore: la stessa che dichiara il catalogo dei blocchi. */
export const VALUE_PROGRAM_ENGINE_VERSION = 'valueprogram-1';

/** Versione dello schema di definizione compresa da questa versione del motore. */
export const VALUE_PROGRAM_SCHEMA_VERSION = 1;

/** Versione dello schema del checkpoint: è 1 **di questa versione**, non un numero globale. */
export const VALUE_PROGRAM_CHECKPOINT_SCHEMA_VERSION = 1;

/** Valore intero più grande ammesso in una variabile o in un confronto. */
export const MAX_VALUE_INTEGER = 1_000_000;

/** Stato di una corsa. */
export type ValueRunStatus =
  'Ready' | 'WaitingForValue' | 'Finished' | 'Refused' | 'BudgetExhausted';

/** Un'espressione che produce un valore. */
export type ValueExpression =
  | { readonly kind: 'literal'; readonly value: number }
  | { readonly kind: 'variable'; readonly variable: string }
  /**
   * Il reporter «risposta»: l'ultimo valore letto. Non è una variabile del docente, e chiedere di
   * nuovo la sostituisce mentre le variabili conservano i valori memorizzati — è la differenza che
   * l'esercizio della somma mette alla prova.
   */
  | { readonly kind: 'answer' }
  | { readonly kind: 'add'; readonly left: ValueExpression; readonly right: ValueExpression };

/** Le relazioni che il motore sa decidere. */
export type ValueComparisonOperator = 'Greater' | 'Less' | 'Equal';

/** Un confronto: non produce un numero, decide un ramo. */
export interface ValueComparison {
  readonly left: ValueExpression;
  readonly operator: ValueComparisonOperator;
  readonly right: ValueExpression;
}

/** Un'istruzione del programma da eseguire. */
export type ValueInstruction =
  | {
      readonly kind: 'set';
      readonly blockId: string;
      readonly variable: string;
      readonly value: ValueExpression;
    }
  | {
      /**
       * «cambia Punti di 1»: legge il valore, somma e riscrive. È un'istruzione propria e non una
       * scorciatoia per «porta Punti a Punti + 1», perché nel corso è un altro blocco.
       */
      readonly kind: 'change';
      readonly blockId: string;
      readonly variable: string;
      readonly delta: ValueExpression;
    }
  | { readonly kind: 'show'; readonly blockId: string; readonly value: ValueExpression }
  | {
      /**
       * «chiedi [domanda?] e attendi». La variabile è facoltativa: nel corso la risposta resta nel
       * reporter «risposta» e si memorizza con un blocco a parte.
       */
      readonly kind: 'ask';
      readonly blockId: string;
      readonly variable: string | null;
      readonly question: string;
    }
  | {
      readonly kind: 'if';
      readonly blockId: string;
      readonly condition: ValueComparison;
      readonly then: readonly ValueInstruction[];
      readonly otherwise: readonly ValueInstruction[];
    }
  | {
      readonly kind: 'repeat';
      readonly blockId: string;
      readonly count: number;
      readonly body: readonly ValueInstruction[];
    };

/** Un fotogramma dello stack: dove siamo dentro una sequenza. */
export interface ValueFrame {
  readonly blockId: string;
  readonly index: number;
}

/**
 * Stato serializzabile di una corsa: è il checkpoint di `valueprogram-1` (§13.7). Contiene
 * l'ambiente delle variabili, l'uscita prodotta, i valori ancora da consumare e lo stack di
 * esecuzione. Non contiene il programma: un checkpoint vale per il progetto da cui è nato.
 */
export interface ValueRunState {
  readonly schemaVersion: number;
  readonly engineVersion: string;
  readonly status: ValueRunStatus;
  readonly frames: readonly ValueFrame[];
  readonly variables: Readonly<Record<string, number>>;
  /** La risposta all'ultima lettura: è il reporter «risposta», e sta a parte dalle variabili. */
  readonly answer: number;
  readonly output: readonly string[];
  readonly pendingValues: readonly number[];
  readonly consumedSteps: number;
  readonly executions: number;
  readonly activeBlockId: string;
  readonly pauseReason: string;
}

/** Un passo: lo stato raggiunto e la diagnosi, vuota quando è andato bene. */
export interface ValueStepResult {
  readonly state: ValueRunState;
  readonly problem: string;
}

/** Vero se la corsa può ancora avanzare, con o senza un valore dall'esterno. */
export function canContinue(state: ValueRunState): boolean {
  return state.status === 'Ready' || state.status === 'WaitingForValue';
}

/**
 * Checkpoint di una corsa: è lo stato, e la forma è la stessa. Il nome distingue **quando** il valore
 * è stato preso — a un confine di passo, per essere ripreso — da quando è soltanto osservato per
 * disegnare. Un checkpoint si riprende solo se è della stessa versione del motore e ha la forma
 * attesa (§13.7).
 */
export type ValueProgramCheckpointState = ValueRunState;

/** Un checkpoint che non si può riprendere: il formato non è quello di questa versione. */
export class ValueProgramCheckpointError extends Error {}

const STATUSES: readonly ValueRunStatus[] = [
  'Ready',
  'WaitingForValue',
  'Finished',
  'Refused',
  'BudgetExhausted',
];

/**
 * Converte un valore in un checkpoint verificato. Rifiuta ciò che non è un checkpoint, un formato di
 * un'altra versione del motore e uno stato internamente incoerente: un checkpoint si riprende dalla
 * stessa istruzione, e un percorso che non esiste in questo progetto porterebbe a riprendere altrove.
 */
export function parseValueProgramCheckpointState(
  value: unknown,
  engineVersion: string = VALUE_PROGRAM_ENGINE_VERSION,
): ValueProgramCheckpointState {
  if (!isRecord(value)) throw new ValueProgramCheckpointError('Il checkpoint non è un oggetto.');

  if (value['schemaVersion'] !== VALUE_PROGRAM_CHECKPOINT_SCHEMA_VERSION) {
    throw new ValueProgramCheckpointError(
      `Il checkpoint usa lo schema ${String(value['schemaVersion'])}, che questa versione non conosce.`,
    );
  }

  if (value['engineVersion'] !== engineVersion) {
    throw new ValueProgramCheckpointError(
      'Il checkpoint appartiene a un’altra versione del motore.',
    );
  }

  const status = value['status'];
  if (typeof status !== 'string' || !STATUSES.includes(status as ValueRunStatus)) {
    throw new ValueProgramCheckpointError(
      `Lo stato «${String(status)}» non è uno di quelli previsti.`,
    );
  }

  const framesValue = value['frames'];
  if (!Array.isArray(framesValue)) {
    throw new ValueProgramCheckpointError('Il checkpoint non contiene lo stack di esecuzione.');
  }

  const frames: ValueFrame[] = framesValue.map((frame, index) => {
    if (!isRecord(frame) || typeof frame['blockId'] !== 'string') {
      throw new ValueProgramCheckpointError(`Il fotogramma ${String(index)} non ha un blocco.`);
    }

    const position = frame['index'];
    if (typeof position !== 'number' || !Number.isInteger(position) || position < 0) {
      throw new ValueProgramCheckpointError(
        `Il fotogramma ${String(index)} non ha una posizione valida.`,
      );
    }

    return { blockId: frame['blockId'], index: position };
  });

  return {
    schemaVersion: VALUE_PROGRAM_CHECKPOINT_SCHEMA_VERSION,
    engineVersion,
    status: status as ValueRunStatus,
    frames,
    variables: readNumbers(value['variables'], 'Le variabili'),
    // La risposta è uno stato della corsa come le variabili: un checkpoint senza di essa
    // riprenderebbe con una risposta diversa da quella che il programma aveva letto.
    answer: readCount(value['answer'] ?? 0, 'La risposta'),
    output: readTexts(value['output'], 'L’uscita'),
    pendingValues: readNumberList(value['pendingValues'], 'I valori da consumare'),
    consumedSteps: readCount(value['consumedSteps'], 'I passi consumati'),
    executions: readCount(value['executions'], 'Le esecuzioni'),
    activeBlockId: typeof value['activeBlockId'] === 'string' ? value['activeBlockId'] : '',
    pauseReason: typeof value['pauseReason'] === 'string' ? value['pauseReason'] : '',
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function readCount(value: unknown, what: string): number {
  if (typeof value !== 'number' || !Number.isInteger(value) || value < 0) {
    throw new ValueProgramCheckpointError(`${what} non sono un numero valido.`);
  }

  return value;
}

function readNumberList(value: unknown, what: string): number[] {
  if (!Array.isArray(value)) throw new ValueProgramCheckpointError(`${what} non sono un elenco.`);

  return value.map((item) => {
    if (typeof item !== 'number' || !Number.isInteger(item)) {
      throw new ValueProgramCheckpointError(`${what} contengono un valore che non è un numero.`);
    }

    return item;
  });
}

function readNumbers(value: unknown, what: string): Record<string, number> {
  if (!isRecord(value)) throw new ValueProgramCheckpointError(`${what} non sono un oggetto.`);

  const result: Record<string, number> = {};
  for (const [name, item] of Object.entries(value)) {
    if (typeof item !== 'number' || !Number.isInteger(item)) {
      throw new ValueProgramCheckpointError(`${what}: «${name}» non è un numero intero.`);
    }

    result[name] = item;
  }

  return result;
}

function readTexts(value: unknown, what: string): string[] {
  if (!Array.isArray(value)) throw new ValueProgramCheckpointError(`${what} non è un elenco.`);

  return value.map((item) => {
    if (typeof item !== 'string') {
      throw new ValueProgramCheckpointError(`${what} contiene una riga che non è testo.`);
    }

    return item;
  });
}

/** Fotogramma di lavoro: il percorso salvato ne conserva solo indice e provenienza. */
interface WorkFrame {
  readonly instructions: readonly ValueInstruction[];
  readonly blockId: string;
  readonly kind: 'root' | 'body' | 'otherwise' | 'loop';
  /** Dove riprende il genitore quando questa sequenza è conclusa. */
  readonly returnIndex: number;
  index: number;
  /** Giri ancora da fare quando il fotogramma è il corpo di un ciclo. */
  remaining: number;
}

/** Identificativo del ramo «altrimenti»: dice **quale** ramo si stava eseguendo. */
function otherwiseId(blockId: string): string {
  return `${blockId}:else`;
}

function kindOf(blockId: string): WorkFrame['kind'] {
  if (blockId.endsWith(':else')) return 'otherwise';
  return blockId.length === 0 ? 'root' : 'body';
}

/** La sequenza che un'istruzione contiene, se ne contiene una. */
function nextSequence(instruction: ValueInstruction, frameId: string): readonly ValueInstruction[] {
  if (instruction.kind === 'if') {
    return frameId === otherwiseId(instruction.blockId) ? instruction.otherwise : instruction.then;
  }
  return instruction.kind === 'repeat' ? instruction.body : [];
}

/**
 * Esegue un programma che conserva valori, **un passo alla volta**.
 *
 * Un passo consuma budget solo se esegue qualcosa: chiudere una sequenza conclusa è contabilità
 * dello stack, non esecuzione. Un ciclo di tre giri costa nove passi, quanti ne esegue.
 */
export class ValueProgramRunner {
  private frames: WorkFrame[] = [];
  private variables: Record<string, number> = {};
  private assigned = new Set<string>();
  /** La risposta all'ultima lettura: è il reporter «risposta», e sta a parte dalle variabili. */
  private answer = 0;
  private output: string[] = [];
  private pending: number[];
  private status: ValueRunStatus = 'Ready';
  private consumed = 0;
  private executions = 0;
  private activeBlockId = '';
  private pauseReason = '';

  private constructor(
    private readonly variableNames: readonly string[],
    private readonly defaults: Readonly<Record<string, number>>,
    pendingValues: readonly number[],
    private readonly maxSteps: number,
    private readonly program: readonly ValueInstruction[],
  ) {
    // I valori si consumano dal primo all'ultimo: lo stato li conserva in quest'ordine.
    this.pending = [...pendingValues];
  }

  /** Avvia una corsa nuova da zero. */
  static start(
    program: readonly ValueInstruction[],
    variableNames: readonly string[],
    defaults: Readonly<Record<string, number>>,
    parameters: readonly number[],
    maxSteps: number,
  ): ValueProgramRunner {
    const runner = new ValueProgramRunner(variableNames, defaults, parameters, maxSteps, program);
    runner.reset();

    return runner;
  }

  /** Riprende una corsa da uno stato salvato, ricostruendo lo stack dal percorso. */
  static resume(
    program: readonly ValueInstruction[],
    variableNames: readonly string[],
    defaults: Readonly<Record<string, number>>,
    state: ValueRunState,
    maxSteps: number,
  ): ValueProgramRunner {
    const runner = new ValueProgramRunner(
      variableNames,
      defaults,
      state.pendingValues,
      maxSteps,
      program,
    );

    runner.variables = { ...state.variables };
    // Tutte le variabili di uno stato ripreso sono state assegnate: altrimenti non sarebbero lì.
    for (const name of Object.keys(state.variables)) runner.assigned.add(name);
    runner.answer = state.answer ?? 0;
    runner.output = [...state.output];
    runner.consumed = state.consumedSteps;
    runner.executions = state.executions;
    runner.pauseReason = state.pauseReason;
    // La corsa riprende in cammino: uno stato di attesa con i valori disponibili non è più
    // un'attesa, altrimenti il primo passo si rifiuterebbe di muoversi.
    runner.status =
      state.status === 'WaitingForValue' && runner.pending.length > 0 ? 'Ready' : state.status;
    runner.reEnter(state.frames);

    return runner;
  }

  /** Stato corrente, pronto per essere osservato o salvato. */
  get state(): ValueRunState {
    return {
      schemaVersion: VALUE_PROGRAM_CHECKPOINT_SCHEMA_VERSION,
      engineVersion: VALUE_PROGRAM_ENGINE_VERSION,
      status: this.status,
      frames: this.frames.map((frame) => ({ blockId: frame.blockId, index: frame.index })),
      variables: { ...this.variables },
      answer: this.answer,
      output: [...this.output],
      pendingValues: [...this.pending],
      consumedSteps: this.consumed,
      executions: this.executions,
      activeBlockId: this.activeBlockId,
      pauseReason: this.pauseReason,
    };
  }

  /** Un passo. La diagnosi è vuota quando il passo è andato a buon fine. */
  step(): ValueStepResult {
    if (!canContinue(this.state)) {
      return { state: this.state, problem: this.pauseReason };
    }

    if (this.consumed >= this.maxSteps) {
      this.status = 'BudgetExhausted';
      this.pauseReason = 'Il programma ha consumato tutti i passi a disposizione.';

      return { state: this.state, problem: this.pauseReason };
    }

    for (;;) {
      const frame = this.frames[this.frames.length - 1];
      if (frame.index >= frame.instructions.length) {
        if (!this.leave(frame)) {
          return { state: this.state, problem: this.pauseReason };
        }

        continue;
      }

      this.consumed += 1;
      const problem = this.execute(frame.instructions[frame.index], frame);

      return { state: this.state, problem };
    }
  }

  /** Esegue fino a fermarsi: alla conclusione, all'attesa di un valore o a un rifiuto. */
  runToRest(): ValueRunState {
    while (this.status === 'Ready') this.step();

    return this.state;
  }

  private reset(): void {
    this.frames = [];
    this.variables = {};
    this.assigned = new Set<string>();
    this.output = [];
    this.status = 'Ready';
    this.consumed = 0;
    this.executions = 0;
    this.activeBlockId = '';
    this.pauseReason = '';

    for (const name of this.variableNames) {
      this.variables[name] = this.defaults[name] ?? 0;
    }

    // Un valore iniziale dichiarato dalla definizione è un valore assegnato: la definizione ha
    // scelto di partire da lì, e non è un valore che manca.
    for (const name of Object.keys(this.defaults)) {
      if (name in this.variables) this.assigned.add(name);
    }

    this.frames.push({
      instructions: this.program,
      blockId: '',
      kind: 'root',
      returnIndex: 0,
      index: 0,
      remaining: 0,
    });
  }

  /**
   * Ricostruisce lo stack da un percorso salvato. L'indice di ritorno non si conserva: si ricalcola,
   * perché dipende dalla struttura del programma e non dall'esecuzione.
   */
  private reEnter(path: readonly ValueFrame[]): void {
    this.frames = [];

    if (path.length === 0) {
      this.frames.push({
        instructions: this.program,
        blockId: '',
        kind: 'root',
        returnIndex: 0,
        index: 0,
        remaining: 0,
      });

      return;
    }

    let instructions: readonly ValueInstruction[] = this.program;
    let returnIndex = 0;

    for (const step of path) {
      if (step.index < 0 || step.index >= instructions.length) {
        throw new Error('Il checkpoint indica un’istruzione che non esiste in questo progetto.');
      }

      const instruction = instructions[step.index];
      this.frames.push({
        instructions,
        blockId: step.blockId,
        kind: kindOf(step.blockId),
        returnIndex,
        index: step.index,
        remaining: 0,
      });

      returnIndex = step.index + 1;
      instructions = nextSequence(instruction, step.blockId);
    }
  }

  private execute(instruction: ValueInstruction, frame: WorkFrame): string {
    this.activeBlockId = instruction.blockId;
    // Un passo eseguito è un'esecuzione: serve a distinguere «ha funzionato» da «non ha eseguito».
    this.executions += 1;

    switch (instruction.kind) {
      case 'set':
        return this.assign(instruction.variable, instruction.value, frame);

      case 'change': {
        // «cambia Punti di 1»: legge, somma e riscrive. La variabile deve esistere **e** avere già un
        // valore, altrimenti l'incremento partirebbe da zero.
        if (!(instruction.variable in this.variables)) {
          return this.refuse(
            `La variabile «${instruction.variable}» non è dichiarata nell’esercizio.`,
          );
        }

        const delta = this.evaluate(instruction.delta);
        if (delta.problem.length > 0) return this.refuse(delta.problem);

        const corrente = this.evaluate({ kind: 'variable', variable: instruction.variable });
        if (corrente.problem.length > 0) return this.refuse(corrente.problem);

        this.variables[instruction.variable] = corrente.value + delta.value;
        this.assigned.add(instruction.variable);
        frame.index += 1;

        return '';
      }

      case 'show': {
        const evaluation = this.evaluate(instruction.value);
        if (evaluation.problem.length > 0) return this.refuse(evaluation.problem);

        this.output.push(String(evaluation.value));
        frame.index += 1;

        return '';
      }

      case 'ask':
        return this.ask(instruction, frame);

      case 'if':
        return this.branch(instruction, frame);

      case 'repeat':
        return this.repeat(instruction, frame);

      default:
        return this.refuse('L’istruzione non è prevista da questo motore.');
    }
  }

  private assign(variable: string, expression: ValueExpression, frame: WorkFrame): string {
    if (!(variable in this.variables)) {
      // Una variabile che la definizione non dichiara non esiste: è la definizione a dire che cosa
      // il programma può nominare.
      return this.refuse(`La variabile «${variable}» non è dichiarata nell’esercizio.`);
    }

    const evaluation = this.evaluate(expression);
    if (evaluation.problem.length > 0) return this.refuse(evaluation.problem);

    this.variables[variable] = evaluation.value;
    this.assigned.add(variable);
    frame.index += 1;

    return '';
  }

  /**
   * «chiedi [domanda?] e attendi». Se un parametro è ancora disponibile lo consuma e prosegue;
   * altrimenti la corsa si ferma **su questa istruzione**, senza avanzare, e riprenderà da qui.
   *
   * La risposta resta a parte dall'ambiente delle variabili, perché è un reporter suo: chiedere di
   * nuovo la sostituisce, mentre le variabili conservano i valori memorizzati.
   */
  private ask(
    instruction: { kind: 'ask'; blockId: string; variable: string | null; question: string },
    frame: WorkFrame,
  ): string {
    if (
      instruction.variable !== null &&
      instruction.variable.length > 0 &&
      !(instruction.variable in this.variables)
    ) {
      return this.refuse(`La variabile «${instruction.variable}» non è dichiarata nell’esercizio.`);
    }

    if (this.pending.length === 0) {
      this.status = 'WaitingForValue';
      this.pauseReason =
        instruction.question.length > 0
          ? `In attesa della risposta a «${instruction.question}».`
          : 'In attesa di un valore.';

      return '';
    }

    this.answer = this.pending[0];
    this.pending.shift();

    if (instruction.variable !== null && instruction.variable.length > 0) {
      this.variables[instruction.variable] = this.answer;
      this.assigned.add(instruction.variable);
    }

    frame.index += 1;

    return '';
  }

  private branch(
    instruction: {
      kind: 'if';
      blockId: string;
      condition: ValueComparison;
      then: readonly ValueInstruction[];
      otherwise: readonly ValueInstruction[];
    },
    frame: WorkFrame,
  ): string {
    const left = this.evaluate(instruction.condition.left);
    if (left.problem.length > 0) return this.refuse(left.problem);

    const right = this.evaluate(instruction.condition.right);
    if (right.problem.length > 0) return this.refuse(right.problem);

    const taken =
      instruction.condition.operator === 'Greater'
        ? left.value > right.value
        : instruction.condition.operator === 'Less'
          ? left.value < right.value
          : left.value === right.value;

    // L'istruzione è compiuta per il genitore: si avanza qui e si ricorda dove riprendere quando il
    // ramo sarà concluso.
    frame.index += 1;
    const returnIndex = frame.index;

    if (taken) {
      this.push(instruction.then, instruction.blockId, 'body', returnIndex);
    } else if (instruction.otherwise.length > 0) {
      this.push(instruction.otherwise, otherwiseId(instruction.blockId), 'otherwise', returnIndex);
    }

    return '';
  }

  private repeat(
    instruction: {
      kind: 'repeat';
      blockId: string;
      count: number;
      body: readonly ValueInstruction[];
    },
    frame: WorkFrame,
  ): string {
    frame.index += 1;
    const returnIndex = frame.index;

    if (instruction.count <= 0) return '';

    this.push(instruction.body, instruction.blockId, 'loop', returnIndex);
    this.frames[this.frames.length - 1].remaining = instruction.count;

    return '';
  }

  /**
   * Chiude un fotogramma concluso. Un corpo di ciclo con giri ancora da fare riparte dall'inizio;
   * altrimenti si torna al genitore **all'indice di ritorno**. Restituisce falso quando la corsa è
   * finita.
   */
  private leave(frame: WorkFrame): boolean {
    this.frames.pop();

    if (frame.kind === 'loop' && frame.remaining > 0) {
      frame.remaining -= 1;
      if (frame.remaining > 0) {
        frame.index = 0;
        this.frames.push(frame);

        return true;
      }
    }

    if (this.frames.length === 0) {
      this.status = 'Finished';
      this.pauseReason = '';

      return false;
    }

    this.frames[this.frames.length - 1].index = frame.returnIndex;

    return true;
  }

  private push(
    instructions: readonly ValueInstruction[],
    blockId: string,
    kind: WorkFrame['kind'],
    returnIndex: number,
  ): void {
    this.frames.push({ instructions, blockId, kind, returnIndex, index: 0, remaining: 0 });
  }

  private refuse(problem: string): string {
    this.status = 'Refused';
    this.pauseReason = problem;

    return problem;
  }

  /** Valuta un'espressione; un problema è un testo vuoto quando è andata a buon fine. */
  private evaluate(expression: ValueExpression): { value: number; problem: string } {
    this.consumed += 1;

    switch (expression.kind) {
      case 'literal':
        return { value: expression.value, problem: '' };

      case 'answer':
        // Il reporter «risposta»: l'ultimo valore letto, anche se nel frattempo il programma ha
        // memorizzato quella risposta in una variabile.
        return { value: this.answer, problem: '' };

      case 'variable': {
        if (!(expression.variable in this.variables)) {
          return {
            value: 0,
            problem: `La variabile «${expression.variable}» non è dichiarata nell’esercizio.`,
          };
        }

        // Un valore mai impostato non vale zero: valere zero in silenzio darebbe un risultato
        // sbagliato con l'aria di essere giusto, che è il peggior esito didattico possibile.
        if (!this.assigned.has(expression.variable)) {
          return {
            value: 0,
            problem: `La variabile «${expression.variable}» non ha ancora un valore: il programma deve prima leggerlo o assegnarlo.`,
          };
        }

        return { value: this.variables[expression.variable], problem: '' };
      }

      case 'add': {
        const left = this.evaluate(expression.left);
        if (left.problem.length > 0) return left;

        const right = this.evaluate(expression.right);

        return right.problem.length > 0 ? right : { value: left.value + right.value, problem: '' };
      }

      default:
        // Un confronto non è un valore: sommarlo o mostrarlo non vuol dire nulla.
        return {
          value: 0,
          problem: 'Un confronto non è un valore: non si può sommare né mostrare.',
        };
    }
  }
}
