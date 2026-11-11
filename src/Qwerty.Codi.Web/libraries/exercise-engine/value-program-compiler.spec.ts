import { compileValueProgram, type ValueProgramRules } from './value-program-compiler';
import { ValueProgramRunner, type ValueInstruction } from './value-program-runner';

/**
 * La compilazione da progetto a blocchi a istruzioni (§13.9): è il punto in cui la forma con cui
 * l'alunno **compone** diventa la forma che il motore **esegue**. Questi casi difendono due cose:
 * che un progetto ben composto produca il programma giusto — verificato eseguendolo, non guardando
 * la sua forma — e che un progetto incompleto produca una **diagnosi** e non un programma a metà.
 */
const RULES: ValueProgramRules = {
  variables: ['Numero', 'Punti'],
  defaults: { Numero: 0, Punti: 0 },
};

function blocco(type: string, id: string, extra: Record<string, unknown> = {}): string {
  return JSON.stringify({ type, id, ...extra });
}

function workspace(blocchi: string): string {
  return `{"blocks":{"languageVersion":0,"blocks":[${blocchi}]}}`;
}

function campo(nome: string, valore: unknown): Record<string, unknown> {
  return { fields: { [nome]: valore } };
}

/**
 * Un ingresso di valore o di sequenza, nella forma del documento di Blockly: l'ingresso è una
 * proprietà il cui valore è il blocco. Non c'è un secondo `block` da nessuna parte — aggiungerlo era
 * l'errore che rendeva muti tutti i blocchi con un ingresso.
 */
function ingresso(nome: string, dentro: string): Record<string, unknown> {
  return { inputs: { [nome]: { block: JSON.parse(dentro) as unknown } } };
}

/**
 * Più ingressi dello stesso blocco. Spalmare due `ingresso` sullo stesso oggetto non li unisce: il
 * secondo sostituisce il primo, perché entrambi scrivono la proprietà `inputs`.
 */
function ingressi(...coppie: readonly (readonly [string, string])[]): Record<string, unknown> {
  const inputs: Record<string, unknown> = {};
  for (const [nome, dentro] of coppie) {
    inputs[nome] = { block: JSON.parse(dentro) as unknown };
  }

  return { inputs };
}

/**
 * Lega i blocchi in catena. Il collegamento si **aggiunge** al blocco invece di essere scritto alla
 * fine del testo: un comando ha anche i suoi ingressi, e comporli per concatenazione di stringhe
 * significherebbe riscrivere il blocco, non aggiungergli un campo.
 */
function catena(blocchi: string[]): string {
  let coda: string | null = null;
  for (let indice = blocchi.length - 1; indice >= 0; indice -= 1) {
    const corrente = JSON.parse(blocchi[indice]) as Record<string, unknown>;
    if (coda !== null) corrente['next'] = { block: JSON.parse(coda) as unknown };
    coda = JSON.stringify(corrente);
  }

  return coda ?? '{}';
}

/** Compila ed esegue il progetto, così il caso verifica il **comportamento** e non la forma. */
function esegui(progetto: string, parametri: readonly number[] = []): string {
  const compilato = compileValueProgram(progetto, RULES);
  if ('problems' in compilato) {
    return `problemi: ${compilato.problems.map((p) => p.message).join(' | ')}`;
  }

  const runner = ValueProgramRunner.start(
    compilato.instructions,
    RULES.variables,
    RULES.defaults,
    parametri,
    500,
  );
  const stato = runner.runToRest();

  return `${stato.status} uscita[${stato.output.join(',')}]`;
}

describe('Compilazione del programma a valori', () => {
  it('IlProgrammaDelleCarte_DelCorso_SegnaIPuntiDelleCarteSopraIlCinque', () => {
    // Il programma della lezione 4 della proposta del corso, blocco per blocco:
    //
    //   quando si clicca sulla bandiera verde
    //   porta Punti a 0
    //   porta Numero a 0
    //   ripeti 3 volte
    //       chiedi [Numero della carta?] e attendi
    //       porta Numero a (risposta)
    //       se <(Numero) > 5> allora
    //           cambia Punti di 1
    //   dire (Punti)
    //
    // Il caso serve a due cose insieme: che il motore conti i punti, e che il **vocabolario** del
    // corso esista davvero — il reporter «risposta», il blocco «cambia», la domanda dentro «chiedi» —
    // invece di essere una riscrittura con altri nomi.
    const cambia = blocco('val_change', 'inc', {
      ...campo('VAR', 'Punti'),
      ...ingresso('DELTA', blocco('val_number', 'uno', campo('NUM', 1))),
    });
    const confronto = blocco('val_compare', 'cmp', {
      ...campo('OP', 'Greater'),
      ...ingressi(
        ['VALUE', blocco('val_variable', 'v', campo('VAR', 'Numero'))],
        ['OTHER', blocco('val_number', 'cinque', campo('NUM', 5))],
      ),
    });
    const corpo = catena([
      blocco('val_ask', 'ask', campo('QUESTION', 'Numero della carta?')),
      blocco('val_set', 'memo', {
        ...campo('VAR', 'Numero'),
        ...ingresso('VALUE', blocco('val_answer', 'risposta')),
      }),
      blocco('val_if', 'if', { ...ingressi(['VALUE', confronto], ['DO', cambia]) }),
    ]);

    const progetto = workspace(
      catena([
        blocco('val_start', 's'),
        blocco('val_set', 'zeroPunti', {
          ...campo('VAR', 'Punti'),
          ...ingresso('VALUE', blocco('val_number', 'zero', campo('NUM', 0))),
        }),
        blocco('val_set', 'zeroNumero', {
          ...campo('VAR', 'Numero'),
          ...ingresso('VALUE', blocco('val_number', 'zero2', campo('NUM', 0))),
        }),
        blocco('val_repeat', 'repeat', {
          ...campo('COUNT', 3),
          ...ingresso('DO', corpo),
        }),
        blocco(
          'val_show',
          'show',
          ingresso('VALUE', blocco('val_variable', 'vend', campo('VAR', 'Punti'))),
        ),
      ]),
    );

    // Le carte di prova della proposta: 3-8-6 danno 2 punti, e solo l'8 e il 6 superano il cinque.
    expect(esegui(progetto, [3, 8, 6])).toBe('Finished uscita[2]');
    // 5-2-9: il 5 non supera il cinque, quindi un punto solo.
    expect(esegui(progetto, [5, 2, 9])).toBe('Finished uscita[1]');
    // Nessuna carta sopra il cinque: zero punti.
    expect(esegui(progetto, [1, 2, 3])).toBe('Finished uscita[0]');
  });

  it('UnProgrammaCheSommaEDisegna_ProduceIlRisultatoAtteso', () => {
    // «porta Punti a 3 + 4» poi «mostra valore di Punti»: la somma è annidata dentro il comando.
    const somma = blocco('val_add', 'add', {
      ...ingressi(
        ['VALUE', blocco('val_number', 'n1', campo('NUM', 3))],
        ['OTHER', blocco('val_number', 'n2', campo('NUM', 4))],
      ),
    });
    const progetto = workspace(
      catena([
        blocco('val_start', 's'),
        blocco('val_set', 'set', { ...campo('VAR', 'Punti'), ...ingresso('VALUE', somma) }),
        blocco(
          'val_show',
          'show',
          ingresso('VALUE', blocco('val_variable', 'v', campo('VAR', 'Punti'))),
        ),
      ]),
    );

    expect(esegui(progetto)).toBe('Finished uscita[7]');
  });

  it('UnProgrammaCheChiedeUnValore_SiFermaInAttesa', () => {
    const progetto = workspace(
      catena([
        blocco('val_start', 's'),
        blocco('val_ask', 'ask', campo('QUESTION', 'Quanto vale?')),
        blocco('val_set', 'memo', {
          ...campo('VAR', 'Numero'),
          ...ingresso('VALUE', blocco('val_answer', 'risposta')),
        }),
        blocco(
          'val_show',
          'show',
          ingresso('VALUE', blocco('val_variable', 'v', campo('VAR', 'Numero'))),
        ),
      ]),
    );

    expect(esegui(progetto)).toBe('WaitingForValue uscita[]');
    expect(esegui(progetto, [42])).toBe('Finished uscita[42]');
  });

  it('UnProgrammaConUnSeEUnConfronto_EsegueIlRamoGiusto', () => {
    const confronto = blocco('val_compare', 'cmp', {
      ...campo('OP', 'Greater'),
      ...ingressi(
        ['VALUE', blocco('val_variable', 'v', campo('VAR', 'Numero'))],
        ['OTHER', blocco('val_number', 'n', campo('NUM', 5))],
      ),
    });
    const ramoAllora = catena([
      blocco('val_show', 'then', ingresso('VALUE', blocco('val_number', 't', campo('NUM', 1)))),
    ]);
    const ramoAltrimenti = catena([
      blocco('val_show', 'else', ingresso('VALUE', blocco('val_number', 'e', campo('NUM', 0)))),
    ]);
    const albero = catena([
      blocco('val_start', 's'),
      blocco('val_ask', 'ask', campo('QUESTION', 'Quanto vale?')),
      blocco('val_set', 'memo', {
        ...campo('VAR', 'Numero'),
        ...ingresso('VALUE', blocco('val_answer', 'risposta')),
      }),
      blocco('val_if', 'if', {
        ...ingressi(['VALUE', confronto], ['DO', ramoAllora], ['ELSE', ramoAltrimenti]),
      }),
    ]);

    // È il costrutto della lezione 4: il confronto è annidato dentro il «se» e decide il ramo. La
    // forma del documento è quella che il server verifica: `{"block":{…}}` dentro l'ingresso.
    expect(esegui(workspace(albero), [9])).toBe('Finished uscita[1]');
    expect(esegui(workspace(albero), [2])).toBe('Finished uscita[0]');
  });

  it('UnSeSenzaConfronto_Rifiuta', () => {
    // Il «se» vuole un confronto dentro: senza, il ramo non si può decidere e il problema lo dice.
    const progetto = workspace(
      catena([
        blocco('val_start', 's'),
        blocco('val_if', 'if', {
          ...ingresso(
            'DO',
            catena([
              blocco(
                'val_show',
                'then',
                ingresso('VALUE', blocco('val_number', 't', campo('NUM', 1))),
              ),
            ]),
          ),
        }),
      ]),
    );

    expect(esegui(progetto)).toContain('vuole un confronto');
  });

  it('UnCicloContato_RipeteIlCorpo', () => {
    const progetto = workspace(
      catena([
        blocco('val_start', 's'),
        blocco('val_set', 'reset', {
          ...campo('VAR', 'Punti'),
          ...ingresso('VALUE', blocco('val_number', 'zero', campo('NUM', 0))),
        }),
        blocco('val_repeat', 'repeat', {
          ...campo('COUNT', 3),
          ...ingresso(
            'DO',
            catena([
              blocco('val_set', 'inc', {
                ...campo('VAR', 'Punti'),
                ...ingresso(
                  'VALUE',
                  blocco('val_add', 'add', {
                    ...ingressi(
                      ['VALUE', blocco('val_variable', 'v', campo('VAR', 'Punti'))],
                      ['OTHER', blocco('val_number', 'uno', campo('NUM', 1))],
                    ),
                  }),
                ),
              }),
            ]),
          ),
        }),
        blocco(
          'val_show',
          'show',
          ingresso('VALUE', blocco('val_variable', 'vend', campo('VAR', 'Punti'))),
        ),
      ]),
    );

    expect(esegui(progetto)).toBe('Finished uscita[3]');
  });
});

describe('Diagnosi di compilazione', () => {
  it('UnProgettoVuoto_DiceCheMancaIlPuntoDiIngresso', () => {
    const esito = esegui(workspace(''));

    expect(esito).toContain('vuoto');
  });

  it('SenzaIlPuntoDiIngresso_IlProblemaNominaIlBloccoCheManca', () => {
    const progetto = workspace(
      catena([
        blocco('val_show', 'show', ingresso('VALUE', blocco('val_number', 'n', campo('NUM', 1)))),
      ]),
    );

    expect(esegui(progetto)).toContain('quando si avvia');
  });

  it('DuePuntiDiIngresso_Rifiutano', () => {
    // Due blocchi appoggiati, non legati in catena: è la forma in cui l'editor li lascia.
    const progetto = workspace(`${blocco('val_start', 's1')},${blocco('val_start', 's2')}`);

    expect(esegui(progetto)).toContain('ne basta uno');
  });

  it('UnaVariabileNonDichiarata_NominaLeVariabiliDellEsercizio', () => {
    const progetto = workspace(
      catena([
        blocco('val_start', 's'),
        blocco(
          'val_show',
          'show',
          ingresso('VALUE', blocco('val_variable', 'v', campo('VAR', 'Sconosciuta'))),
        ),
      ]),
    );

    const esito = esegui(progetto);

    expect(esito).toContain('Sconosciuta');
    expect(esito).toContain('Numero, Punti');
  });

  it('UnValoreMancanteDentroUnComando_NominaIlBlocco', () => {
    const progetto = workspace(catena([blocco('val_start', 's'), blocco('val_show', 'show')]));

    expect(esegui(progetto)).toContain('Manca il valore');
  });

  it('UnConfrontoUsatoComeValore_DiceCheNonSiPuoMostrare', () => {
    const progetto = workspace(
      catena([
        blocco('val_start', 's'),
        blocco(
          'val_show',
          'show',
          ingresso(
            'VALUE',
            blocco('val_compare', 'cmp', {
              ...ingressi(
                ['VALUE', blocco('val_number', 'a', campo('NUM', 1))],
                ['OTHER', blocco('val_number', 'b', campo('NUM', 2))],
              ),
              ...campo('OP', 'Less'),
            }),
          ),
        ),
      ]),
    );

    expect(esegui(progetto)).toContain('non si può sommare né mostrare');
  });

  it('UnSeSenzaConfronto_DiceCheServeUnConfronto', () => {
    const progetto = workspace(
      catena([
        blocco('val_start', 's'),
        blocco('val_if', 'if', ingresso('VALUE', blocco('val_number', 'n', campo('NUM', 1)))),
      ]),
    );

    expect(esegui(progetto)).toContain('vuole un confronto');
  });

  it('UnCicloSenzaConteggio_DiceCheMancaIlNumero', () => {
    const progetto = workspace(catena([blocco('val_start', 's'), blocco('val_repeat', 'repeat')]));

    expect(esegui(progetto)).toContain('Manca quante volte ripetere');
  });

  it('RelazioneNonPrevista_Rifiuta', () => {
    const progetto = workspace(
      catena([
        blocco('val_start', 's'),
        blocco('val_if', 'if', {
          ...ingresso(
            'VALUE',
            blocco('val_compare', 'cmp', {
              ...ingressi(
                ['VALUE', blocco('val_number', 'a', campo('NUM', 1))],
                ['OTHER', blocco('val_number', 'b', campo('NUM', 2))],
              ),
              ...campo('OP', 'Diverso'),
            }),
          ),
        }),
      ]),
    );

    expect(esegui(progetto)).toContain('Diverso');
  });

  it('UnProgettoIllegibile_RifiutaSenzaEseguire', () => {
    const compilato = compileValueProgram('non è json', RULES);

    expect('problems' in compilato).toBe(true);
  });

  it('UnBloccoNonPrevisto_NominaIlTipoCheNonConosce', () => {
    const progetto = workspace(catena([blocco('val_start', 's'), blocco('val_balla', 'x')]));

    expect(esegui(progetto)).toContain('val_balla');
  });

  it('UnProblemaNonSiSegnalaDueVolte_PerLoStessoBlocco', () => {
    const compilato = compileValueProgram(
      workspace(catena([blocco('val_start', 's'), blocco('val_show', 'show')])),
      RULES,
    );

    expect('problems' in compilato && compilato.problems.length).toBe(1);
  });
});

describe('Programmi compilati: le istruzioni sono quelle attese', () => {
  it('LeIstruzioni_RispecchianoLaCatenaDeiComandi', () => {
    const progetto = workspace(
      catena([
        blocco('val_start', 's'),
        blocco('val_ask', 'ask', campo('QUESTION', 'Quanto vale?')),
        blocco(
          'val_show',
          'show',
          ingresso('VALUE', blocco('val_variable', 'v', campo('VAR', 'Numero'))),
        ),
      ]),
    );

    const compilato = compileValueProgram(progetto, RULES);
    expect('instructions' in compilato).toBe(true);
    if (!('instructions' in compilato)) return;

    const attese: ValueInstruction[] = [
      { kind: 'ask', blockId: 'ask', variable: null, question: 'Quanto vale?' },
      { kind: 'show', blockId: 'show', value: { kind: 'variable', variable: 'Numero' } },
    ];
    expect(compilato.instructions).toEqual(attese);
  });
});
