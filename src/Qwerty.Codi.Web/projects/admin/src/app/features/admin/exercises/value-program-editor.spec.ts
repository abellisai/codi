import { leggiCasi, leggiVariabili } from './value-program-editor';

/**
 * La lettura delle variabili e dei casi di prova dall'editor (§13.9). La sintassi è elementare — una
 * riga per elemento — ed è proprio per questo che va provata: una forma breve ha molte più
 * interpretazioni di quante ne ha una form con un campo per volta, e un documento composto a metà
 * sarebbe un esercizio che non si corregge.
 */
describe('Variabili scritte dall’autore', () => {
  it('LeggeUnaVariabilePerRiga', () => {
    const esito = leggiVariabili('Numero = 0\nPunti = 3');

    expect(esito.errore).toBeNull();
    expect(esito.voci).toEqual([
      { name: 'Numero', initialValue: 0 },
      { name: 'Punti', initialValue: 3 },
    ]);
  });

  it('IgnoraLeRigheVuoteELoSpazioAttorno', () => {
    const esito = leggiVariabili('\n  Numero=0  \n\n   \n');

    expect(esito.errore).toBeNull();
    expect(esito.voci).toEqual([{ name: 'Numero', initialValue: 0 }]);
  });

  it('UnValoreNegativo_SiLeggeComeNumeroConIlSegno', () => {
    const esito = leggiVariabili('Debito = -4');

    expect(esito.errore).toBeNull();
    expect(esito.voci).toEqual([{ name: 'Debito', initialValue: -4 }]);
  });

  it('UnaRigaSenzaUguale_DiceQualeRiga', () => {
    const esito = leggiVariabili('Numero = 0\nPunti');

    expect(esito.errore).toContain('Riga 2');
    expect(esito.errore).toContain('Nome = valore');
    expect(esito.voci).toEqual([]);
  });

  it('UnValoreNonNumerico_DiceQualeRiga', () => {
    const esito = leggiVariabili('Numero = tanti');

    expect(esito.errore).toContain('Riga 1');
    expect(esito.errore).toContain('numero intero');
  });

  it('UnNomeConSpazi_Rifiuta', () => {
    const esito = leggiVariabili('I miei punti = 0');

    expect(esito.errore).toContain('comincia con una lettera');
  });

  it('UnNomeCheIniziaConUnaCifra_Rifiuta', () => {
    const esito = leggiVariabili('1Punto = 0');

    expect(esito.errore).not.toBeNull();
  });

  it('UnNomeDichiaratoDueVolte_Rifiuta', () => {
    const esito = leggiVariabili('Punti = 0\nPunti = 1');

    expect(esito.errore).toContain('già dichiarata');
  });

  it('UnTestoVuoto_NonEUnErrore: è la condizione di partenza', () => {
    const esito = leggiVariabili('');

    expect(esito.errore).toBeNull();
    expect(esito.voci).toEqual([]);
  });
});

describe('Casi di prova scritti dall’autore', () => {
  it('LeggeValoriEUscita', () => {
    const esito = leggiCasi('6 -> 7\n3 -> 4');

    expect(esito.errore).toBeNull();
    expect(esito.voci).toEqual([
      { inputs: [6], expectedOutput: ['7'] },
      { inputs: [3], expectedOutput: ['4'] },
    ]);
  });

  it('LeggePiuValoriEPiuRigheDiUscita', () => {
    const esito = leggiCasi('3, 8, 6 -> 2');

    expect(esito.errore).toBeNull();
    expect(esito.voci).toEqual([{ inputs: [3, 8, 6], expectedOutput: ['2'] }]);
  });

  it('AccettaUsciteMultipleSeparateDaVirgoleOdaSpazi', () => {
    const esito = leggiCasi('3, 8, 6 -> 0, 1, 2\n5, 2, 9 -> 0 0 1');

    expect(esito.errore).toBeNull();
    expect(esito.voci).toEqual([
      { inputs: [3, 8, 6], expectedOutput: ['0', '1', '2'] },
      { inputs: [5, 2, 9], expectedOutput: ['0', '0', '1'] },
    ]);
  });

  it('UnCasoCheNonLeggeNulla_SiScriveSenzaValori', () => {
    const esito = leggiCasi('-> 12');

    expect(esito.errore).toBeNull();
    expect(esito.voci).toEqual([{ inputs: [], expectedOutput: ['12'] }]);
  });

  it('SenzaFreccia_DiceQualeRiga', () => {
    const esito = leggiCasi('6 7');

    expect(esito.errore).toContain('Riga 1');
    expect(esito.errore).toContain('uscita attesa');
  });

  it('SenzaUscitaAttesa_Rifiuta', () => {
    const esito = leggiCasi('6 ->');

    expect(esito.errore).toContain('manca l');
  });

  it('UnValoreNonNumerico_DiceQualeRigaEQualeValore', () => {
    const esito = leggiCasi('sei -> 7');

    expect(esito.errore).toContain('Riga 1');
    expect(esito.errore).toContain('sei');
  });

  it('UnaRigaSbagliataInMezzo_FermaTutto: un documento a metà non si salva', () => {
    const esito = leggiCasi('6 -> 7\nsbagliata\n3 -> 4');

    expect(esito.errore).toContain('Riga 2');
    expect(esito.voci).toEqual([]);
  });
});
