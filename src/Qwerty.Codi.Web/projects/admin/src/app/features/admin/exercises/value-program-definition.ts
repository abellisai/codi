import {
  VALUE_PROGRAM_ENGINE_VERSION,
  VALUE_PROGRAM_EXERCISE_TYPE,
} from '@exercise-engine/value-blocks';
import type { PreviewCase, PreviewVariable } from './value-program-preview';

/** Documento didattico della versione numerica, distinto dal progetto Blockly. */
export interface ValueProgramDefinition {
  readonly schemaVersion: number;
  readonly exerciseType: string;
  readonly engineVersion: string;
  readonly title: string;
  readonly instructions: string;
  readonly variables: readonly PreviewVariable[];
  readonly testCases: readonly PreviewCase[];
  readonly toolbox: { readonly allowedBlocks: readonly string[] };
  readonly limits: {
    readonly maxBlocks: number;
    readonly maxNestingDepth: number;
    readonly maxExecutionSteps: number;
    readonly maxActiveDurationMs: number;
  };
  readonly feedback: { readonly success: string; readonly collision: string };
}

/** Legge solo la forma conosciuta; la validazione didattica completa resta al server. */
export function parseValueProgramDefinition(json: string): ValueProgramDefinition | null {
  let value: unknown;
  try {
    value = JSON.parse(json);
  } catch {
    return null;
  }
  if (
    !isRecord(value) ||
    value['schemaVersion'] !== 1 ||
    value['exerciseType'] !== VALUE_PROGRAM_EXERCISE_TYPE ||
    value['engineVersion'] !== VALUE_PROGRAM_ENGINE_VERSION ||
    typeof value['title'] !== 'string' ||
    typeof value['instructions'] !== 'string'
  )
    return null;
  const variables = value['variables'];
  const cases = value['testCases'];
  const toolbox = value['toolbox'];
  const limits = value['limits'];
  const feedback = value['feedback'];
  if (
    !Array.isArray(variables) ||
    !variables.every(
      (variable) =>
        isRecord(variable) &&
        typeof variable['name'] === 'string' &&
        Number.isInteger(variable['initialValue']),
    ) ||
    !Array.isArray(cases) ||
    !cases.every(
      (caso) =>
        isRecord(caso) &&
        Array.isArray(caso['inputs']) &&
        caso['inputs'].every(Number.isInteger) &&
        Array.isArray(caso['expectedOutput']) &&
        caso['expectedOutput'].every((line) => typeof line === 'string'),
    ) ||
    !isRecord(toolbox) ||
    !Array.isArray(toolbox['allowedBlocks']) ||
    !toolbox['allowedBlocks'].every((block) => typeof block === 'string') ||
    !isRecord(limits) ||
    !['maxBlocks', 'maxNestingDepth', 'maxExecutionSteps', 'maxActiveDurationMs'].every(
      (name) => Number.isInteger(limits[name]) && Number(limits[name]) > 0,
    ) ||
    !isRecord(feedback) ||
    typeof feedback['success'] !== 'string' ||
    typeof feedback['collision'] !== 'string'
  )
    return null;
  return value as unknown as ValueProgramDefinition;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
