import {
  MAX_VALUE_INTEGER,
  VALUE_PROGRAM_ENGINE_VERSION,
  ValueProgramRunner,
  type ValueExpression,
  type ValueInstruction,
  type ValueRunState,
} from './value-program-runner';
import { MAX_INTEGER, VALUE_PROGRAM_BLOCK_TYPES } from './value-blocks';

/**
 * Il motore `valueprogram-1` nel browser (§13.9). Questi casi sono la **controparte** di
 * `ValueProgramRunnerTests.cs`: ogni caso qui ha il suo omonimo nel dominio .NET, perché le due
 * implementazioni eseguono lo stesso esercizio e una divergenza non si vedrebbe fino all'aula.
 *
 * Le differenze di lingua restano: in TypeScript le proprietà sono `kind`, in C# i tipi sono
 * distinte. Ciò che non può differire è il **comportamento**: gli stessi passi, la stessa uscita,
 * la stessa sospensione.
 */
const VARIABLES = ['Numero', 'Punti'];

function run(
  program: readonly ValueInstruction[],
  parameters: readonly number[] = [],
  maxSteps = 200,
): ValueRunState {
  return ValueProgramRunner.start(program, VARIABLES, {}, parameters, maxSteps).runToRest();
}

function literal(value: number): ValueExpression {
  return { kind: 'literal', value };
}

const PUNTI = { kind: 'variable', variable: 'Punti' } as const;
const NUMERO = { kind: 'variable', variable: 'Numero' } as const;

describe('Motore dei valori: variabili e uscita', () => {
  it('ConUnaVariabileMostrata_IlProgrammaProduceIlValoreAssegnato', () => {
    const program: ValueInstruction[] = [
      { kind: 'set', blockId: 'set', variable: 'Numero', value: literal(6) },
      { kind: 'show', blockId: 'show', value: NUMERO },
    ];

    const state = run(program);

    expect(state.status).toBe('Finished');
    expect(state.output).toEqual(['6']);
    expect(state.variables['Numero']).toBe(6);
  });

  it('SenzaValoriDaLeggere_IlProgrammaSiConclude', () => {
    const program: ValueInstruction[] = [
      {
        kind: 'set',
        blockId: 'set',
        variable: 'Numero',
        value: { kind: 'add', left: literal(3), right: literal(4) },
      },
      { kind: 'show', blockId: 'show', value: NUMERO },
    ];

    const state = run(program);

    expect(state.status).toBe('Finished');
    expect(state.output).toEqual(['7']);
  });

  it('ConUnCicloContato_IlCorpoSiRipeteEsattamente', () => {
    const program: ValueInstruction[] = [
      { kind: 'set', blockId: 'set', variable: 'Punti', value: literal(0) },
      {
        kind: 'repeat',
        blockId: 'repeat',
        count: 3,
        body: [
          {
            kind: 'set',
            blockId: 'inc',
            variable: 'Punti',
            value: { kind: 'add', left: PUNTI, right: literal(1) },
          },
        ],
      },
      { kind: 'show', blockId: 'show', value: PUNTI },
    ];

    const state = run(program);

    expect(state.status).toBe('Finished');
    expect(state.output).toEqual(['3']);
    expect(state.variables['Punti']).toBe(3);
  });
});

describe('Motore dei valori: il confronto e i rami', () => {
  it.each([
    [6, '0'],
    [3, '1'],
  ])('ConUnConfronto_SiEsegueIlRamoCheCorrisponde(%i)', (valore, atteso) => {
    const program: ValueInstruction[] = [
      { kind: 'ask', blockId: 'ask', variable: null, question: 'Quanto vale?' },
      { kind: 'set', blockId: 'memo', variable: 'Numero', value: { kind: 'answer' } },
      {
        kind: 'if',
        blockId: 'if',
        condition: { left: NUMERO, operator: 'Greater', right: literal(5) },
        then: [{ kind: 'show', blockId: 'then', value: literal(0) }],
        otherwise: [{ kind: 'show', blockId: 'else', value: literal(1) }],
      },
    ];

    const state = run(program, [valore]);

    expect(state.status).toBe('Finished');
    expect(state.output).toEqual([atteso]);
  });

  it('ConUnSeSenzaAltrimenti_IlRamoFalsoNonMostraNulla', () => {
    const program: ValueInstruction[] = [
      { kind: 'ask', blockId: 'ask', variable: null, question: 'Quanto vale?' },
      { kind: 'set', blockId: 'memo', variable: 'Numero', value: { kind: 'answer' } },
      {
        kind: 'if',
        blockId: 'if',
        condition: { left: NUMERO, operator: 'Greater', right: literal(5) },
        then: [{ kind: 'show', blockId: 'then', value: literal(1) }],
        otherwise: [],
      },
      { kind: 'show', blockId: 'end', value: literal(9) },
    ];

    const state = run(program, [2]);

    expect(state.output).toEqual(['9']);
  });

  it('ConUnCicloCheContieneUnConfronto_IlConteggioRispettaLaCondizione', () => {
    // È il programma della lezione 4: contare tre carte e sommare solo quelle sopra il cinque.
    const program: ValueInstruction[] = [
      { kind: 'set', blockId: 'reset', variable: 'Punti', value: literal(0) },
      {
        kind: 'repeat',
        blockId: 'repeat',
        count: 3,
        body: [
          { kind: 'ask', blockId: 'ask', variable: null, question: 'Quanto vale?' },
          { kind: 'set', blockId: 'memo', variable: 'Numero', value: { kind: 'answer' } },
          { kind: 'set', blockId: 'memo', variable: 'Numero', value: { kind: 'answer' } },
          {
            kind: 'if',
            blockId: 'if',
            condition: { left: NUMERO, operator: 'Greater', right: literal(5) },
            then: [
              {
                kind: 'set',
                blockId: 'inc',
                variable: 'Punti',
                value: { kind: 'add', left: PUNTI, right: literal(1) },
              },
            ],
            otherwise: [],
          },
        ],
      },
      { kind: 'show', blockId: 'show', value: PUNTI },
    ];

    const state = run(program, [3, 8, 6]);

    expect(state.status).toBe('Finished');
    expect(state.output).toEqual(['2']);
  });
});

describe('Motore dei valori: l’attesa di un valore', () => {
  it('ConUnValoreDaLeggere_LaCorsaSiFermaInAttesa', () => {
    const program: ValueInstruction[] = [
      { kind: 'ask', blockId: 'ask', variable: null, question: 'Quanto vale?' },
      { kind: 'set', blockId: 'memo', variable: 'Numero', value: { kind: 'answer' } },
      { kind: 'show', blockId: 'show', value: NUMERO },
    ];

    const state = run(program);

    expect(state.status).toBe('WaitingForValue');
    expect(state.output).toEqual([]);
    // Il motivo dice che cosa si sta aspettando: la domanda che il bambino legge.
    expect(state.pauseReason).toContain('Quanto vale?');
    // La corsa è ferma **sull'istruzione che chiede**: riprenderà da qui.
    expect(state.activeBlockId).toBe('ask');
  });

  it('AttesaDiUnValore_RiprendeDallaStessaIstruzioneSenzaRicomincare', () => {
    const program: ValueInstruction[] = [
      { kind: 'set', blockId: 'set', variable: 'Punti', value: literal(1) },
      { kind: 'ask', blockId: 'ask', variable: null, question: 'Quanto vale?' },
      { kind: 'set', blockId: 'memo', variable: 'Numero', value: { kind: 'answer' } },
      {
        kind: 'set',
        blockId: 'inc',
        variable: 'Punti',
        value: { kind: 'add', left: PUNTI, right: literal(1) },
      },
      { kind: 'show', blockId: 'show', value: NUMERO },
      { kind: 'show', blockId: 'showPunti', value: PUNTI },
    ];

    const waiting = run(program);
    expect(waiting.status).toBe('WaitingForValue');

    // Il valore arriva dopo: la corsa riprende e **non** riesegue l'assegnazione iniziale, che
    // riporterebbe Punti a 1.
    const resumed = ValueProgramRunner.resume(
      program,
      VARIABLES,
      {},
      { ...waiting, pendingValues: [42] },
      100,
    );
    const finished = resumed.runToRest();

    expect(finished.status).toBe('Finished');
    expect(finished.output).toEqual(['42', '2']);
    expect(finished.variables['Numero']).toBe(42);
    expect(finished.variables['Punti']).toBe(2);
  });

  it('ConPiuValoriDiQuantiNeServono_QuelliInPiuRestano', () => {
    const program: ValueInstruction[] = [
      { kind: 'ask', blockId: 'ask', variable: null, question: 'Quanto vale?' },
      { kind: 'set', blockId: 'memo', variable: 'Numero', value: { kind: 'answer' } },
      { kind: 'show', blockId: 'show', value: NUMERO },
    ];

    const state = run(program, [7, 9]);

    expect(state.output).toEqual(['7']);
    expect(state.pendingValues).toEqual([9]);
  });

  it('ConUnValoreInizialeDichiarato_IlProgrammaPuoUsarlo', () => {
    const program: ValueInstruction[] = [{ kind: 'show', blockId: 'show', value: PUNTI }];

    const state = ValueProgramRunner.start(program, VARIABLES, { Punti: 5 }, [], 50).runToRest();

    expect(state.status).toBe('Finished');
    expect(state.output).toEqual(['5']);
  });
});

describe('Motore dei valori: i rifiuti', () => {
  it('ConUnValoreMaiImpostato_RifiutaInveceDiUsareZero', () => {
    const program: ValueInstruction[] = [{ kind: 'show', blockId: 'show', value: NUMERO }];

    const state = run(program);

    expect(state.status).toBe('Refused');
    expect(state.pauseReason).toContain('non ha ancora un valore');
  });

  it('UnaVariabileNonDichiarata_RifiutaInveceDiInventarla', () => {
    const program: ValueInstruction[] = [
      { kind: 'set', blockId: 'set', variable: 'Sconosciuta', value: literal(1) },
    ];

    const state = run(program);

    expect(state.status).toBe('Refused');
    expect(state.pauseReason).toContain('Sconosciuta');
    expect(state.variables['Sconosciuta']).toBeUndefined();
  });

  it('UnProgrammaCheNonFinisce_SiFermaSulBudget', () => {
    const program: ValueInstruction[] = [
      {
        kind: 'repeat',
        blockId: 'repeat',
        count: 50,
        body: [{ kind: 'show', blockId: 'show', value: literal(1) }],
      },
    ];

    const state = run(program, [], 10);

    expect(state.status).toBe('BudgetExhausted');
    expect(state.pauseReason).toContain('passi');
  });
});

describe('Motore dei valori: lo stato salvato', () => {
  it('LoStatoSalvato_ContieneAmbienteUscitaEPercorso', () => {
    const program: ValueInstruction[] = [
      { kind: 'set', blockId: 'set', variable: 'Punti', value: literal(4) },
      { kind: 'ask', blockId: 'ask', variable: null, question: 'Quanto vale?' },
      { kind: 'set', blockId: 'memo', variable: 'Numero', value: { kind: 'answer' } },
    ];

    const state = run(program);

    expect(state.schemaVersion).toBe(1);
    expect(state.engineVersion).toBe('valueprogram-1');
    expect(state.variables['Punti']).toBe(4);
    expect(state.frames.length).toBeGreaterThan(0);
    expect(state.activeBlockId).toBe('ask');
    expect(state.status).toBe('WaitingForValue');
  });

  it('UnaCorsaConclusa_NonConsumaPiuPassiDelDovuto', () => {
    const program: ValueInstruction[] = [
      { kind: 'set', blockId: 'set', variable: 'Numero', value: literal(1) },
      { kind: 'show', blockId: 'show', value: NUMERO },
    ];

    const state = run(program, [], 100);

    expect(state.status).toBe('Finished');
    // Due istruzioni e una valutazione: tre passi, più la valutazione del valore mostrato.
    expect(state.consumedSteps).toBeLessThanOrEqual(4);
  });

  it('UnCicloDiTreGiri_CostaDodiciPassiNonUnoInPiuPerGiro', () => {
    const program: ValueInstruction[] = [
      { kind: 'set', blockId: 'set', variable: 'Punti', value: literal(0) },
      {
        kind: 'repeat',
        blockId: 'repeat',
        count: 3,
        body: [
          {
            kind: 'set',
            blockId: 'inc',
            variable: 'Punti',
            value: { kind: 'add', left: PUNTI, right: literal(1) },
          },
        ],
      },
      { kind: 'show', blockId: 'show', value: PUNTI },
    ];

    const state = run(program);

    // Il conto è lo stesso in C# (`ValueProgramRunnerTests`): due passi per l'assegnazione iniziale,
    // uno per il ciclo, quattro per giro — l'istruzione e le tre valutazioni della somma — e due per
    // l'uscita. Chiudere una sequenza conclusa non si addebita al programma.
    expect(state.consumedSteps).toBe(17);
    expect(state.output).toEqual(['3']);
  });
});

describe('Motore dei valori: i limiti dichiarati', () => {
  it('IlMassimoIntero_DichiaratoDalCatalogo', () => {
    // Il limite vive nella libreria e lo condivide chi compone l'esercizio: un numero più grande non
    // entra in un blocco, e il motore non deve difendersi da un valore che nessuno può scrivere.
    expect(MAX_VALUE_INTEGER).toBe(1_000_000);
    // Il catalogo dei blocchi e il motore lo dichiarano una volta sola ciascuno: se divergessero, un
    // numero ammesso dall'editor sarebbe rifiutato dal motore.
    expect(MAX_VALUE_INTEGER).toBe(MAX_INTEGER);
    expect(VALUE_PROGRAM_ENGINE_VERSION).toBe('valueprogram-1');
    // Il catalogo dei blocchi è la forma con cui il programma si compone; il motore lo esegue. I
    // nomi dei blocchi non entrano nel runner — non ne ha bisogno — ma la versione sì.
    expect(VALUE_PROGRAM_BLOCK_TYPES).toContain('val_set');
  });
});
