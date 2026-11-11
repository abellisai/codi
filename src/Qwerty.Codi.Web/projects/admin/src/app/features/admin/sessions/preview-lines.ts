/** Riga del programma mostrato nell'anteprima: profondità della ripetizione e testo del blocco. */
export interface PreviewLine {
  readonly depth: number;
  readonly label: string;
}

import { findEngine } from '@exercise-engine/engine-registry';

/** Oltre questo numero di blocchi l'anteprima si tronca: resta leggibile, ma non nasconde nulla. */
const MAX_PREVIEW_LINES = 60;

/**
 * Legge il lavoro dell'alunno come elenco di righe da mostrare al docente (§15.5).
 *
 * Il lavoro ha due forme, e la versione del motore fissata dall'assegnazione dice quale: un progetto
 * a blocchi si mostra come sequenza di blocchi in italiano, un verbale di risposta come la risposta
 * che il bambino ha scritto. Leggere l'uno con le regole dell'altro non darebbe nulla — un verbale
 * non ha blocchi — e il docente vedrebbe una card vuota senza capire perché.
 */
export function describeWork(
  workspaceJson: string | null,
  engineVersion: string,
): readonly PreviewLine[] {
  if (workspaceJson === null) return [];

  return findEngine(engineVersion)?.blockly === true
    ? describeWorkspaceBlocks(workspaceJson)
    : describeAnswerRecord(workspaceJson);
}

/**
 * Legge un verbale di risposta come riga di anteprima. Una risposta vuota è «non ha ancora
 * risposto», che è un'informazione e non un'assenza: il docente deve poterla distinguere da un
 * lavoro che non è arrivato.
 */
export function describeAnswerRecord(workspaceJson: string): readonly PreviewLine[] {
  try {
    const answer = (JSON.parse(workspaceJson) as { answer?: unknown }).answer;
    const text = typeof answer === 'string' ? answer.trim() : '';

    return [
      {
        depth: 0,
        label: text === '' ? 'Nessuna risposta scritta' : `Risposta: ${text}`,
      },
    ];
  } catch {
    // Un verbale illeggibile non inventa una risposta: il docente vede che non c'è nulla da mostrare.
    return [];
  }
}

/**
 * Legge il progetto dell'alunno come elenco di blocchi in italiano (§15.5). L'anteprima è un
 * progetto Blockly: qui si mostra la sequenza e le ripetizioni annidate, senza un editor. Un
 * progetto illeggibile non inventa blocchi: si dichiara vuoto e il docente vede che non c'è nulla
 * da mostrare.
 *
 * La traduzione sta in un posto solo, usato dalla pagina che mostra l'anteprima; il componente che
 * la rende non interpreta il JSON, riceve le righe già lette.
 */
export function describeWorkspaceBlocks(workspaceJson: string | null): readonly PreviewLine[] {
  if (workspaceJson === null) return [];
  const lines: PreviewLine[] = [];
  try {
    const root = (JSON.parse(workspaceJson) as { blocks?: { blocks?: readonly unknown[] } }).blocks;
    const first = root?.blocks?.[0];
    if (first !== undefined) walk(first, 0, lines);
  } catch {
    return [];
  }

  return lines;
}

/** Percorre la catena dei blocchi e le ripetizioni annidate, con un limite di righe. */
function walk(block: unknown, depth: number, lines: PreviewLine[]): void {
  if (lines.length >= MAX_PREVIEW_LINES || typeof block !== 'object' || block === null) return;
  const node = block as {
    type?: unknown;
    fields?: Record<string, unknown>;
    inputs?: Record<string, { block?: unknown }>;
    next?: { block?: unknown };
  };
  const type = typeof node.type === 'string' ? node.type : '';
  const label = blockLabel(type, node.fields ?? {});
  if (label === null) return;
  lines.push({ depth, label });
  for (const input of Object.values(node.inputs ?? {})) {
    if (input?.block !== undefined) walk(input.block, depth + 1, lines);
  }
  if (node.next?.block !== undefined) walk(node.next.block, depth, lines);
}

/** Testo italiano del blocco; `null` quando il tipo non appartiene alla tavolozza dell'esercizio. */
function blockLabel(type: string, fields: Record<string, unknown>): string | null {
  switch (type) {
    case 'aula_start':
      return 'quando si avvia';
    case 'aula_move':
      return 'fai un passo';
    case 'aula_turn':
      return Object.values(fields)[0] === 'Left' ? 'gira a sinistra' : 'gira a destra';
    case 'aula_repeat':
      return `ripeti ${String(Object.values(fields)[0] ?? '')} volte`;
    default:
      return null;
  }
}
