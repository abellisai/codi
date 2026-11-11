import { previewCases, type PreviewCase, type PreviewVariable } from './value-program-preview';

/**
 * L'anteprima dell'autore (§13.9): il docente esegue il proprio programma sui casi di prova prima di
 * darlo agli alunni.
 *
 * È la parte che vale la pena provare: la form la monta, il calcolo decide se un esercizio è
 * soddisfacibile. Questi casi difendono le tre cose che rendono l'anteprima utile — vedere **tutti** i
 * casi, distinguere un programma che sbaglia da uno che non si esegue, e usare lo stesso motore degli
 * alunni.
 */
const VARIABLES: readonly PreviewVariable[] = [
  { name: 'Numero', initialValue: 0 },
  { name: 'Punti', initialValue: 0 },
];

function blocco(
  type: string,
  id: string,
  extra: Record<string, unknown> = {},
): Record<string, unknown> {
  return { type, id, ...extra };
}

function campo(nome: string, valore: unknown): Record<string, unknown> {
  return { fields: { [nome]: valore } };
}

function ingresso(nome: string, dentro: Record<string, unknown>): Record<string, unknown> {
  return { inputs: { [nome]: { block: dentro } } };
}

/**
 * Più ingressi dello stesso blocco. Spalmare due `ingresso` sullo stesso oggetto non li unisce: il
 * secondo sostituisce il primo, perché entrambi scrivono la proprietà `inputs`. È un inciampo che ho
 * fatto tre volte in questa fase, e questo aiutante lo rende impossibile.
 */
function ingressi(
  ...coppie: readonly (readonly [string, Record<string, unknown>])[]
): Record<string, unknown> {
  const inputs: Record<string, unknown> = {};
  for (const [nome, dentro] of coppie) {
    inputs[nome] = { block: dentro };
  }

  return { inputs };
}

function catena(blocchi: Record<string, unknown>[]): Record<string, unknown> {
  for (let indice = blocchi.length - 2; indice >= 0; indice -= 1) {
    blocchi[indice]['next'] = { block: blocchi[indice + 1] };
  }

  return blocchi[0];
}

function workspace(corpo: Record<string, unknown>): string {
  return JSON.stringify({ blocks: { languageVersion: 0, blocks: [corpo] } });
}

/** «chiedi [domanda?] e attendi», «porta Numero a (risposta)», «dire (Numero + 1)». */
function sommaUno(): string {
  return workspace(
    catena([
      blocco('val_start', 's'),
      blocco('val_ask', 'ask', campo('QUESTION', 'Quanto vale?')),
      blocco('val_set', 'memo', {
        ...campo('VAR', 'Numero'),
        ...ingresso('VALUE', blocco('val_answer', 'risposta')),
      }),
      blocco('val_set', 'set', {
        ...campo('VAR', 'Punti'),
        ...ingresso(
          'VALUE',
          blocco('val_add', 'add', {
            ...ingressi(
              ['VALUE', blocco('val_variable', 'v', campo('VAR', 'Numero'))],
              ['OTHER', blocco('val_number', 'n', campo('NUM', 1))],
            ),
          }),
        ),
      }),
      blocco(
        'val_show',
        'show',
        ingresso('VALUE', blocco('val_variable', 'vend', campo('VAR', 'Punti'))),
      ),
    ]),
  );
}

const CASI: readonly PreviewCase[] = [
  { inputs: [6], expectedOutput: ['7'] },
  { inputs: [3], expectedOutput: ['4'] },
];

describe('Anteprima dell’autore', () => {
  it('UnProgrammaCheFunziona_SuperaTuttiICasi', () => {
    const esito = previewCases(sommaUno(), VARIABLES, CASI);

    expect('rows' in esito).toBe(true);
    if (!('rows' in esito)) return;

    expect(esito.rows).toHaveLength(2);
    expect(esito.rows.every((riga) => riga.passed)).toBe(true);
    expect(esito.rows[0]).toEqual({
      inputs: '6',
      expected: '7',
      shown: '7',
      passed: true,
    });
  });

  it('MostraTuttiICasi_NonSoloIlPrimoCheNonTorna', () => {
    // Il primo caso è giusto e il secondo no: fermarsi al primo errore nasconderebbe il caso che
    // conta, ed è il motivo per cui l'anteprima li mostra tutti.
    const sbagliato = sommaUno().replace('"NUM":1', '"NUM":2');

    const esito = previewCases(sbagliato, VARIABLES, CASI);

    expect('rows' in esito).toBe(true);
    if (!('rows' in esito)) return;

    expect(esito.rows).toHaveLength(2);
    expect(esito.rows.map((riga) => riga.passed)).toEqual([false, false]);
    expect(esito.rows[0]?.shown).toBe('8');
    expect(esito.rows[0]?.expected).toBe('7');
  });

  it('UnCasoGiustoEUnoSbagliato_SiVedonoTuttiEDue', () => {
    // Con una soglia che rende giusto il primo caso e sbagliato il secondo: l'anteprima li distingue.
    const esito = previewCases(sommaUno(), VARIABLES, [
      { inputs: [6], expectedOutput: ['7'] },
      { inputs: [3], expectedOutput: ['999'] },
    ]);

    expect('rows' in esito).toBe(true);
    if (!('rows' in esito)) return;

    expect(esito.rows.map((riga) => riga.passed)).toEqual([true, false]);
  });

  it('UnCasoCheNonDaAbbastanzaValori_LoDiceInveceDiMostrareZero', () => {
    // Il programma chiede un valore e il caso non ne dà: la corsa resta in attesa, e la colpa è
    // dell'esercizio. Un'uscita vuota farebbe sembrare il caso sbagliato per colpa del programma.
    const esito = previewCases(sommaUno(), VARIABLES, [{ inputs: [], expectedOutput: ['7'] }]);

    expect('rows' in esito).toBe(true);
    if (!('rows' in esito)) return;

    expect(esito.rows[0]?.passed).toBe(false);
    expect(esito.rows[0]?.shown).toContain('Quanto vale?');
  });

  it('UnProgettoCheNonSiEsegue_RaccontaIlProblemaInveceDiUnaRiga', () => {
    // Il progetto vuoto: manca il punto di ingresso, e non c'è nulla da eseguire.
    const esito = previewCases(
      JSON.stringify({ blocks: { languageVersion: 0, blocks: [] } }),
      VARIABLES,
      CASI,
    );

    expect('problems' in esito).toBe(true);
    if (!('problems' in esito)) return;

    expect(esito.problems.join(' ')).toContain('quando si avvia');
  });

  it('UnaVariabileNonDichiarata_RaccontaQualiSonoAmmesse', () => {
    const sbagliato = sommaUno().replace('"VAR":"Punti"', '"VAR":"Sconosciuta"');

    const esito = previewCases(sbagliato, VARIABLES, CASI);

    expect('problems' in esito).toBe(true);
    if (!('problems' in esito)) return;

    expect(esito.problems.join(' ')).toContain('Sconosciuta');
    expect(esito.problems.join(' ')).toContain('Numero, Punti');
  });
});
