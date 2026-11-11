/**
 * Definizione didattica `Quiz` (§13.9.1) e sua composizione dai campi dell'editor.
 *
 * Come per `GridPath`, queste funzioni sono pure: non conoscono Angular, il dispatcher o il DOM. La
 * pagina vi appoggia il modulo e il salvataggio, quindi la stessa regola — per esempio che la
 * risposta attesa non entra mai nella definizione — vale per ciò che si vede e per il JSON inviato.
 *
 * La differenza che conta rispetto a `GridPath`: **la risposta attesa non fa parte della
 * definizione**. Vive nella soluzione privata della versione pubblicata, che il catalogo riserva al
 * docente e che non esce mai verso gli alunni (§13.1). Se stesse qui, il documento che il browser
 * dell'alunno riceve conterrebbe la soluzione.
 */

/** Tipo di esercizio e versione del motore dichiarati nella definizione. */
export const QUIZ_EXERCISE_TYPE = 'Quiz';
export const QUIZ_ENGINE_VERSION = 'quiz-1';
export const QUIZ_SCHEMA_VERSION = 1;

/** Modo di confronto della risposta, come il server lo dichiara. */
export const QUIZ_MATCHES = ['Text', 'Integer'] as const;
export type QuizMatch = (typeof QUIZ_MATCHES)[number];

/** Nomi comprensibili dei modi di confronto, mostrati nel selettore. */
export const MATCH_LABELS: Readonly<Record<QuizMatch, string>> = {
  Text: 'Testo: uguale senza distinguere maiuscole e spazi ai bordi',
  Integer: 'Numero: la stessa quantità, anche con zeri davanti',
};

/** Limiti dei testi, allineati al dominio (`QuizData`). */
export const MAX_STIMULUS_LENGTH = 4000;
export const MAX_PROMPT_LENGTH = 4000;
export const MAX_ANSWER_LENGTH = 200;
export const MAX_MESSAGE_LENGTH = 500;

/**
 * Limiti didattici della definizione. Un esercizio dichiarativo non compone blocchi e non esegue
 * nulla: i quattro valori restano quelli della definizione (sono comuni a ogni tipo) e valgono 1.
 * Il server li accetta e non li usa per questo tipo.
 */
export const QUIZ_LIMITS = {
  maxBlocks: 1,
  maxNestingDepth: 1,
  maxExecutionSteps: 1,
  maxActiveDurationMs: 1000,
} as const;

/** Definizione come viaggia in `definitionJson`. */
export interface QuizDefinition {
  readonly schemaVersion: number;
  readonly exerciseType: string;
  readonly engineVersion: string;
  readonly title: string;
  readonly instructions: string;
  readonly toolbox: { readonly allowedBlocks: readonly string[] };
  readonly limits: typeof QUIZ_LIMITS;
  readonly quiz: {
    readonly stimulus: string;
    readonly prompt: string;
    readonly match: string;
  };
  readonly feedback: { readonly success: string; readonly collision: string };
}

/** Stato modificabile dell'editor. */
export interface QuizEditorModel {
  readonly stimulus: string;
  readonly prompt: string;
  readonly match: QuizMatch;
  readonly successMessage: string;
  readonly wrongMessage: string;
}

/** Modello suggerito per una bozza nuova: i campi vuoti con il confronto testuale. */
export function suggestedQuizModel(): QuizEditorModel {
  return {
    stimulus: '',
    prompt: '',
    match: 'Text',
    successMessage: 'Giusto!',
    wrongMessage: 'Riprova: rileggi la rappresentazione.',
  };
}

/**
 * Compone la definizione da inviare. La risposta attesa non compare: quello che l'alunno deve
 * produrre è il contenuto della soluzione privata, composta a parte.
 */
export function buildQuizDefinition(title: string, model: QuizEditorModel): QuizDefinition {
  return {
    schemaVersion: QUIZ_SCHEMA_VERSION,
    exerciseType: QUIZ_EXERCISE_TYPE,
    engineVersion: QUIZ_ENGINE_VERSION,
    title,
    instructions: model.prompt.trim(),
    toolbox: { allowedBlocks: [] },
    limits: QUIZ_LIMITS,
    quiz: {
      stimulus: model.stimulus.trim(),
      prompt: model.prompt.trim(),
      match: model.match,
    },
    feedback: {
      success: model.successMessage.trim(),
      collision: model.wrongMessage.trim(),
    },
  };
}

/** Converte una definizione letta dal server nel modello dell'editor. */
export function toQuizModel(definition: QuizDefinition): QuizEditorModel {
  const match = QUIZ_MATCHES.find((candidate) => candidate === definition.quiz?.match) ?? 'Text';

  return {
    stimulus: definition.quiz?.stimulus ?? '',
    prompt: definition.quiz?.prompt ?? '',
    match,
    successMessage: definition.feedback?.success ?? '',
    wrongMessage: definition.feedback?.collision ?? '',
  };
}

/** L'ordine delle proprietà e gli escape del server non rappresentano modifiche dell'autore. */
export function canonicalQuizDefinition(json: string): string {
  const definition = parseQuizDefinition(json);
  return definition === null
    ? json
    : JSON.stringify(buildQuizDefinition(definition.title, toQuizModel(definition)));
}

/**
 * Legge la `definitionJson` del server. Restituisce `null` per un testo che non è una definizione
 * `Quiz` utilizzabile: la pagina lo dice e non lo sovrascrive con valori suggeriti.
 */
export function parseQuizDefinition(json: string): QuizDefinition | null {
  let value: unknown;
  try {
    value = JSON.parse(json);
  } catch {
    return null;
  }

  if (!isRecord(value) || !isRecord(value['quiz']) || !isRecord(value['feedback'])) {
    return null;
  }
  if (!isRecord(value['toolbox']) || !Array.isArray(value['toolbox']['allowedBlocks'])) {
    return null;
  }
  if (
    typeof value['schemaVersion'] !== 'number' ||
    value['exerciseType'] !== QUIZ_EXERCISE_TYPE ||
    value['engineVersion'] !== QUIZ_ENGINE_VERSION ||
    typeof value['title'] !== 'string' ||
    typeof value['instructions'] !== 'string' ||
    typeof value['quiz']['stimulus'] !== 'string' ||
    typeof value['quiz']['prompt'] !== 'string' ||
    typeof value['quiz']['match'] !== 'string'
  ) {
    return null;
  }

  return value as unknown as QuizDefinition;
}

/**
 * Il verbale di risposta attesa, nella forma che il server confronta: è il contenuto della soluzione
 * privata della versione, non della definizione.
 */
export function answerSolution(answer: string): string {
  return JSON.stringify({ answer });
}

/** Legge la risposta attesa dalla soluzione privata; vuota quando non ce n'è una. */
export function readAnswerSolution(solutionJson: string | null): string {
  if (solutionJson === null || solutionJson.trim() === '') {
    return '';
  }

  try {
    const value: unknown = JSON.parse(solutionJson);
    const answer = isRecord(value) ? value['answer'] : null;

    return typeof answer === 'string' ? answer : '';
  } catch {
    return '';
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
