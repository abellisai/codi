import type { FieldTree } from '@angular/forms/signals';
import type { ExerciseDraft } from '@codi/api';
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
