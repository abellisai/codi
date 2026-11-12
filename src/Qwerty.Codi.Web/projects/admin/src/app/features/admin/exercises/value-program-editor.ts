import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  signal,
  OnInit,
  viewChild,
} from '@angular/core';
import { disabled, form, FormField, FormRoot, validate, submit } from '@angular/forms/signals';
import { NgTemplateOutlet } from '@angular/common';
import { formPublicationIssues, type PreparedExerciseDraft } from './publication-feedback';
import { RouterLink } from '@angular/router';
import {
  ApiError,
  ApiService,
  type ExerciseDraft,
  type ExerciseReferenceSolution,
  toApiError,
} from '@codi/api';
import {
  VALUE_PROGRAM_BLOCK_TYPES,
  VALUE_PROGRAM_ENGINE_VERSION,
  VALUE_PROGRAM_EXERCISE_TYPE,
} from '@exercise-engine/value-blocks';
import { textRules } from '@shared/forms';
import { previewCases, type PreviewRow } from './value-program-preview';
import { ValueProgramProgramEditor } from './value-program-program-editor/value-program-program-editor';
import { ExercisePublication } from './exercise-publication/exercise-publication';
import {
  parseValueProgramDefinition,
  type ValueProgramDefinition,
} from './value-program-definition';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';

/** Autore numerico: conserva la revisione salvata e pubblica soltanto una bozza senza modifiche. */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-value-program-editor',
  imports: [
    RouterLink,
    HlmButtonImports,
    HlmFieldImports,
    HlmInputImports,
    HlmTextareaImports,
    ValueProgramProgramEditor,
    ExercisePublication,
    FormField,
    FormRoot,
    NgTemplateOutlet,
  ],
  templateUrl: './value-program-editor.html',
  styleUrl: './value-program-editor.css',
})
export class ValueProgramEditor implements OnInit {
  protected readonly publication = viewChild(ExercisePublication);
  private readonly latestDraft = signal<ExerciseDraft | null>(null);
  private readonly publicationAttempted = signal(false);
  protected readonly publicationIssues = computed(() =>
    this.publicationAttempted() ? formPublicationIssues(this.editorForm) : [],
  );
  /**
   * Prepara la bozza per la verifica o la pubblicazione. Il salvataggio che serve alla pubblicazione
   * non annuncia un successo proprio: la conferma è una sola, quella della pubblicazione (§14.2).
   */
  private publishingDraft = false;
  protected readonly prepareForPublication = async (
    forPublication: boolean,
  ): Promise<PreparedExerciseDraft | null> => {
    this.publicationAttempted.set(forPublication);
    this.error.set(null);
    this.publishingDraft = forPublication;
    try {
      await submit(this.editorForm);
    } finally {
      this.publishingDraft = false;
    }
    const draft = this.latestDraft();
    return this.error() !== null || this.editorForm().invalid() || this.changed() || draft === null
      ? null
      : { ...draft, referenceSolutionJson: this.solutionJson() };
  };
  private readonly api = inject(ApiService);
  readonly exerciseId = input.required<string>();
  readonly title = input.required<string>();

  private readonly model = signal({
    instructions: '',
    variablesText: '',
    testCasesText: '',
    successMessage: 'Bravo!',
    wrongMessage: 'Riprova.',
  });
  private readonly savedModel = signal<string | null>(null);
  private readonly savedTitle = signal('');
  private readonly savedWorkspace = signal(emptyWorkspace());
  private readonly loadedDefinition = signal<ValueProgramDefinition | null>(null);
  protected readonly ready = signal(false);
  protected readonly savedRowVersion = signal<string | null>(null);
  protected readonly solutionJson = signal<string | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<ApiError | null>(null);

  /** Il progetto iniziale può essere vuoto o incompleto. La soluzione privata resta distinta. */
  protected readonly workspaceJson = signal(emptyWorkspace());
  protected readonly allowedBlocks = computed(
    () => this.loadedDefinition()?.toolbox.allowedBlocks ?? VALUE_PROGRAM_BLOCK_TYPES,
  );
  protected readonly preview = signal<readonly PreviewRow[] | null>(null);
  protected readonly previewProblem = signal('');
  protected readonly variables = computed(() => leggiVariabili(this.model().variablesText).voci);
  protected readonly testCases = computed(() => leggiCasi(this.model().testCasesText).voci);

  protected readonly editorForm = form(
    this.model,
    (path) => {
      disabled(path, () => this.busy() || !this.ready() || this.publication()?.locking() === true);
      textRules(path.instructions, 'Consegna', 2000);
      textRules(path.successMessage, 'Messaggio di programma corretto', 500);
      textRules(path.wrongMessage, 'Messaggio di programma da correggere', 500);
      validate(path.variablesText, ({ value }) => {
        const parsed = leggiVariabili(value());
        const message =
          parsed.errore ?? (parsed.voci.length === 0 ? 'Dichiara almeno una variabile.' : null);
        return message === null ? undefined : { kind: 'variables', message };
      });
      validate(path.testCasesText, ({ value }) => {
        const parsed = leggiCasi(value());
        const message =
          parsed.errore ?? (parsed.voci.length === 0 ? 'Scrivi almeno un caso di prova.' : null);
        return message === null ? undefined : { kind: 'testCases', message };
      });
    },
    {
      submission: {
        action: async () => {
          await this.save();
        },
        ignoreValidators: 'none',
        onInvalid: (field) => field().errorSummary()[0]?.fieldTree().focusBoundControl(),
      },
    },
  );

  /** I limiti e il catalogo letti dal server restano quelli della bozza, anche dopo una copia. */
  protected readonly definitionJson = computed(() =>
    JSON.stringify({
      schemaVersion: 1,
      exerciseType: VALUE_PROGRAM_EXERCISE_TYPE,
      engineVersion: VALUE_PROGRAM_ENGINE_VERSION,
      title: this.title(),
      instructions: this.model().instructions.trim(),
      variables: this.variables(),
      testCases: this.testCases(),
      toolbox: { allowedBlocks: this.allowedBlocks() },
      limits: this.loadedDefinition()?.limits ?? {
        maxBlocks: 40,
        maxNestingDepth: 8,
        maxExecutionSteps: 1000,
        maxActiveDurationMs: 120000,
      },
      feedback: {
        success: this.model().successMessage.trim(),
        collision: this.model().wrongMessage.trim(),
      },
    }),
  );

  protected readonly changed = computed(
    () =>
      this.savedModel() === null ||
      JSON.stringify(this.model()) !== this.savedModel() ||
      this.title() !== this.savedTitle() ||
      canonicalWorkspace(this.workspaceJson()) !== canonicalWorkspace(this.savedWorkspace()),
  );
  protected readonly publicationBlocked = computed(
    () => !this.ready() || this.busy() || this.changed(),
  );

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async save(): Promise<void> {
    if (this.busy() || !this.ready() || !this.changed() || this.editorForm().invalid()) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      const payload: Record<string, unknown> = {
        exerciseId: this.exerciseId(),
        definitionJson: this.definitionJson(),
        workspaceJson: this.workspaceJson(),
        referenceSolutionJson: this.solutionJson(),
      };
      const version = this.savedRowVersion();
      if (version !== null) payload['baseRowVersion'] = version;
      const draft = await this.api.executeCommand<ExerciseDraft>('SaveExerciseDraft', payload, {
        // Dentro «Rendi disponibile» il salvataggio è un passo interno: la conferma è una sola.
        silentSuccess: this.publishingDraft,
      });
      this.applyDraft(draft);
    } catch (failure) {
      this.error.set(toApiError(failure));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.ready.set(false);
    this.error.set(null);
    try {
      const draft = await this.api.executeQuery<ExerciseDraft>(
        'GetExerciseDraft',
        { exerciseId: this.exerciseId() },
        { notFoundIsExpected: true },
      );
      this.applyDraft(draft);
      await this.loadSolution();
      this.ready.set(true);
    } catch (failure) {
      const problem = toApiError(failure);
      if (problem.status === 404) {
        this.savedRowVersion.set(null);
        this.loadedDefinition.set(null);
        this.solutionJson.set(null);
        this.savedModel.set(null);
        this.workspaceJson.set(emptyWorkspace());
        this.ready.set(true);
      } else {
        this.error.set(problem);
      }
    } finally {
      this.loading.set(false);
    }
  }

  private applyDraft(draft: ExerciseDraft): void {
    const definition = parseValueProgramDefinition(draft.definitionJson);
    if (definition === null)
      throw new ApiError(
        'DefinitionUnreadable',
        0,
        'La bozza salvata non è leggibile con questa versione dell’editor.',
      );
    this.latestDraft.set(draft);
    this.loadedDefinition.set(definition);
    this.model.set({
      instructions: definition.instructions,
      variablesText: definition.variables
        .map((v) => `${v.name} = ${String(v.initialValue)}`)
        .join('\n'),
      testCasesText: definition.testCases
        .map((c) => `${c.inputs.join(', ')} -> ${c.expectedOutput.join(' ')}`)
        .join('\n'),
      successMessage: definition.feedback.success,
      wrongMessage: definition.feedback.collision,
    });
    this.savedRowVersion.set(draft.rowVersion);
    this.savedModel.set(JSON.stringify(this.model()));
    this.savedTitle.set(definition.title);
    this.workspaceJson.set(draft.workspaceJson);
    this.savedWorkspace.set(draft.workspaceJson);
    this.preview.set(null);
    this.previewProblem.set('');
    this.editorForm().reset();
  }

  private async loadSolution(): Promise<void> {
    try {
      const solution = await this.api.executeQuery<ExerciseReferenceSolution>(
        'GetReferenceSolution',
        { exerciseId: this.exerciseId() },
        { notFoundIsExpected: true },
      );
      this.solutionJson.set(solution.workspaceJson);
    } catch (failure) {
      if (toApiError(failure).status !== 404) throw failure;
      this.solutionJson.set(null);
    }
  }

  protected async onDraftReplaced(): Promise<void> {
    await this.load();
  }

  protected prova(): void {
    this.preview.set(null);
    this.previewProblem.set('');
    const result = previewCases(this.workspaceJson(), this.variables(), this.testCases());
    if ('problems' in result) {
      this.previewProblem.set(result.problems.join(' '));
      return;
    }
    this.preview.set(result.rows);
  }

  protected onWorkspace(json: string): void {
    this.workspaceJson.set(json);
    this.preview.set(null);
    this.previewProblem.set('');
  }
}

/** Ignora soltanto la formattazione del documento, senza cambiare i blocchi. */
function canonicalWorkspace(json: string): string {
  try {
    return JSON.stringify(JSON.parse(json));
  } catch {
    return json;
  }
}
/** Il progetto vuoto: è la condizione di partenza dell'editor, e il valore iniziale del segnale. */
function emptyWorkspace(): string {
  return JSON.stringify({ blocks: { languageVersion: 0, blocks: [] } });
}

/** Una variabile dichiarata: nome e valore di partenza. */
export interface VariabileVoce {
  readonly name: string;
  readonly initialValue: number;
}

/** Un caso di prova: i valori da leggere e l'uscita attesa. */
export interface CasoVoce {
  readonly inputs: number[];
  readonly expectedOutput: string[];
}

interface EsitoLettura<T> {
  readonly voci: T[];
  readonly errore: string | null;
}

/**
 * Legge le variabili da righe del tipo `Punti = 0`. Una riga vuota si ignora; una riga che non si
 * legge ferma tutto e lo dice, perché comporre metà documento è peggio che non salvarlo.
 */
export function leggiVariabili(testo: string): EsitoLettura<VariabileVoce> {
  const voci: VariabileVoce[] = [];

  for (const [indice, riga] of testo.split('\n').entries()) {
    const pulita = riga.trim();
    if (pulita.length === 0) continue;

    const pezzi = pulita.split('=');
    if (pezzi.length !== 2) {
      return { voci: [], errore: `Riga ${String(indice + 1)}: scrivi «Nome = valore».` };
    }

    const nome = pezzi[0].trim();
    const valore = Number.parseInt(pezzi[1].trim(), 10);

    if (nome.length === 0 || !/^[A-Za-z][A-Za-z0-9_]*$/.test(nome)) {
      return {
        voci: [],
        errore: `Riga ${String(indice + 1)}: il nome della variabile comincia con una lettera e non ha spazi.`,
      };
    }

    if (Number.isNaN(valore)) {
      return {
        voci: [],
        errore: `Riga ${String(indice + 1)}: il valore deve essere un numero intero.`,
      };
    }

    if (voci.some((voce) => voce.name === nome)) {
      return {
        voci: [],
        errore: `Riga ${String(indice + 1)}: la variabile «${nome}» è già dichiarata.`,
      };
    }

    voci.push({ name: nome, initialValue: valore });
  }

  return { voci, errore: null };
}

/**
 * Legge i casi di prova da righe del tipo `3, 8, 6 -> 2`. I valori letti si separano con virgole.
 * L'uscita attesa si separa con spazi o con virgole: le due notazioni sono equivalenti.
 */
export function leggiCasi(testo: string): EsitoLettura<CasoVoce> {
  const voci: CasoVoce[] = [];

  for (const [indice, riga] of testo.split('\n').entries()) {
    const pulita = riga.trim();
    if (pulita.length === 0) continue;

    const freccia = pulita.indexOf('->');
    if (freccia < 0) {
      return { voci: [], errore: `Riga ${String(indice + 1)}: scrivi «valori -> uscita attesa».` };
    }

    const valoriTesto = pulita.slice(0, freccia).trim();
    const uscitaTesto = pulita.slice(freccia + 2).trim();

    const inputs: number[] = [];
    if (valoriTesto.length > 0) {
      for (const pezzo of valoriTesto.split(',')) {
        const valore = Number.parseInt(pezzo.trim(), 10);
        if (Number.isNaN(valore)) {
          return {
            voci: [],
            errore: `Riga ${String(indice + 1)}: «${pezzo.trim()}» non è un numero intero.`,
          };
        }

        inputs.push(valore);
      }
    }

    const expectedOutput =
      uscitaTesto.length === 0
        ? []
        : uscitaTesto.split(/[,\s]+/).filter((pezzo) => pezzo.length > 0);

    if (expectedOutput.length === 0) {
      return { voci: [], errore: `Riga ${String(indice + 1)}: manca l'uscita attesa.` };
    }

    voci.push({ inputs, expectedOutput });
  }

  return { voci, errore: null };
}
