import {
  ValueProgramCheckpointError,
  ValueProgramRunner,
  parseValueProgramCheckpointState,
  type ValueInstruction,
  type ValueRunState,
} from './value-program-runner';

/**
 * Il checkpoint di `valueprogram-1` (§13.7): un formato **proprio** di questa versione del motore.
 * Il numero di schema è 1, come quello del percorso, e non significa la stessa cosa: sono due formati
 * diversi e il server li distingue per versione del motore. Questi casi difendono il confine di
 * ripresa: un checkpoint di un'altra versione, o con una forma incoerente, non si riprende.
 */
const PROGRAM: ValueInstruction[] = [
  { kind: 'set', blockId: 'set', variable: 'Punti', value: { kind: 'literal', value: 4 } },
  { kind: 'ask', blockId: 'ask', variable: null, question: 'Quanto vale?' },
  // La risposta si legge con il reporter, non da una variabile: è il vocabolario del corso.
  { kind: 'show', blockId: 'show', value: { kind: 'answer' } },
];

function attesa(): ValueRunState {
  return ValueProgramRunner.start(PROGRAM, ['Numero', 'Punti'], {}, [], 100).runToRest();
}

describe('Checkpoint dei valori', () => {
  it('UnoStatoProdottoDalMotore_SiConverteSenzaPerdereNulla', () => {
    const state = attesa();

    const checkpoint = parseValueProgramCheckpointState(state);

    expect(checkpoint).toEqual(state);
    expect(checkpoint.schemaVersion).toBe(1);
    expect(checkpoint.engineVersion).toBe('valueprogram-1');
    expect(checkpoint.status).toBe('WaitingForValue');
    expect(checkpoint.variables['Punti']).toBe(4);
  });

  it('UnoSchemaDiUnAltraVersione_Rifiuta', () => {
    const state = attesa();

    expect(() => parseValueProgramCheckpointState({ ...state, schemaVersion: 2 })).toThrow(
      ValueProgramCheckpointError,
    );
  });

  it('UnCheckpointDiUnAltroMotore_Rifiuta', () => {
    const state = attesa();

    // Lo stesso numero di schema non basta: il formato appartiene a una versione del motore.
    expect(() =>
      parseValueProgramCheckpointState({ ...state, engineVersion: 'gridpath-1' }),
    ).toThrow(/altra versione del motore/);
  });

  it('UnoStatoNonPrevisto_Rifiuta', () => {
    const state = attesa();

    expect(() => parseValueProgramCheckpointState({ ...state, status: 'Paused' })).toThrow(
      /non è uno di quelli previsti/,
    );
  });

  it('UnFotogrammaSenzaPosizioneValida_Rifiuta', () => {
    const state = attesa();

    expect(() =>
      parseValueProgramCheckpointState({ ...state, frames: [{ blockId: 'set', index: -1 }] }),
    ).toThrow(/posizione valida/);
  });

  it('VariabiliNonNumeriche_Rifiutano', () => {
    const state = attesa();

    expect(() =>
      parseValueProgramCheckpointState({ ...state, variables: { Punti: 'quattro' } }),
    ).toThrow(/non è un numero intero/);
  });

  it('UnValoreNonNumerico_Rifiuta', () => {
    const state = attesa();

    expect(() => parseValueProgramCheckpointState({ ...state, pendingValues: ['sette'] })).toThrow(
      /non è un numero/,
    );
  });

  it('QualcosaCheNonEUnCheckpoint_Rifiuta', () => {
    for (const valore of [null, 42, 'checkpoint', []]) {
      expect(() => parseValueProgramCheckpointState(valore)).toThrow(ValueProgramCheckpointError);
    }
  });

  it('UnCheckpointVerificato_RiprendeDallaStessaIstruzione', () => {
    // È l'uso per cui il checkpoint esiste: si riprende dal punto in cui la corsa si è fermata, con
    // le variabili come le ha lasciate, e non si ricomincia da capo.
    const state = attesa();
    const checkpoint = parseValueProgramCheckpointState(state);

    const ripreso = ValueProgramRunner.resume(
      PROGRAM,
      ['Numero', 'Punti'],
      {},
      { ...checkpoint, pendingValues: [7] },
      100,
    ).runToRest();

    expect(ripreso.status).toBe('Finished');
    expect(ripreso.output).toEqual(['7']);
    expect(ripreso.variables['Punti']).toBe(4);
  });
});
