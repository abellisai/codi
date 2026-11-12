import type { FieldTree } from '@angular/forms/signals';
import type { ExerciseDraft } from '@codi/api';
/**
 * Bozza pronta per la verifica o la pubblicazione: i documenti correnti dell'editor sostituiscono
 * quelli dell'ultima risposta del server, così la prova riguarda ciò che il docente ha a schermo.
 */
export type PreparedExerciseDraft = ExerciseDraft & {
  readonly referenceSolutionJson: string | null;
};

export type PublicationStatus = 'Draft' | 'Available' | 'UnpublishedChanges';
export const PUBLICATION_LABELS: Record<PublicationStatus, string> = {
  Draft: 'Bozza',
  Available: 'Disponibile',
  UnpublishedChanges: 'Modifiche da pubblicare',
};
export interface PublicationIssue {
  readonly message: string;
  readonly focus: () => void;
}

/** Ogni messaggio conserva il controllo associato dal validatore della form. */
export function formPublicationIssues<T>(tree: FieldTree<T>): readonly PublicationIssue[] {
  return tree()
    .errorSummary()
    .map((error) => ({
      message: error.message ?? 'Completa il campo indicato.',
      focus: () => {
        error.fieldTree().focusBoundControl();
        document.activeElement?.scrollIntoView?.({ block: 'center' });
      },
    }));
}

/** Associa i problemi del server alle sezioni note dell'editor, senza nascondere il messaggio. */
export function focusPublicationProblem(type: string, message: string): void {
  const text = message.toLocaleLowerCase('it');
  let id =
    type === 'Quiz'
      ? 'quiz-prompt'
      : type === 'ValueProgram'
        ? 'value-instructions'
        : 'editor-instructions';
  if (type === 'Quiz' && /risposta|soluzione/.test(text)) id = 'quiz-answer';
  else if (type === 'ValueProgram' && /variabil/.test(text)) id = 'value-variables';
  else if (type === 'ValueProgram' && /caso|casi|uscita/.test(text)) id = 'value-cases';
  else if (/blocch|progetto|ingresso/.test(text))
    id = type === 'ValueProgram' ? 'value-program-title' : 'editor-solution-title';
  const element = document.getElementById(id);
  if (!element) return;
  element.scrollIntoView?.({ block: 'center' });
  if (!element.matches('input, textarea, select, button, a'))
    element.setAttribute('tabindex', '-1');
  element.focus();
}

/**
 * Normalizza la sola formattazione di un documento Blockly, senza toccare i blocchi. Il confronto
 * «modifiche non salvate» usa questa forma: Blockly può riscrivere l'ordine o il formato del vuoto
 * (`{}` contro `{ blocks: { languageVersion: 0, blocks: [] } }`) senza che il programma sia cambiato,
 * e un confronto letterale segnalerebbe modifiche che non esistono.
 */
export function canonicalWorkspace(json: string): string {
  try {
    const parsed: unknown = JSON.parse(json);
    const blocks = (parsed as { blocks?: { blocks?: unknown } })?.blocks?.blocks;
    if (Array.isArray(blocks) && blocks.length === 0) {
      return '{}';
    }
    return JSON.stringify(parsed);
  } catch {
    return json;
  }
}

/** Vero quando il documento Blockly non contiene blocchi: è il progetto vuoto del programma iniziale. */
export function isCanonicalEmptyWorkspace(json: string): boolean {
  return canonicalWorkspace(json) === '{}';
}
