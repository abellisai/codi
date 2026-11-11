import { compileValueProgram } from '@exercise-engine/value-program-compiler';
import { ValueProgramRunner } from '@exercise-engine/value-program-runner';

/**
 * L'anteprima dell'autore: esegue il programma su **tutti** i casi di prova e dice, per ciascuno, che
 * cosa ha mostrato e se è quello che doveva.
 *
 * È una funzione pura e non un metodo del componente perché è la parte che vale la pena provare: la
 * form la monta, il calcolo decide se un esercizio è soddisfacibile. Ed è la cosa che manca di più a
 * chi progetta un esercizio — senza, i casi si scrivono alla cieca e si scopre dagli alunni se sono
 * possibili.
 *
 * Si mostrano **tutti** i casi e non solo il primo che non torna: un esercizio può avere il primo
 * caso giusto e il terzo impossibile, e fermarsi al primo errore nasconderebbe proprio il caso che
 * conta.
 *
 * Usa lo **stesso** compilatore e lo **stesso** motore degli alunni: un'anteprima che eseguisse un
 * altro codice direbbe una verità che in aula non vale.
 */
export interface PreviewRow {
  /** I valori che il caso dà al programma, come li legge il docente. */
  readonly inputs: string;
  /** Che cosa il caso pretende che il programma mostri. */
  readonly expected: string;
  /** Che cosa il programma ha mostrato davvero, o perché non si esegue. */
  readonly shown: string;
  /** Vero quando il caso è superato: il programma mostra quello che doveva. */
  readonly passed: boolean;
}

/** Esito dell'anteprima: le righe, oppure i problemi che impediscono di eseguire il programma. */
export type PreviewResult =
  { readonly rows: readonly PreviewRow[] } | { readonly problems: readonly string[] };

/** Variabile dichiarata dall'esercizio, come la legge il calcolo dell'anteprima. */
export interface PreviewVariable {
  readonly name: string;
  readonly initialValue: number;
}

/** Caso di prova, come lo legge il calcolo dell'anteprima. */
export interface PreviewCase {
  readonly inputs: readonly number[];
  readonly expectedOutput: readonly string[];
}

export function previewCases(
  workspaceJson: string,
  variables: readonly PreviewVariable[],
  testCases: readonly PreviewCase[],
): PreviewResult {
  const names = variables.map((variable) => variable.name);
  const defaults = Object.fromEntries(variables.map((v) => [v.name, v.initialValue]));

  const compilato = compileValueProgram(workspaceJson, { variables: names, defaults });
  if ('problems' in compilato) {
    return { problems: compilato.problems.map((problema) => problema.message) };
  }

  const rows: PreviewRow[] = [];
  for (const caso of testCases) {
    const stato = ValueProgramRunner.start(
      compilato.instructions,
      names,
      defaults,
      caso.inputs,
      1000,
    ).runToRest();

    // Una corsa che aspetta un valore non ha finito: il caso non dà abbastanza valori, e la colpa è
    // dell'esercizio, non dell'alunno. Si mostra il motivo invece di un'uscita vuota.
    const eseguito = stato.status === 'Finished';
    const mostra = stato.output.join(' ');
    const atteso = caso.expectedOutput.join(' ');

    rows.push({
      inputs: caso.inputs.join(', '),
      expected: atteso,
      shown: eseguito ? mostra : stato.pauseReason,
      passed: eseguito && mostra === atteso,
    });
  }

  return { rows };
}
